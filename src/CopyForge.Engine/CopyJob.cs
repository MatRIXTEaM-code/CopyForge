using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CopyForge.Engine;

public sealed class CopyJob
{
	private sealed record FileItem(string Src, string Dst, long Size, DateTime MtimeUtc, bool SkipCopy, DateTime CtimeUtc = default(DateTime), FileAttributes Attrs = FileAttributes.None);

	private sealed record DirPair(string Src, string Dst, DateTime CreatedUtc, DateTime WrittenUtc);

	private enum Decision
	{
		Copy,
		SkipIdentical,
		SkipPolicy
	}

	private sealed class Plan
	{
		public readonly List<FileItem> Files = new List<FileItem>();

		public readonly List<string> DirsToCreate = new List<string>();

		public readonly List<string> SourceDirs = new List<string>();

		public readonly List<string> SyncRoots = new List<string>();

		public readonly List<DirPair> DirPairs = new List<DirPair>();

		public readonly HashSet<string> Expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class VerifyException : IOException
	{
		public VerifyException(string m)
			: base(m)
		{
		}
	}

	private readonly record struct Strategy(bool Native, bool Direct, int Workers, bool Split);

	private sealed class Target
	{
		public string[] Final = Array.Empty<string>();

		public string[] Tmp = Array.Empty<string>();

		public long PartSize = long.MaxValue;

		public SafeFileHandle?[] H = Array.Empty<SafeFileHandle>();

		public bool InPlace;

		public bool MetaSet;

		public void Close()
		{
			for (int i = 0; i < H.Length; i++)
			{
				H[i]?.Dispose();
				H[i] = null;
			}
		}
	}

	private static readonly EnumerationOptions EnumOpts = new EnumerationOptions
	{
		IgnoreInaccessible = true,
		AttributesToSkip = FileAttributes.None,
		RecurseSubdirectories = false,
		ReturnSpecialDirectories = false
	};

	private readonly JobOptions _o;

	private readonly PathFilter _filter;

	private readonly Stopwatch _sw = new Stopwatch();

	private readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();

	private readonly ConcurrentQueue<FilePair> _failedPairs = new ConcurrentQueue<FilePair>();

	private readonly ConcurrentQueue<string> _identical = new ConcurrentQueue<string>();

	private const long Fat32MaxBytes = 4294967295L;

	private string _dest = "";

	private string _destFs = "";

	private long _destFree = -1L;

	private VolInfo _srcVol;

	private VolInfo _dstVol;

	private int _threads = 4;

	private bool _extras;

	private readonly int _buf;

	private const long InPlaceMax = 4194304L;

	private long _ticksCopy;

	private long _ticksFinish;

	private long _ticksScan;

	private int _nInPlace;

	private int _whyLogged;

	private volatile bool _metaViaHandle = true;

	private volatile bool _metaNativeOk = true;

	private int _metaChecks;

	private readonly ConcurrentDictionary<string, byte> _dirsMade = new ConcurrentDictionary<string, byte>();

	private long _filesTotal;

	private long _filesDone;

	private long _bytesTotal;

	private long _bytesDone;

	private long _skipped;

	private long _failed;

	private Throttle? _throttle;

	private JobLog? _log;

	private CancellationToken _jobCt;

	private long _deleted;

	private long _nNative;

	private long _nClone;

	private int _cloneFails;

	private long _nDirect;

	private long _nChunk;

	private long _nBuffered;

	private long _nSplit;

	private long _nRetries;

	private volatile string _current = "";

	private volatile string _phase = "Idle";

	private string _phaseBeforePause = "";

	private long _lastTicks;

	private long _lastBytes;

	private double _speed;

	private long _nDelta;

	private long _deltaWritten;

	private long _deltaTotal;

	private const long DirectMin = 67108864L;

	private const long ChunkMin = 536870912L;

	private const long SmallMax = 1048576L;

	private volatile TaskCompletionSource? _pauseTcs;

	private readonly ConcurrentBag<AlignedBuffer> _pool = new ConcurrentBag<AlignedBuffer>();

	private readonly ConcurrentQueue<AlignedBuffer> _allBuffers = new ConcurrentQueue<AlignedBuffer>();

	private int _directFallbackLogged;

	private PlanPreview? _preview;

	private long _nBadMedia;

	private ShadowSet? _vss;

	private readonly object _vssGate = new object();

	private const int PreviewListMax = 5000;

	private int _tuneStart;

	private int _tuneEnd;

	private int _tuneLo = int.MaxValue;

	private int _tuneHi;

	private int _tuneSamples;

	private long Fat32Max => _o.Fat32MaxOverride.GetValueOrDefault(4294967295L);

	private bool IsFat32
	{
		get
		{
			if (!_destFs.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
			{
				return _destFs.Equals("FAT", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	private bool _zoneCapable
	{
		get
		{
			if (Native.IsWindows)
			{
				if (!_destFs.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
				{
					return _destFs.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
				}
				return true;
			}
			return false;
		}
	}

	private bool IsFatFamily
	{
		get
		{
			if (!_destFs.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
			{
				return _destFs.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	public bool IsPaused => _pauseTcs != null;

	private long PartSize => Math.Max(1L, Fat32Max / _buf) * _buf;

	public event Action<string>? Message;

	private void EnsureDir(string dir)
	{
		if (_dirsMade.TryAdd(dir, 0))
		{
			Directory.CreateDirectory(dir);
		}
	}

	private static bool IsLink(FileSystemInfo e)
	{
		if ((e.Attributes & FileAttributes.ReparsePoint) == 0)
		{
			return false;
		}
		try
		{
			return e.LinkTarget != null;
		}
		catch
		{
			return true;
		}
	}

	private bool TimeSame(DateTime a, DateTime b)
	{
		double num = Math.Abs((a - b).TotalSeconds);
		if (!(num <= 2.0))
		{
			if (IsFatFamily)
			{
				return Math.Abs(num - 3600.0) <= 2.0;
			}
			return false;
		}
		return true;
	}

	public CopyJob(JobOptions options)
	{
		_o = options;
		_o.Threads = Math.Clamp(_o.Threads, 0, 64);
		_o.BufferSizeMB = Math.Clamp(_o.BufferSizeMB, 1, 64);
		_o.ChunkWorkers = Math.Clamp(_o.ChunkWorkers, 0, 8);
		_o.DeltaMinMB = Math.Clamp(_o.DeltaMinMB, 1, 100000);
		_o.Retries = Math.Clamp(_o.Retries, 0, 10);
		_o.RetryWaitSeconds = Math.Clamp(_o.RetryWaitSeconds, 0, 600);
		_o.ReconnectWaitSeconds = Math.Clamp(_o.ReconnectWaitSeconds, 0, 3600);
		_buf = _o.BufferSizeMB * 1024 * 1024;
		_filter = new PathFilter(_o);
	}

	public void Pause()
	{
		if (_pauseTcs == null && _sw.IsRunning)
		{
			_pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_sw.Stop();
			_phaseBeforePause = _phase;
			_phase = "Paused";
		}
	}

	public void Resume()
	{
		TaskCompletionSource taskCompletionSource = Interlocked.Exchange(ref _pauseTcs, null);
		if (taskCompletionSource != null)
		{
			_sw.Start();
			_lastTicks = 0L;
			_phase = _phaseBeforePause;
			taskCompletionSource.TrySetResult();
		}
	}

	public async Task<JobResult> RunAsync(CancellationToken ct)
	{
		Validate();
		_sw.Start();
		bool cancelled = false;
		_jobCt = ct;
		if (_o.LimitMBps > 0)
		{
			_throttle = new Throttle((long)_o.LimitMBps * 1048576L);
			Info($"Speed limit: {_o.LimitMBps} MB/s (CopyForge pipeline is used so the limit applies).");
		}
		KeepAwake? awake = (_o.KeepAwake && !_o.DryRun) ? new KeepAwake() : null;
		if (!string.IsNullOrWhiteSpace(_o.LogPath) && !_o.DryRun)
		{
			try
			{
				_log = new JobLog(_o.LogPath);
			}
			catch (Exception logEx)
			{
				Info("Could not open the log file: " + logEx.Message);
			}
		}
		try
		{
			if (_o.Mode != JobMode.Delete)
			{
				await RunCopyAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await RunDeleteAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			_phase = "Done";
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
			_phase = "Cancelled";
		}
		finally
		{
			Resume();
			awake?.Dispose();
			FreeBuffers();
			try
			{
				_vss?.Dispose();
			}
			catch
			{
			}
		}
		_sw.Stop();
		if (_skipVerified + _skipMismatch > 0)
		{
			Info($"Skipped-file check: {_skipVerified:N0} identical files confirmed byte for byte, {_skipMismatch:N0} differed and were copied again.");
		}
		_log?.Dispose();
		_log = null;
		if (_o.EjectAfter && !cancelled && !_o.DryRun && _preview == null && _o.Mode != JobMode.Delete && Interlocked.Read(ref _failed) == 0 && _dstVol.Removable)
		{
			Info("Ejecting " + (Path.GetPathRoot(_dest) ?? _dest) + " - waiting up to 60 s for Windows, antivirus and indexing to let go of the new files...");
			if (!Native.EjectVolume(_dest, out var ejectMsg, 60, ct))
			{
				EjectPendingPath = _dest;
			}
			Info(ejectMsg);
		}
		return new JobResult(Interlocked.Read(ref _filesDone), Interlocked.Read(ref _skipped), Interlocked.Read(ref _deleted), Interlocked.Read(ref _failed), _o.Verify ? (Interlocked.Read(ref _bytesDone) / 2) : Interlocked.Read(ref _bytesDone), _sw.Elapsed, cancelled, _errors.ToArray(), _failedPairs.ToArray(), _preview);
	}

	/// <summary>Set when the job asked for an eject but Windows would not release the drive; the GUI offers a retry.</summary>
	public string? EjectPendingPath { get; private set; }

	public ProgressSnapshot GetSnapshot()
	{
		long num = Interlocked.Read(ref _bytesDone);
		long num2 = Interlocked.Read(ref _bytesTotal);
		long num3 = Interlocked.Read(ref _filesDone);
		long num4 = Interlocked.Read(ref _filesTotal);
		long timestamp = Stopwatch.GetTimestamp();
		if (_lastTicks == 0L)
		{
			_lastTicks = timestamp;
			_lastBytes = num;
		}
		else
		{
			double num5 = (double)(timestamp - _lastTicks) / (double)Stopwatch.Frequency;
			if (num5 >= 0.5)
			{
				double num6 = (double)(num - _lastBytes) / num5;
				_speed = ((_speed == 0.0) ? num6 : (_speed * 0.6 + num6 * 0.4));
				_lastBytes = num;
				_lastTicks = timestamp;
			}
		}
		if (IsPaused)
		{
			_speed = 0.0;
		}
		bool flag = _o.Mode == JobMode.Delete;
		double value = ((!flag) ? ((num2 == 0L) ? 0.0 : ((double)num / (double)num2)) : ((num4 == 0L) ? 0.0 : ((double)num3 / (double)num4)));
		value = Math.Clamp(value, 0.0, 1.0);
		TimeSpan? eta = null;
		double totalSeconds = _sw.Elapsed.TotalSeconds;
		if (!flag && _speed > 1.0 && num2 > num)
		{
			eta = TimeSpan.FromSeconds((double)(num2 - num) / _speed);
		}
		else if (flag && totalSeconds > 1.0 && num3 > 0 && num4 > num3)
		{
			eta = TimeSpan.FromSeconds(totalSeconds * (double)(num4 - num3) / (double)num3);
		}
		return new ProgressSnapshot(_phase, num4, num3, num2, num, Interlocked.Read(ref _skipped), Interlocked.Read(ref _failed), Interlocked.Read(ref _deleted), _current, _sw.Elapsed, _speed, value, eta);
	}

	private static string Norm(string p)
	{
		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Trim().Trim('"')));
	}

	private static bool IsUnder(string child, string parent)
	{
		if (!child.Equals(parent, StringComparison.OrdinalIgnoreCase))
		{
			ReadOnlySpan<char> readOnlySpan = parent;
			char reference = Path.DirectorySeparatorChar;
			return child.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	internal static string SafeName(DirectoryInfo di)
	{
		if (di.Parent != null)
		{
			return di.Name;
		}
		string text = di.FullName.Replace(":", "").Replace("\\", "").Replace("/", "");
		if (text.Length != 0)
		{
			return text;
		}
		return "root";
	}

	private void ProbeDestination(string firstSource)
	{
		try
		{
			string pathRoot = Path.GetPathRoot(_dest);
			if (!string.IsNullOrEmpty(pathRoot))
			{
				DriveInfo driveInfo = new DriveInfo(pathRoot);
				if (driveInfo.IsReady)
				{
					_destFs = driveInfo.DriveFormat;
					_destFree = driveInfo.AvailableFreeSpace;
				}
			}
		}
		catch
		{
		}
		if (_o.FileSystemOverride != null)
		{
			_destFs = _o.FileSystemOverride;
		}
		_srcVol = (string.IsNullOrEmpty(firstSource) ? default(VolInfo) : Native.Probe(firstSource));
		_dstVol = Native.Probe(_dest);
		_threads = ((_o.Threads > 0) ? _o.Threads : AutoThreads());
		_extras = _o.CopyNtfsExtras;
		if (_extras && (!Native.IsWindows || (!_destFs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !_destFs.Equals("ReFS", StringComparison.OrdinalIgnoreCase))))
		{
			_extras = false;
			Info("NTFS permissions/streams skipped: the destination is " + (string.IsNullOrEmpty(_destFs) ? "not NTFS/ReFS" : _destFs) + " and cannot store them.");
		}
	}

	private int AutoThreads()
	{
		int processorCount = Environment.ProcessorCount;
		if (_srcVol.Network || _dstVol.Network)
		{
			return Math.Clamp(processorCount, 2, 8);
		}
		if (_dstVol.Removable || _srcVol.Removable)
		{
			return 2;
		}
		if ((_srcVol.SolidKnown && !_srcVol.Solid) || (_dstVol.SolidKnown && !_dstVol.Solid))
		{
			return 2;
		}
		return Math.Clamp(processorCount / 2, 4, 8);
	}

	private void Validate()
	{
		if (_o.RecycleBin && !Native.IsWindows)
		{
			throw new InvalidOperationException("The Recycle Bin option is only available on Windows.");
		}
		if (_o.ExplicitPairs != null)
		{
			JobMode mode = _o.Mode;
			if ((uint)mode > 1u)
			{
				throw new InvalidOperationException("Retrying specific files only works for Copy and Move.");
			}
			if (string.IsNullOrWhiteSpace(_o.Destination))
			{
				throw new InvalidOperationException("No destination selected.");
			}
			_dest = Norm(_o.Destination);
			ProbeDestination((_o.ExplicitPairs.Count > 0) ? _o.ExplicitPairs[0].Src : "");
			return;
		}
		List<string> list = _o.Sources.Where((string s) => !string.IsNullOrWhiteSpace(s)).Select(Norm).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException("No source selected.");
		}
		foreach (string item in list)
		{
			if (!File.Exists(item) && !Directory.Exists(item))
			{
				throw new InvalidOperationException("Source not found: " + item);
			}
		}
		_o.Sources = list;
		if (_o.Mode == JobMode.Delete)
		{
			_threads = ((_o.Threads > 0) ? _o.Threads : 8);
			List<string> list2 = new string[6]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.Windows),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				Environment.GetFolderPath(Environment.SpecialFolder.System)
			}.Where((string p) => !string.IsNullOrEmpty(p)).Select(Norm).ToList();
			{
				foreach (string item2 in list)
				{
					if (string.Equals(Path.GetPathRoot(item2), item2, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("Refusing to delete a drive root: " + item2);
					}
					foreach (string item3 in list2)
					{
						if (item2.Equals(item3, StringComparison.OrdinalIgnoreCase) || IsUnder(item3, item2))
						{
							throw new InvalidOperationException("Refusing to delete protected folder: " + item2);
						}
					}
				}
				return;
			}
		}
		if (string.IsNullOrWhiteSpace(_o.Destination))
		{
			throw new InvalidOperationException("No destination selected.");
		}
		_dest = Norm(_o.Destination);
		ProbeDestination(list[0]);
		if (_o.Mode == JobMode.Sync)
		{
			if (list.Count != 1 || !Directory.Exists(list[0]))
			{
				throw new InvalidOperationException("Sync needs exactly one source folder.");
			}
			if (string.Equals(Path.GetPathRoot(_dest), _dest, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Sync into a drive root is not allowed (it deletes extra files). Pick a folder.");
			}
			if (IsUnder(list[0], _dest))
			{
				throw new InvalidOperationException("The source is inside the sync destination; syncing would delete it.");
			}
		}
		foreach (string item4 in list)
		{
			bool num = Directory.Exists(item4);
			string path = (num ? SafeName(new DirectoryInfo(item4)) : Path.GetFileName(item4));
			if (((_o.Mode == JobMode.Sync) ? _dest : Path.Combine(_dest, path)).Equals(item4, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Source and destination are the same: " + item4);
			}
			if (num && IsUnder(_dest, item4))
			{
				throw new InvalidOperationException("The destination is inside the source folder: " + item4);
			}
		}
	}

	private Plan Scan(bool forDelete, CancellationToken ct)
	{
		Plan plan = new Plan();
		if (_o.ExplicitPairs != null)
		{
			foreach (FilePair explicitPair in _o.ExplicitPairs)
			{
				ct.ThrowIfCancellationRequested();
				FileInfo fileInfo = new FileInfo(explicitPair.Src);
				if (!fileInfo.Exists)
				{
					Report(explicitPair.Src + ": source file no longer exists");
					_failed++;
				}
				else
				{
					AddFile(plan, fileInfo, explicitPair.Dst, forDelete: false);
				}
			}
			return plan;
		}
		foreach (string source in _o.Sources)
		{
			ct.ThrowIfCancellationRequested();
			if (File.Exists(source))
			{
				FileInfo fileInfo2 = new FileInfo(source);
				if (_filter.FileAccepted(fileInfo2.Name, fileInfo2.Name, fileInfo2.Length, fileInfo2.LastWriteTimeUtc))
				{
					AddFile(plan, fileInfo2, forDelete ? "" : Path.Combine(_dest, fileInfo2.Name), forDelete);
				}
			}
			else
			{
				if (!Directory.Exists(source))
				{
					continue;
				}
				DirectoryInfo directoryInfo = new DirectoryInfo(source);
				string text = (forDelete ? "" : ((_o.Mode == JobMode.Sync) ? _dest : Path.Combine(_dest, SafeName(directoryInfo))));
				plan.SourceDirs.Add(directoryInfo.FullName);
				if (!forDelete)
				{
					plan.DirsToCreate.Add(text);
					plan.Expected.Add(text);
					plan.DirPairs.Add(new DirPair(directoryInfo.FullName, text, directoryInfo.CreationTimeUtc, directoryInfo.LastWriteTimeUtc));
					if (_o.Mode == JobMode.Sync)
					{
						plan.SyncRoots.Add(text);
					}
				}
				Walk(directoryInfo, "", text, plan, forDelete, ct);
			}
		}
		return plan;
	}

	private void Walk(DirectoryInfo dir, string rel, string dstDir, Plan plan, bool forDelete, CancellationToken ct)
	{
		foreach (FileSystemInfo item in dir.EnumerateFileSystemInfos("*", EnumOpts))
		{
			ct.ThrowIfCancellationRequested();
			if (IsLink(item))
			{
				Info("Skipped link/junction: " + item.FullName);
				continue;
			}
			string text;
			if (rel.Length != 0)
			{
				ReadOnlySpan<char> readOnlySpan = rel;
				char reference = Path.DirectorySeparatorChar;
				text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference), item.Name);
			}
			else
			{
				text = item.Name;
			}
			string rel2 = text;
			if (item is DirectoryInfo directoryInfo)
			{
				if (!_filter.DirExcluded(rel2, directoryInfo.Name))
				{
					string text2 = (forDelete ? "" : Path.Combine(dstDir, directoryInfo.Name));
					plan.SourceDirs.Add(directoryInfo.FullName);
					if (!forDelete)
					{
						plan.DirsToCreate.Add(text2);
						plan.Expected.Add(text2);
						plan.DirPairs.Add(new DirPair(directoryInfo.FullName, text2, directoryInfo.CreationTimeUtc, directoryInfo.LastWriteTimeUtc));
					}
					Walk(directoryInfo, rel2, text2, plan, forDelete, ct);
				}
			}
			else if (item is FileInfo fileInfo && _filter.FileAccepted(rel2, fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTimeUtc))
			{
				AddFile(plan, fileInfo, forDelete ? "" : Path.Combine(dstDir, fileInfo.Name), forDelete);
			}
		}
	}

	private bool NeedsSplit(long size)
	{
		if (_o.SplitOversize && IsFat32)
		{
			return size > Fat32Max;
		}
		return false;
	}

	private int PartCount(long size)
	{
		return (int)((size + PartSize - 1) / PartSize);
	}

	private static string PartName(string dst, int i)
	{
		return $"{dst}.{i + 1:000}";
	}

	private void AddFile(Plan plan, FileInfo f, string dst, bool forDelete)
	{
		if (forDelete)
		{
			plan.Files.Add(new FileItem(f.FullName, "", f.Length, f.LastWriteTimeUtc, SkipCopy: false));
			return;
		}
		bool flag = NeedsSplit(f.Length);
		if (flag)
		{
			int num = PartCount(f.Length);
			for (int i = 0; i < num; i++)
			{
				plan.Expected.Add(PartName(dst, i));
			}
		}
		else
		{
			plan.Expected.Add(dst);
		}
		Decision decision = flag ? DecideSplit(f, dst) : Decide(f, dst);
		if (decision == Decision.SkipIdentical && _o.VerifySkipped && _o.Verify && !flag && !_o.DryRun)
		{
			if (!SameBytes(f.FullName, dst, _jobCt))
			{
				decision = Decision.Copy;
				Interlocked.Increment(ref _skipMismatch);
				Info("Skipped-file check: " + f.Name + " looks identical but its contents differ - it will be copied again.");
			}
			else
			{
				Interlocked.Increment(ref _skipVerified);
			}
		}
		switch (decision)
		{
		case Decision.Copy:
			if (IsFat32 && f.Length > Fat32Max && !flag)
			{
				Interlocked.Increment(ref _failed);
				Report(f.FullName + ": file is " + FormatBytes(f.Length) + ", over the 4 GB per-file limit of the FAT32 destination - skipped. Tick 'Split files over 4 GB', or reformat the destination as exFAT or NTFS.");
				break;
			}
			if (flag)
			{
				Info($"{f.Name} ({FormatBytes(f.Length)}) is over the FAT32 limit: it will be written as {PartCount(f.Length)} parts ({f.Name}.001, .002 ...). Join them later with:  copy /b \"{f.Name}.001\"+\"{f.Name}.002\"... \"{f.Name}\"");
			}
			plan.Files.Add(new FileItem(f.FullName, dst, f.Length, f.LastWriteTimeUtc, SkipCopy: false, f.CreationTimeUtc, f.Attributes));
			_filesTotal++;
			_bytesTotal += f.Length * ((!_o.Verify) ? 1 : 2);
			break;
		case Decision.SkipIdentical:
			_skipped++;
			_log?.Row("skipped-identical", f.FullName, dst, f.Length);
			if (_o.WriteManifest)
			{
				_identical.Enqueue(dst);
			}
			if (_o.Mode == JobMode.Move)
			{
				plan.Files.Add(new FileItem(f.FullName, dst, f.Length, f.LastWriteTimeUtc, SkipCopy: true, f.CreationTimeUtc, f.Attributes));
			}
			break;
		default:
			_skipped++;
			_log?.Row("skipped", f.FullName, dst, f.Length);
			break;
		}
	}

	private long _skipVerified;

	private long _skipMismatch;

	private static bool SameBytes(string a, string b, CancellationToken ct)
	{
		try
		{
			using FileStream fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
			using FileStream fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
			if (fa.Length != fb.Length)
			{
				return false;
			}
			byte[] ba = new byte[1048576];
			byte[] bb = new byte[1048576];
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				int n = fa.Read(ba, 0, ba.Length);
				if (n == 0)
				{
					return true;
				}
				int got = 0;
				while (got < n)
				{
					int r = fb.Read(bb, got, n - got);
					if (r == 0)
					{
						return false;
					}
					got += r;
				}
				if (!ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n)))
				{
					return false;
				}
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}

	private Decision Decide(FileInfo s, string dst)
	{
		FileInfo fileInfo = new FileInfo(dst);
		if (!fileInfo.Exists)
		{
			return Decision.Copy;
		}
		bool flag = fileInfo.Length == s.Length && TimeSame(fileInfo.LastWriteTimeUtc, s.LastWriteTimeUtc);
		if (!flag && Interlocked.Increment(ref _whyLogged) <= 3)
		{
			Info($"Why re-copy: {s.Name}  size src {s.Length} / dst {fileInfo.Length}  mtime src {s.LastWriteTimeUtc:u} / dst {fileInfo.LastWriteTimeUtc:u}  (delta {(fileInfo.LastWriteTimeUtc - s.LastWriteTimeUtc).TotalSeconds:0.#} s)");
		}
		return _o.Overwrite switch
		{
			OverwriteMode.Always => Decision.Copy, 
			OverwriteMode.Skip => flag ? Decision.SkipIdentical : Decision.SkipPolicy, 
			OverwriteMode.IfNewer => (!(s.LastWriteTimeUtc > fileInfo.LastWriteTimeUtc.AddSeconds(2.0)) || TimeSame(s.LastWriteTimeUtc, fileInfo.LastWriteTimeUtc)) ? (flag ? Decision.SkipIdentical : Decision.SkipPolicy) : Decision.Copy, 
			_ => flag ? Decision.SkipIdentical : Decision.Copy, 
		};
	}

	private Decision DecideSplit(FileInfo s, string dst)
	{
		int num = PartCount(s.Length);
		bool flag = true;
		for (int i = 0; i < num && flag; i++)
		{
			FileInfo fileInfo = new FileInfo(PartName(dst, i));
			long num2 = ((i < num - 1) ? PartSize : (s.Length - PartSize * (num - 1)));
			flag = fileInfo.Exists && fileInfo.Length == num2 && (i > 0 || TimeSame(fileInfo.LastWriteTimeUtc, s.LastWriteTimeUtc));
		}
		if (_o.Overwrite == OverwriteMode.Always)
		{
			return Decision.Copy;
		}
		if (flag)
		{
			return Decision.SkipIdentical;
		}
		if (_o.Overwrite != OverwriteMode.Skip || !new FileInfo(PartName(dst, 0)).Exists)
		{
			return Decision.Copy;
		}
		return Decision.SkipPolicy;
	}

	private async Task RunCopyAsync(CancellationToken ct)
	{
		_phase = "Scanning";
		long scan0 = Stopwatch.GetTimestamp();
		Plan plan = await Task.Run(() => Scan(forDelete: false, ct), ct).ConfigureAwait(continueOnCapturedContext: false);
		_ticksScan = Stopwatch.GetTimestamp() - scan0;
		if (_o.DryRun)
		{
			_phase = "Preview";
			_preview = MakeCopyPreview(plan);
			Info($"PREVIEW (nothing was changed): {_preview.CopyCount:N0} to copy ({FormatBytes(_preview.CopyBytes)}), {_preview.SkipCount:N0} unchanged" + ((_o.Mode == JobMode.Sync) ? $", {_preview.DeleteCount:N0} to delete from the destination." : "."));
			return;
		}
		long num = plan.Files.Where((FileItem x) => !x.SkipCopy).Sum((FileItem x) => x.Size);
		if (_destFree >= 0 && num > _destFree)
		{
			Info($"WARNING: {FormatBytes(num)} to copy but only {FormatBytes(_destFree)} free on the destination " + "(files that overwrite existing ones free some space). The copy may run out of space.");
		}
		Info($"Plan: {_threads} file thread(s){((_o.Threads == 0) ? " (auto)" : "")}, {_o.BufferSizeMB} MB blocks, I/O {_o.Io}, method {_o.Method}, source {_srcVol.Describe()} -> destination {_dstVol.Describe()}" + (string.IsNullOrEmpty(_destFs) ? "" : (" (" + _destFs + ")")) + ".");
		UsbNote(_dest, "destination");
		UsbNote(_o.Sources.Count > 0 ? _o.Sources[0] : "", "source");
		_phase = ((_o.Mode == JobMode.Move) ? "Moving" : "Copying");
		foreach (string item in plan.DirsToCreate)
		{
			try
			{
				Directory.CreateDirectory(item);
			}
			catch (Exception ex)
			{
				Report(item + ": " + ex.Message);
			}
		}
		if (_o.AutoTune && _o.Threads == 0 && plan.Files.Count > 8)
		{
			await RunTunedAsync(plan.Files, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			ParallelOptions parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = _threads,
				CancellationToken = ct
			};
			await Parallel.ForEachAsync(plan.Files, parallelOptions, ProcessFileAsync).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (_o.Mode == JobMode.Sync)
		{
			_phase = "Removing extra files";
			foreach (string syncRoot in plan.SyncRoots)
			{
				Prune(syncRoot, "", plan, ct);
			}
		}
		if (_o.PreserveDirTimes || _extras)
		{
			ApplyDirMeta(plan);
		}
		if (_o.WriteManifest && _o.Mode != JobMode.Delete && !string.IsNullOrEmpty(_o.Destination) && Directory.Exists(_o.Destination))
		{
			_phase = "Writing manifest";
			HashSet<string> failedDst = new HashSet<string>(_failedPairs.Select((FilePair x) => x.Dst), StringComparer.OrdinalIgnoreCase);
			try
			{
				await Manifest.UpdateAsync(_o.Destination, (from x in plan.Files
					where !string.IsNullOrEmpty(x.Dst) && !x.SkipCopy && !failedDst.Contains(x.Dst)
					select (x.Src, Dst: x.Dst)).Concat<(string, string)>(_identical.Select((string d) => ((string, string d))(null, d: d))), _o.ManifestSignKey, _threads, delegate(string m)
				{
					Info(m);
				}, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex3)
			{
				Report("Manifest: " + ex3.Message);
			}
		}
		if (_o.Mode == JobMode.Move)
		{
			_phase = "Cleaning up source folders";
			foreach (string item2 in plan.SourceDirs.OrderByDescending((string x) => x.Length))
			{
				try
				{
					Directory.Delete(item2, recursive: false);
				}
				catch
				{
				}
			}
		}
		Info($"Methods used: block-clone {_nClone}, windows-native {_nNative}, direct {_nDirect}, buffered {_nBuffered}, parallel-chunk {_nChunk}, split {_nSplit}, in-place {_nInPlace}; retries {_nRetries}.");
		if (_nBadMedia > 0)
		{
			Info($"{_nBadMedia:N0} file(s) hit a disk data error and were skipped (see the errors above). The drive may be failing - copy what is readable first.");
		}
		if (_tuneSamples > 0)
		{
			Info($"Auto-tune: started at {_tuneStart}, finished at {_tuneEnd} thread(s) (range {_tuneLo}-{_tuneHi}) over {_tuneSamples} samples.");
		}
		if (_nDelta > 0)
		{
			Info($"Delta update: {_nDelta:N0} file(s) compared, rewrote {FormatBytes(_deltaWritten)} of {FormatBytes(_deltaTotal)} ({((_deltaTotal == 0L) ? 0.0 : (100.0 * (double)_deltaWritten / (double)_deltaTotal)):0.#}% changed).");
		}
		double num2 = Stopwatch.Frequency;
		Info($"Time split: scan {(double)_ticksScan / num2:0.0} s; per-file work summed over threads: copy {(double)_ticksCopy / num2:0.0} s, finish (rename/times) {(double)_ticksFinish / num2:0.0} s.");
	}

	private void ApplyDirMeta(Plan plan)
	{
		foreach (DirPair dirPair in plan.DirPairs)
		{
			try
			{
				if (!Directory.Exists(dirPair.Dst))
				{
					continue;
				}
				if (_o.PreserveDirTimes)
				{
					try
					{
						Directory.SetCreationTimeUtc(dirPair.Dst, dirPair.CreatedUtc);
					}
					catch
					{
					}
					try
					{
						Directory.SetLastWriteTimeUtc(dirPair.Dst, dirPair.WrittenUtc);
					}
					catch
					{
					}
				}
				if (_extras && Directory.Exists(dirPair.Src))
				{
					try
					{
						Native.CopySecurity(dirPair.Src, dirPair.Dst);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}

	private async ValueTask ProcessFileAsync(FileItem it, CancellationToken ct)
	{
		_current = it.Src;
		try
		{
			await WaitIfPaused(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (it.SkipCopy)
			{
				DeleteFile(it.Src, recycle: false);
				return;
			}
			if (_o.Mode == JobMode.Move && !_o.Verify && !NeedsSplit(it.Size) && SameVolume(it.Src, it.Dst))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(it.Dst));
				if (File.Exists(it.Dst))
				{
					File.SetAttributes(it.Dst, FileAttributes.Normal);
				}
				File.Move(it.Src, it.Dst, overwrite: true);
				Interlocked.Add(ref _bytesDone, it.Size);
				Interlocked.Increment(ref _filesDone);
				_log?.Row("moved", it.Src, it.Dst, it.Size);
				return;
			}
			FileItem cur = it;
			bool usedShadow = false;
			int attempt = 0;
			while (true)
			{
				try
				{
					await CopyFileAsync(cur, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex2)
				{
					if (_o.UseVss && !usedShadow && IsLockedFile(ex2))
					{
						string text = ShadowPathFor(it.Src);
						if (text != null)
						{
							usedShadow = true;
							cur = it with
							{
								Src = text
							};
							Info(Path.GetFileName(it.Src) + " is in use by another program - reading it from a shadow copy.");
							attempt--;
							goto IL_046b;
						}
					}
					if (attempt >= _o.Retries || !IsRetryable(ex2, it))
					{
						throw;
					}
					Interlocked.Increment(ref _nRetries);
					Info($"Retry {attempt + 1}/{_o.Retries}: {Path.GetFileName(it.Src)} - {Explain(ex2, it)}");
					if (!(await WaitBeforeRetryAsync(it, attempt, ct).ConfigureAwait(continueOnCapturedContext: false)))
					{
						throw;
					}
					goto IL_046b;
				}
				break;
				IL_046b:
				attempt++;
			}
			Interlocked.Increment(ref _filesDone);
			_log?.Row("copied", it.Src, it.Dst, it.Size);
			if (_o.Mode == JobMode.Move)
			{
				DeleteFile(it.Src, recycle: false);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex4)
		{
			Interlocked.Increment(ref _failed);
			_failedPairs.Enqueue(new FilePair(it.Src, it.Dst));
			_log?.Row("FAILED", it.Src, it.Dst, it.Size, Explain(ex4, it));
			Report(it.Src + ": " + Explain(ex4, it));
		}
	}

	private bool IsRetryable(Exception ex, FileItem it)
	{
		if (!File.Exists(it.Src))
		{
			return false;
		}
		if (IsBadMedia(ex))
		{
			Interlocked.Increment(ref _nBadMedia);
			return false;
		}
		if (!DestinationAvailable())
		{
			return true;
		}
		if ((ex is FileNotFoundException || ex is PathTooLongException || ex is NotSupportedException || ex is ArgumentException) ? true : false)
		{
			return false;
		}
		if (IsDiskFull(ex))
		{
			return false;
		}
		if (ex is IOException || ex is UnauthorizedAccessException)
		{
			return true;
		}
		return false;
	}

	private static bool IsDiskFull(Exception ex)
	{
		bool flag = ex.Message.Contains("disk was full", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);
		if (!flag)
		{
			bool flag2 = ex is IOException;
			if (flag2)
			{
				int num = ex.HResult & 0xFFFF;
				bool flag3 = ((num == 39 || num == 112) ? true : false);
				flag2 = flag3;
			}
			flag = flag2;
		}
		return flag;
	}

	private bool DestinationAvailable()
	{
		try
		{
			string pathRoot = Path.GetPathRoot(_dest);
			return !string.IsNullOrEmpty(pathRoot) && Directory.Exists(pathRoot);
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> WaitBeforeRetryAsync(FileItem it, int attempt, CancellationToken ct)
	{
		_dirsMade.Clear();
		if (!DestinationAvailable())
		{
			Info($"The destination ({Path.GetPathRoot(_dest)}) is not available - waiting up to {_o.ReconnectWaitSeconds} s for it to come back...");
			for (int s = 0; s < _o.ReconnectWaitSeconds; s++)
			{
				await Task.Delay(1000, ct).ConfigureAwait(continueOnCapturedContext: false);
				await WaitIfPaused(ct).ConfigureAwait(continueOnCapturedContext: false);
				if (DestinationAvailable())
				{
					Info("The destination is back - continuing.");
					return true;
				}
			}
			return false;
		}
		await Task.Delay(TimeSpan.FromSeconds((double)(_o.RetryWaitSeconds * (attempt + 1))), ct).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	private string Explain(Exception ex, FileItem it)
	{
		if (IsBadMedia(ex))
		{
			return "the drive reports a data error (bad sector or failing disk) - not retried. " + ex.Message;
		}
		if (!IsDiskFull(ex))
		{
			return ex.Message;
		}
		try
		{
			string pathRoot = Path.GetPathRoot(_dest);
			if (!string.IsNullOrEmpty(pathRoot))
			{
				long availableFreeSpace = new DriveInfo(pathRoot).AvailableFreeSpace;
				if (availableFreeSpace < it.Size)
				{
					return $"not enough free space on the destination (needs {FormatBytes(it.Size)}, only {FormatBytes(availableFreeSpace)} free).";
				}
				return "the destination reported 'disk full' although " + FormatBytes(availableFreeSpace) + " is free. Likely a file-system limit (FAT32 allows 4 GB per file) or a quota. " + ex.Message;
			}
		}
		catch
		{
		}
		return ex.Message;
	}

	internal static string FormatBytes(long b)
	{
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		double num = Math.Max(b, 0L);
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{num:0} {array[num2]}";
		}
		return $"{num:0.##} {array[num2]}";
	}

	private static bool SameVolume(string a, string b)
	{
		return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
	}

	private void Prune(string dir, string rel, Plan plan, CancellationToken ct)
	{
		foreach (FileSystemInfo item in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", EnumOpts).ToList())
		{
			ct.ThrowIfCancellationRequested();
			if (IsLink(item))
			{
				continue;
			}
			string text;
			if (rel.Length != 0)
			{
				ReadOnlySpan<char> readOnlySpan = rel;
				char reference = Path.DirectorySeparatorChar;
				text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference), item.Name);
			}
			else
			{
				text = item.Name;
			}
			string rel2 = text;
			try
			{
				if (item is DirectoryInfo directoryInfo)
				{
					if (!_filter.DirExcluded(rel2, directoryInfo.Name))
					{
						Prune(directoryInfo.FullName, rel2, plan, ct);
						if (!plan.Expected.Contains(directoryInfo.FullName) && !directoryInfo.EnumerateFileSystemInfos().Any())
						{
							directoryInfo.Delete();
							Interlocked.Increment(ref _deleted);
						}
					}
				}
				else if (item is FileInfo fileInfo && !plan.Expected.Contains(fileInfo.FullName) && (fileInfo.Name.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase) || _filter.FileAccepted(rel2, fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTimeUtc)))
				{
					_current = fileInfo.FullName;
					fileInfo.Attributes = FileAttributes.Normal;
					fileInfo.Delete();
					Interlocked.Increment(ref _deleted);
				}
			}
			catch (Exception ex)
			{
				Report(item.FullName + ": " + ex.Message);
			}
		}
	}

	private async Task RunDeleteAsync(CancellationToken ct)
	{
		_phase = "Scanning";
		Plan plan = await Task.Run(() => Scan(forDelete: true, ct), ct).ConfigureAwait(continueOnCapturedContext: false);
		Interlocked.Exchange(ref _filesTotal, plan.Files.Count);
		if (_o.DryRun)
		{
			_phase = "Preview";
			_preview = MakeDeletePreview(plan);
			Info($"PREVIEW (nothing was changed): {_preview.DeleteCount:N0} file(s) would be deleted.");
			return;
		}
		_phase = (_o.RecycleBin ? "Recycling" : "Deleting");
		int maxDegreeOfParallelism = (_o.RecycleBin ? 1 : Math.Max(_threads, 8));
		ParallelOptions parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = maxDegreeOfParallelism,
			CancellationToken = ct
		};
		await Parallel.ForEachAsync(plan.Files, parallelOptions, async delegate(FileItem it, CancellationToken t)
		{
			await WaitIfPaused(t).ConfigureAwait(continueOnCapturedContext: false);
			_current = it.Src;
			try
			{
				DeleteFile(it.Src, _o.RecycleBin);
				Interlocked.Increment(ref _filesDone);
				Interlocked.Increment(ref _deleted);
			}
			catch (Exception ex)
			{
				Interlocked.Increment(ref _failed);
				Report(it.Src + ": " + ex.Message);
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		_phase = "Removing folders";
		foreach (string item in plan.SourceDirs.OrderByDescending((string x) => x.Length))
		{
			try
			{
				Directory.Delete(item, recursive: false);
			}
			catch
			{
			}
		}
	}

	private static void DeleteFile(string path, bool recycle)
	{
		if (recycle && Native.IsWindows)
		{
			Native.Recycle(path);
			return;
		}
		try
		{
			File.Delete(path);
		}
		catch (UnauthorizedAccessException)
		{
			File.SetAttributes(path, FileAttributes.Normal);
			File.Delete(path);
		}
	}

	private void Info(string msg)
	{
		this.Message?.Invoke(msg);
	}

	private void Report(string msg)
	{
		_errors.Enqueue(msg);
		this.Message?.Invoke("ERROR: " + msg);
	}

	private async Task<bool> TryDeltaAsync(FileItem it, CancellationToken ct)
	{
		if (!_o.DeltaUpdate || it.Size < (long)_o.DeltaMinMB * 1024L * 1024 || NeedsSplit(it.Size))
		{
			return false;
		}
		FileInfo fileInfo;
		try
		{
			fileInfo = new FileInfo(it.Dst);
		}
		catch
		{
			return false;
		}
		if (!fileInfo.Exists || fileInfo.Length == 0L || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			return false;
		}
		try
		{
			if ((fileInfo.Attributes & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(it.Dst, FileAttributes.Normal);
			}
		}
		catch
		{
			return false;
		}
		long written = 0L;
		File.SetLastWriteTimeUtc(it.Dst, new DateTime(1980, 1, 2, 0, 0, 0, DateTimeKind.Utc));
		using (SafeFileHandle src = File.OpenHandle(it.Src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan | FileOptions.Asynchronous, 0L))
		{
			using SafeFileHandle dst = File.OpenHandle(it.Dst, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous, 0L);
			AlignedBuffer bs = Rent();
			AlignedBuffer bd = Rent();
			try
			{
				int ns;
				for (long off = 0L; off < it.Size; off += ns, AddBytes(ns))
				{
					await WaitIfPaused(ct).ConfigureAwait(continueOnCapturedContext: false);
					int want = (int)Math.Min(_buf, it.Size - off);
					Task<int> rs = ReadFullAsync(src, bs.Memory.Slice(0, want), off, ct);
					Task<int> rd = ReadFullAsync(dst, bd.Memory.Slice(0, want), off, ct);
					await Task.WhenAll<int>(rs, rd).ConfigureAwait(continueOnCapturedContext: false);
					ns = rs.Result;
					int result = rd.Result;
					if (ns != want)
					{
						throw new IOException("The source changed while it was being read (it got shorter).");
					}
					if (result == ns)
					{
						Span<byte> span = bs.GetSpan();
						Span<byte> span2 = span.Slice(0, ns);
						span = bd.GetSpan();
						if (span2.SequenceEqual(span.Slice(0, result)))
						{
							continue;
						}
					}
					await RandomAccess.WriteAsync(dst, bs.Memory.Slice(0, ns), off, ct).ConfigureAwait(continueOnCapturedContext: false);
					written += ns;
				}
				if (RandomAccess.GetLength(dst) != it.Size)
				{
					RandomAccess.SetLength(dst, it.Size);
				}
				if (_o.FlushToDisk || (_o.Verify && _dstVol.Removable))
				{
					RandomAccess.FlushToDisk(dst);
				}
			}
			finally
			{
				Return(bs);
				Return(bd);
			}
		}
		if (_o.Verify)
		{
			await VerifyNativeAsync(it, it.Dst, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		try
		{
			File.SetAttributes(it.Dst, (it.Attrs != 0) ? it.Attrs : FileAttributes.Normal);
		}
		catch
		{
		}
		if (it.CtimeUtc != default(DateTime))
		{
			try
			{
				File.SetCreationTimeUtc(it.Dst, it.CtimeUtc);
			}
			catch
			{
			}
		}
		File.SetLastWriteTimeUtc(it.Dst, it.MtimeUtc);
		Interlocked.Increment(ref _nDelta);
		Interlocked.Add(ref _deltaWritten, written);
		Interlocked.Add(ref _deltaTotal, it.Size);
		return true;
	}

	private static async Task<int> ReadFullAsync(SafeFileHandle h, Memory<byte> buf, long offset, CancellationToken ct)
	{
		int got;
		int num;
		for (got = 0; got < buf.Length; got += num)
		{
			num = await RandomAccess.ReadAsync(h, buf.Slice(got), offset + got, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (num <= 0)
			{
				break;
			}
		}
		return got;
	}

	private Task WaitIfPaused(CancellationToken ct)
	{
		TaskCompletionSource pauseTcs = _pauseTcs;
		if (pauseTcs != null)
		{
			return pauseTcs.Task.WaitAsync(ct);
		}
		return Task.CompletedTask;
	}

	private AlignedBuffer Rent()
	{
		if (_pool.TryTake(out AlignedBuffer result))
		{
			return result;
		}
		result = new AlignedBuffer(_buf);
		_allBuffers.Enqueue(result);
		return result;
	}

	private void Return(AlignedBuffer b)
	{
		_pool.Add(b);
	}

	private void FreeBuffers()
	{
		_pool.Clear();
		AlignedBuffer result;
		while (_allBuffers.TryDequeue(out result))
		{
			result.Free();
		}
	}

	private void AddBytes(long n)
	{
		_throttle?.Wait(n, _jobCt);
		Interlocked.Add(ref _bytesDone, n);
	}

	private static int RoundUp(int n, int a = 4096)
	{
		return (n + a - 1) / a * a;
	}

	private HashKey Hash(AlignedBuffer b, int n)
	{
		return HashKey.Once(_o.VerifyAlgo, b.GetSpan().Slice(0, n));
	}

	private Strategy Choose(FileItem it)
	{
		bool isWindows = Native.IsWindows;
		bool flag = NeedsSplit(it.Size);
		bool flag2 = !_srcVol.Network && !_dstVol.Network;
		bool flag3 = isWindows && !flag && _o.LimitMBps <= 0 && _o.Method != CopyMethod.Pipeline && (_o.Method == CopyMethod.Native || SameVolume(it.Src, it.Dst));
		bool flag4 = isWindows;
		if (flag4)
		{
			flag4 = _o.Io switch
			{
				IoMode.Direct => true, 
				IoMode.Buffered => false, 
				_ => it.Size >= 67108864 && flag2, 
			};
		}
		bool direct = flag4;
		int workers = 1;
		if (!flag3 && it.Size > _buf)
		{
			if (_o.ChunkWorkers > 0)
			{
				workers = _o.ChunkWorkers;
			}
			else if (it.Size >= 536870912 && flag2 && !_srcVol.Removable && !_dstVol.Removable)
			{
				VolInfo srcVol = _srcVol;
				if (srcVol.SolidKnown && srcVol.Solid)
				{
					srcVol = _dstVol;
					if (srcVol.SolidKnown && srcVol.Solid)
					{
						workers = 3;
					}
				}
			}
		}
		return new Strategy(flag3, direct, workers, flag);
	}

	private Target BuildTarget(FileItem it, bool split)
	{
		FileItem it2 = it;
		Target target = new Target();
		if (split)
		{
			int count = PartCount(it2.Size);
			target.PartSize = PartSize;
			target.Final = (from i in Enumerable.Range(0, count)
				select PartName(it2.Dst, i)).ToArray();
		}
		else
		{
			target.Final = new string[1] { it2.Dst };
		}
		target.Tmp = target.Final.Select((string f) => f + ".cfpart").ToArray();
		target.H = new SafeFileHandle[target.Tmp.Length];
		return target;
	}

	private static SafeFileHandle OpenH(string path, bool write, bool direct, bool async, long prealloc, bool createNew = false)
	{
		FileOptions fileOptions = FileOptions.SequentialScan;
		if (async)
		{
			fileOptions |= FileOptions.Asynchronous;
		}
		if (direct)
		{
			fileOptions |= (FileOptions)536870912;
		}
		if (!write)
		{
			return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, fileOptions, 0L);
		}
		return File.OpenHandle(path, createNew ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None, fileOptions, prealloc);
	}

	private SafeFileHandle OpenSafe(string path, bool write, ref bool direct, bool async, long prealloc, bool createNew = false)
	{
		try
		{
			return OpenH(path, write, direct, async, prealloc, createNew);
		}
		catch (Exception ex) when (direct && (ex is IOException || ex is ArgumentException || ex is NotSupportedException || ex is UnauthorizedAccessException))
		{
			if (Interlocked.Exchange(ref _directFallbackLogged, 1) == 0)
			{
				Info($"Direct I/O is not available for {Path.GetPathRoot(path)} ({ex.Message.TrimEnd('.', ' ')}) - using cached I/O there.");
			}
			direct = false;
			return OpenH(path, write, direct: false, async, prealloc, createNew);
		}
	}

	private static bool IsExistsError(Exception ex)
	{
		bool flag = ex is IOException;
		if (flag)
		{
			int num = ex.HResult & 0xFFFF;
			bool flag2 = ((num == 80 || num == 183) ? true : false);
			flag = flag2;
		}
		return flag;
	}

	private static void UseTempNames(Target t)
	{
		t.InPlace = false;
		t.Tmp = t.Final.Select((string f) => f + ".cfpart").ToArray();
	}

	private void OpenTarget(Target t, long size, bool write, ref bool direct, bool async)
	{
		if (t.InPlace)
		{
			try
			{
				t.H[0] = OpenSafe(t.Tmp[0], write, ref direct, async, write ? size : 0, createNew: true);
				return;
			}
			catch (Exception ex) when (IsExistsError(ex))
			{
				UseTempNames(t);
			}
		}
		for (int i = 0; i < t.Tmp.Length; i++)
		{
			long num = Math.Min(Math.Max(size - i * ((t.PartSize == long.MaxValue) ? 0 : t.PartSize), 0L), (t.PartSize == long.MaxValue) ? size : t.PartSize);
			t.H[i] = OpenSafe(t.Tmp[i], write, ref direct, async, write ? num : 0);
		}
	}

	private static async ValueTask<int> ReadBlockAsync(SafeFileHandle h, Memory<byte> buf, long offset, bool async, CancellationToken ct)
	{
		int total;
		int num2;
		for (total = 0; total < buf.Length; total += num2)
		{
			int num = ((!async) ? RandomAccess.Read(h, buf.Span.Slice(total), offset + total) : (await RandomAccess.ReadAsync(h, buf.Slice(total), offset + total, ct).ConfigureAwait(continueOnCapturedContext: false)));
			num2 = num;
			if (num2 == 0)
			{
				break;
			}
		}
		return total;
	}

	private static async ValueTask WriteBlockAsync(Target t, long block, int blockSize, AlignedBuffer ab, int n, bool direct, bool async, CancellationToken ct)
	{
		long num = block * blockSize;
		int num2 = (int)(num / t.PartSize);
		long fileOffset = num % t.PartSize;
		int num3 = (direct ? RoundUp(n) : n);
		Span<byte> span;
		if (num3 > n)
		{
			span = ab.GetSpan();
			span = span.Slice(n, num3 - n);
			span.Clear();
		}
		SafeFileHandle handle = t.H[num2];
		if (async)
		{
			await RandomAccess.WriteAsync(handle, ab.Memory.Slice(0, num3), fileOffset, ct).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		span = ab.GetSpan();
		RandomAccess.Write(handle, span.Slice(0, num3), fileOffset);
	}

	private async Task RunBlocks(long blocks, int workers, Func<long, CancellationToken, ValueTask> body, CancellationToken ct)
	{
		Func<long, CancellationToken, ValueTask> body2 = body;
		if (workers <= 1 || blocks <= 1)
		{
			for (long b = 0L; b < blocks; b++)
			{
				await WaitIfPaused(ct).ConfigureAwait(continueOnCapturedContext: false);
				await body2(b, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			return;
		}
		long next = -1L;
		CancellationTokenSource lc = CancellationTokenSource.CreateLinkedTokenSource(ct);
		try
		{
			Task[] array = new Task[(int)Math.Min(workers, blocks)];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = Task.Run(async delegate
				{
					_ = 1;
					try
					{
						while (true)
						{
							await WaitIfPaused(lc.Token).ConfigureAwait(continueOnCapturedContext: false);
							long num = Interlocked.Increment(ref next);
							if (num >= blocks)
							{
								break;
							}
							await body2(num, lc.Token).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
					catch
					{
						lc.Cancel();
						throw;
					}
				}, CancellationToken.None);
			}
			Task all = Task.WhenAll(array);
			try
			{
				await all.ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				Exception ex = all.Exception?.InnerExceptions.FirstOrDefault((Exception e) => !(e is OperationCanceledException));
				if (ex != null)
				{
					ExceptionDispatchInfo.Capture(ex).Throw();
				}
				throw;
			}
		}
		finally
		{
			if (lc != null)
			{
				((IDisposable)lc).Dispose();
			}
		}
	}

	private void UsbNote(string path, string role)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		BusInfo bus = Native.ProbeBus(path);
		if (bus.IsUsb2OrOlder)
		{
			Info($"WARNING: the {role} drive is connected over {bus.Describe()} - expect about 30-40 MB/s at best. A USB 3 (blue or SS) port is much faster.");
		}
	}

	private bool CloneEligible(FileItem it, Strategy s)
	{
		if (!_o.BlockClone || !Native.IsWindows || s.Split || it.Size < 1048576 || Volatile.Read(ref _cloneFails) >= 3)
		{
			return false;
		}
		if (!SameVolume(it.Src, it.Dst))
		{
			return false;
		}
		return Native.Probe(it.Src).Fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase) && Native.Probe(it.Dst).Fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
	}

	private async Task CopyFileAsync(FileItem it, CancellationToken ct)
	{
		EnsureDir(Path.GetDirectoryName(it.Dst));
		if (await TryDeltaAsync(it, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			return;
		}
		Strategy s = Choose(it);
		Target t = BuildTarget(it, s.Split);
		if (!s.Split && it.Size <= 4194304)
		{
			VolInfo dstVol = _dstVol;
			if ((!dstVol.Removable || _o.DirectWriteRemovable) && !dstVol.Network)
			{
				t.Tmp = (string[])t.Final.Clone();
				t.InPlace = true;
			}
		}
		bool ok = false;
		long tk0 = Stopwatch.GetTimestamp();
		try
		{
			HashKey[] hashes = null;
			bool cloned = false;
			if (CloneEligible(it, s))
			{
				UseTempNames(t);
				cloned = Native.TryCloneFile(it.Src, t.Tmp[0], it.Size, out var cloneWhy);
				if (cloned)
				{
					AddBytes(it.Size);
					Interlocked.Increment(ref _nClone);
					if (_o.Verify)
					{
						await VerifyNativeAsync(it, t.Tmp[0], ct).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				else if (Interlocked.Increment(ref _cloneFails) == 3)
				{
					Info("Block cloning is not working here (" + cloneWhy + ") - copying normally from now on.");
				}
			}
			if (cloned)
			{
			}
			else if (s.Native)
			{
				if (!RunNativeCopy(it, t.Tmp[0], s.Direct, ct, t.InPlace))
				{
					UseTempNames(t);
					RunNativeCopy(it, t.Tmp[0], s.Direct, ct, failIfExists: false);
				}
				t.MetaSet = _metaNativeOk;
				Interlocked.Increment(ref _nNative);
				if (_o.Verify)
				{
					await VerifyNativeAsync(it, t.Tmp[0], ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				long val = (it.Size + _buf - 1) / _buf;
				if (_o.Verify)
				{
					hashes = new HashKey[Math.Max(1L, val)];
				}
				bool flag = await TransferAsync(it, t, s, hashes, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (s.Split)
				{
					Interlocked.Increment(ref _nSplit);
				}
				if (s.Workers > 1)
				{
					Interlocked.Increment(ref _nChunk);
				}
				else if (flag)
				{
					Interlocked.Increment(ref _nDirect);
				}
				else
				{
					Interlocked.Increment(ref _nBuffered);
				}
				if (hashes != null)
				{
					await VerifyTargetAsync(it, t, hashes, s, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			long timestamp = Stopwatch.GetTimestamp();
			Interlocked.Add(ref _ticksCopy, timestamp - tk0);
			if (t.InPlace)
			{
				Interlocked.Increment(ref _nInPlace);
			}
			t.Close();
			if (_extras && !s.Split)
			{
				try
				{
					Native.CopyStreams(it.Src, t.Tmp[0]);
				}
				catch (Exception ex)
				{
					Info(Path.GetFileName(it.Src) + ": alternate streams not copied (" + ex.Message.TrimEnd('.', ' ') + ").");
				}
			}
			else if (!s.Split && !s.Native && _zoneCapable)
			{
				try
				{
					Native.CopyStreams(it.Src, t.Tmp[0], zoneOnly: true);
				}
				catch
				{
				}
			}
			for (int i = 0; i < t.Tmp.Length; i++)
			{
				if (t.InPlace)
				{
					break;
				}
				try
				{
					File.Move(t.Tmp[i], t.Final[i], overwrite: true);
				}
				catch (UnauthorizedAccessException)
				{
					File.SetAttributes(t.Final[i], FileAttributes.Normal);
					File.Move(t.Tmp[i], t.Final[i], overwrite: true);
				}
			}
			if (t.MetaSet && it.MtimeUtc != default(DateTime) && Interlocked.Increment(ref _metaChecks) <= 4)
			{
				bool flag2;
				try
				{
					flag2 = TimeSame(File.GetLastWriteTimeUtc(t.Final[0]), it.MtimeUtc);
				}
				catch
				{
					flag2 = false;
				}
				if (!flag2)
				{
					if (s.Native)
					{
						_metaNativeOk = false;
					}
					else
					{
						_metaViaHandle = false;
					}
					t.MetaSet = false;
					Info("Timestamps set on the open handle did not stick on this drive; using the slower per-file method.");
				}
			}
			string[] final;
			if (!t.MetaSet)
			{
				DateTime dateTime = it.CtimeUtc;
				if (dateTime == default(DateTime))
				{
					try
					{
						dateTime = File.GetCreationTimeUtc(it.Src);
					}
					catch
					{
					}
				}
				for (int j = 0; j < t.Final.Length; j++)
				{
					try
					{
						if (dateTime != default(DateTime))
						{
							File.SetCreationTimeUtc(t.Final[j], dateTime);
						}
						File.SetLastWriteTimeUtc(t.Final[j], it.MtimeUtc);
					}
					catch
					{
					}
				}
				FileAttributes fileAttributes = it.Attrs;
				if (fileAttributes == FileAttributes.None)
				{
					try
					{
						fileAttributes = File.GetAttributes(it.Src);
					}
					catch
					{
						fileAttributes = FileAttributes.Normal;
					}
				}
				fileAttributes &= ~(FileAttributes.SparseFile | FileAttributes.ReparsePoint | FileAttributes.Offline);
				final = t.Final;
				foreach (string path in final)
				{
					try
					{
						File.SetAttributes(path, fileAttributes);
					}
					catch
					{
					}
				}
			}
			ok = true;
			Interlocked.Add(ref _ticksFinish, Stopwatch.GetTimestamp() - timestamp);
			if (!_extras)
			{
				return;
			}
			final = t.Final;
			foreach (string dst in final)
			{
				try
				{
					Native.CopySecurity(it.Src, dst);
				}
				catch (Exception ex3)
				{
					Info(Path.GetFileName(it.Src) + ": permissions not copied (" + ex3.Message.TrimEnd('.', ' ') + ").");
				}
			}
		}
		finally
		{
			t.Close();
			if (!ok)
			{
				string[] final = t.Tmp;
				foreach (string path2 in final)
				{
					try
					{
						if (File.Exists(path2))
						{
							File.SetAttributes(path2, FileAttributes.Normal);
						}
						File.Delete(path2);
					}
					catch
					{
					}
				}
			}
		}
	}

	private bool RunNativeCopy(FileItem it, string tmp, bool direct, CancellationToken ct, bool failIfExists)
	{
		long copied = 0L;
		if (!Native.CopyFile(it.Src, tmp, direct, delegate(long delta)
		{
			TaskCompletionSource pauseTcs = _pauseTcs;
			if (pauseTcs != null)
			{
				try
				{
					pauseTcs.Task.Wait(ct);
				}
				catch (OperationCanceledException)
				{
				}
			}
			if (ct.IsCancellationRequested)
			{
				return false;
			}
			copied += delta;
			AddBytes(delta);
			return true;
		}, failIfExists))
		{
			return false;
		}
		if (copied < it.Size)
		{
			AddBytes(it.Size - copied);
		}
		ct.ThrowIfCancellationRequested();
		return true;
	}

	private async Task VerifyNativeAsync(FileItem it, string tmp, CancellationToken ct)
	{
		bool direct = Native.IsWindows && !_dstVol.Network && _o.Io != IoMode.Buffered;
		Task<HashKey> a = HashWholeAsync(it.Src, it.Size, direct: false, count: false, ct);
		Task<HashKey> b = HashWholeAsync(tmp, it.Size, direct, count: true, ct);
		await Task.WhenAll<HashKey>(a, b).ConfigureAwait(continueOnCapturedContext: false);
		if (a.Result != b.Result)
		{
			throw new VerifyException("Verify failed: destination content does not match the source.");
		}
	}

	private async Task<HashKey> HashWholeAsync(string path, long size, bool direct, bool count, CancellationToken ct)
	{
		bool async = size > 1048576;
		using SafeFileHandle h = OpenSafe(path, write: false, ref direct, async, 0L);
		AlignedBuffer ab = Rent();
		try
		{
			using HashAcc hasher = new HashAcc(_o.VerifyAlgo);
			long off = 0L;
			int want;
			int num;
			do
			{
				await WaitIfPaused(ct).ConfigureAwait(continueOnCapturedContext: false);
				want = (int)(direct ? _buf : Math.Min(_buf, Math.Max(size - off, 1L)));
				num = await ReadBlockAsync(h, ab.Memory.Slice(0, want), off, async, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (num == 0)
				{
					break;
				}
				hasher.Append(ab.GetSpan().Slice(0, num));
				if (count)
				{
					AddBytes(num);
				}
				off += num;
			}
			while (num >= want);
			return hasher.Finish();
		}
		finally
		{
			Return(ab);
		}
	}

	private async Task<bool> TransferAsync(FileItem it, Target t, Strategy s, HashKey[]? hashes, CancellationToken ct)
	{
		bool async = it.Size > 1048576;
		bool sDirect = s.Direct;
		bool dDirect = s.Direct;
		long num = (it.Size + _buf - 1) / _buf;
		SafeFileHandle src = null;
		try
		{
			src = OpenSafe(it.Src, write: false, ref sDirect, async, 0L);
			OpenTarget(t, it.Size, write: true, ref dDirect, async);
			long num2 = ((num <= 1) ? (await TransferSingleAsync(it, src, t, sDirect, dDirect, async, hashes, ct).ConfigureAwait(continueOnCapturedContext: false)) : ((s.Workers <= 1) ? (await TransferSequentialAsync(it, src, t, sDirect, dDirect, num, hashes, ct).ConfigureAwait(continueOnCapturedContext: false)) : (await TransferChunkedAsync(it, src, t, s.Workers, sDirect, dDirect, num, hashes, ct).ConfigureAwait(continueOnCapturedContext: false))));
			if (num2 != it.Size)
			{
				throw new IOException("The source file changed size while it was being copied.");
			}
			for (int i = 0; i < t.H.Length; i++)
			{
				long num3 = ((t.PartSize == long.MaxValue) ? 0 : (i * t.PartSize));
				long length = Math.Min(Math.Max(num2 - num3, 0L), t.PartSize);
				RandomAccess.SetLength(t.H[i], length);
				if (_o.FlushToDisk || (_o.Verify && _dstVol.Removable))
				{
					RandomAccess.FlushToDisk(t.H[i]);
				}
			}
			if (_metaViaHandle && Native.IsWindows && it.CtimeUtc != default(DateTime))
			{
				bool flag = true;
				for (int j = 0; j < t.H.Length && flag; j++)
				{
					flag = Native.SetBasicInfo(t.H[j], it.CtimeUtc, it.MtimeUtc, it.Attrs);
				}
				t.MetaSet = flag;
			}
			return sDirect || dDirect;
		}
		finally
		{
			src?.Dispose();
			t.Close();
		}
	}

	private async Task<long> TransferSingleAsync(FileItem it, SafeFileHandle src, Target t, bool sDirect, bool dDirect, bool async, HashKey[]? hashes, CancellationToken ct)
	{
		if (it.Size == 0L)
		{
			return 0L;
		}
		AlignedBuffer ab = Rent();
		try
		{
			int length = (int)(sDirect ? RoundUp((int)it.Size) : it.Size);
			int i = await ReadBlockAsync(src, ab.Memory.Slice(0, length), 0L, async, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (hashes != null)
			{
				hashes[0] = Hash(ab, i);
			}
			if (i > 0)
			{
				await WriteBlockAsync(t, 0L, _buf, ab, i, dDirect, async, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			AddBytes(i);
			return i;
		}
		finally
		{
			Return(ab);
		}
	}

	private async Task<long> TransferSequentialAsync(FileItem it, SafeFileHandle src, Target t, bool sDirect, bool dDirect, long blocks, HashKey[]? hashes, CancellationToken ct)
	{
		FileItem it2 = it;
		SafeFileHandle src2 = src;
		HashKey[] hashes2 = hashes;
		Channel<(AlignedBuffer B, long Idx, int N)> ch = Channel.CreateBounded<(AlignedBuffer, long, int)>(new BoundedChannelOptions(4)
		{
			SingleReader = true,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait
		});
		using CancellationTokenSource lcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		CancellationToken tk = lcts.Token;
		Task reader = Task.Run(async delegate
		{
			_ = 2;
			try
			{
				for (long b = 0L; b < blocks; b++)
				{
					await WaitIfPaused(tk).ConfigureAwait(continueOnCapturedContext: false);
					AlignedBuffer ab2 = Rent();
					int length = (int)(sDirect ? _buf : Math.Min(_buf, it2.Size - b * _buf));
					int num;
					try
					{
						num = await ReadBlockAsync(src2, ab2.Memory.Slice(0, length), b * _buf, async: true, tk).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch
					{
						Return(ab2);
						throw;
					}
					if (num == 0)
					{
						Return(ab2);
						break;
					}
					if (hashes2 != null)
					{
						hashes2[b] = Hash(ab2, num);
					}
					await ch.Writer.WriteAsync((ab2, b, num), tk).ConfigureAwait(continueOnCapturedContext: false);
				}
				ch.Writer.TryComplete();
			}
			catch (Exception error)
			{
				ch.Writer.TryComplete(error);
			}
		}, CancellationToken.None);
		long total = 0L;
		try
		{
			_ = 2;
			try
			{
				await foreach (var (ab, block, i) in ch.Reader.ReadAllAsync(tk).ConfigureAwait(continueOnCapturedContext: false))
				{
					try
					{
						await WriteBlockAsync(t, block, _buf, ab, i, dDirect, async: true, tk).ConfigureAwait(continueOnCapturedContext: false);
					}
					finally
					{
						Return(ab);
					}
					total += i;
					AddBytes(i);
				}
			}
			catch
			{
				lcts.Cancel();
				throw;
			}
		}
		finally
		{
			await reader.ConfigureAwait(continueOnCapturedContext: false);
		}
		return total;
	}

	private async Task<long> TransferChunkedAsync(FileItem it, SafeFileHandle src, Target t, int workers, bool sDirect, bool dDirect, long blocks, HashKey[]? hashes, CancellationToken ct)
	{
		FileItem it2 = it;
		SafeFileHandle src2 = src;
		HashKey[] hashes2 = hashes;
		Target t2 = t;
		long[] total = new long[1];
		await RunBlocks(blocks, workers, async delegate(long b, CancellationToken tk)
		{
			AlignedBuffer ab = Rent();
			try
			{
				long expect = Math.Min(_buf, it2.Size - b * _buf);
				int length = (int)(sDirect ? _buf : expect);
				int i = await ReadBlockAsync(src2, ab.Memory.Slice(0, length), b * _buf, async: true, tk).ConfigureAwait(continueOnCapturedContext: false);
				if (i != expect)
				{
					throw new IOException("The source file changed size while it was being copied.");
				}
				if (hashes2 != null)
				{
					hashes2[b] = Hash(ab, i);
				}
				await WriteBlockAsync(t2, b, _buf, ab, i, dDirect, async: true, tk).ConfigureAwait(continueOnCapturedContext: false);
				Interlocked.Add(ref total[0], i);
				AddBytes(i);
			}
			finally
			{
				Return(ab);
			}
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		return total[0];
	}

	private async Task VerifyTargetAsync(FileItem it, Target t, HashKey[] hashes, Strategy s, CancellationToken ct)
	{
		FileItem it2 = it;
		Target t2 = t;
		HashKey[] hashes2 = hashes;
		bool async = it2.Size > 1048576;
		bool direct = Native.IsWindows && !_dstVol.Network && _o.Io != IoMode.Buffered;
		long blocks = (it2.Size + _buf - 1) / _buf;
		OpenTarget(t2, it2.Size, write: false, ref direct, async);
		try
		{
			long num = 0L;
			SafeFileHandle[] h = t2.H;
			foreach (SafeFileHandle handle in h)
			{
				num += RandomAccess.GetLength(handle);
			}
			if (num != it2.Size)
			{
				throw new VerifyException($"Verify failed: destination is {FormatBytes(num)} but the source is {FormatBytes(it2.Size)}.");
			}
			await RunBlocks(blocks, s.Workers, async delegate(long b, CancellationToken tk)
			{
				AlignedBuffer ab = Rent();
				try
				{
					long off = b * _buf;
					long expect = Math.Min(_buf, it2.Size - off);
					int num2 = (int)(off / t2.PartSize);
					long offset = off % t2.PartSize;
					int length = (int)(direct ? _buf : expect);
					int num3 = await ReadBlockAsync(t2.H[num2], ab.Memory.Slice(0, length), offset, async, tk).ConfigureAwait(continueOnCapturedContext: false);
					if (num3 != expect || Hash(ab, num3) != hashes2[b])
					{
						throw new VerifyException($"Verify failed at offset {off:N0}: destination content does not match the source.");
					}
					AddBytes(num3);
				}
				finally
				{
					Return(ab);
				}
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			t2.Close();
		}
	}

	private PlanPreview MakeCopyPreview(Plan plan)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		long num = 0L;
		long num2 = 0L;
		long count = 0L;
		bool trunc = false;
		foreach (FileItem file in plan.Files)
		{
			if (!file.SkipCopy)
			{
				num++;
				num2 += file.Size;
				if (list.Count < 5000)
				{
					list.Add(file.Src);
				}
				else
				{
					trunc = true;
				}
			}
		}
		if (_o.Mode == JobMode.Sync)
		{
			foreach (string syncRoot in plan.SyncRoots)
			{
				PrunePreview(syncRoot, "", plan, list2, ref count, ref trunc);
			}
		}
		return new PlanPreview(num, num2, _skipped, count, list, list2, trunc);
	}

	private PlanPreview MakeDeletePreview(Plan plan)
	{
		List<string> deleteList = (from f in plan.Files.Take(5000)
			select f.Src).ToList();
		return new PlanPreview(0L, 0L, 0L, plan.Files.Count, new List<string>(), deleteList, plan.Files.Count > 5000);
	}

	private void PrunePreview(string dir, string rel, Plan plan, List<string> del, ref long count, ref bool trunc)
	{
		foreach (FileSystemInfo item in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", EnumOpts).ToList())
		{
			if (IsLink(item))
			{
				continue;
			}
			string text;
			if (rel.Length != 0)
			{
				ReadOnlySpan<char> readOnlySpan = rel;
				char reference = Path.DirectorySeparatorChar;
				text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference), item.Name);
			}
			else
			{
				text = item.Name;
			}
			string rel2 = text;
			try
			{
				if (item is DirectoryInfo directoryInfo)
				{
					if (!_filter.DirExcluded(rel2, directoryInfo.Name))
					{
						PrunePreview(directoryInfo.FullName, rel2, plan, del, ref count, ref trunc);
					}
				}
				else if (item is FileInfo fileInfo && !plan.Expected.Contains(fileInfo.FullName) && (fileInfo.Name.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase) || _filter.FileAccepted(rel2, fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTimeUtc)))
				{
					count++;
					if (del.Count < 5000)
					{
						del.Add(fileInfo.FullName);
					}
					else
					{
						trunc = true;
					}
				}
			}
			catch
			{
			}
		}
	}

	private static bool IsLockedFile(Exception ex)
	{
		bool flag = ex is IOException;
		if (flag)
		{
			int num = ex.HResult & 0xFFFF;
			bool flag2 = (uint)(num - 32) <= 1u;
			flag = flag2;
		}
		if (!flag && !ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
		{
			return ex.Message.Contains("locked a portion of the file", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private string? ShadowPathFor(string src)
	{
		lock (_vssGate)
		{
			if (_vss == null)
			{
				_vss = new ShadowSet(delegate(string m)
				{
					Info(m);
				});
			}
		}
		return _vss.Map(src);
	}

	internal static bool IsBadMedia(Exception ex)
	{
		bool flag = ex is IOException;
		if (flag)
		{
			bool flag2;
			switch (ex.HResult & 0xFFFF)
			{
			case 23:
			case 27:
			case 1117:
			case 1392:
			case 1393:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (!flag)
		{
			return ex.Message.Contains("cyclic redundancy", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private async Task RunTunedAsync(List<FileItem> files, CancellationToken ct)
	{
		int max = Math.Clamp(Math.Max(_threads * 2, 8), 4, 32);
		int min = 1;
		int num = Math.Clamp(_threads, min, max);
		AutoTuner tuner = new AutoTuner(num, min, max);
		SemaphoreSlim gate = new SemaphoreSlim(max, max);
		int held = max - num;
		for (int i = 0; i < held; i++)
		{
			gate.Wait();
		}
		_tuneStart = num;
		_tuneEnd = num;
		_tuneLo = num;
		_tuneHi = num;
		CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
		try
		{
			Task controller = Task.Run(async delegate
			{
				long lastB = Interlocked.Read(ref _bytesDone);
				long lastF = Interlocked.Read(ref _filesDone);
				long lastT = Stopwatch.GetTimestamp();
				try
				{
					while (!stop.IsCancellationRequested)
					{
						await Task.Delay(2000, stop.Token).ConfigureAwait(continueOnCapturedContext: false);
						if (IsPaused)
						{
							lastT = Stopwatch.GetTimestamp();
							lastB = Interlocked.Read(ref _bytesDone);
							lastF = Interlocked.Read(ref _filesDone);
						}
						else
						{
							long timestamp = Stopwatch.GetTimestamp();
							long num2 = Interlocked.Read(ref _bytesDone);
							long num3 = Interlocked.Read(ref _filesDone);
							double num4 = (double)(timestamp - lastT) / (double)Stopwatch.Frequency;
							if (!(num4 < 0.5))
							{
								double scorePerSec = AutoTuner.Score(num2 - lastB, num3 - lastF) / num4;
								lastT = timestamp;
								lastB = num2;
								lastF = num3;
								int want = tuner.Next(scorePerSec);
								_tuneSamples++;
								int wantHeld;
								for (wantHeld = max - want; held < wantHeld; held++)
								{
									await gate.WaitAsync(stop.Token).ConfigureAwait(continueOnCapturedContext: false);
								}
								for (; held > wantHeld; held--)
								{
									gate.Release();
								}
								_tuneEnd = want;
								_tuneLo = Math.Min(_tuneLo, want);
								_tuneHi = Math.Max(_tuneHi, want);
							}
						}
					}
				}
				catch (OperationCanceledException)
				{
				}
			});
			try
			{
				ParallelOptions parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = max,
					CancellationToken = ct
				};
				await Parallel.ForEachAsync(files, parallelOptions, async delegate(FileItem it, CancellationToken t)
				{
					await gate.WaitAsync(t).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						await ProcessFileAsync(it, t).ConfigureAwait(continueOnCapturedContext: false);
					}
					finally
					{
						gate.Release();
					}
				}).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				stop.Cancel();
				try
				{
					await controller.ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
				}
			}
		}
		finally
		{
			if (stop != null)
			{
				((IDisposable)stop).Dispose();
			}
		}
	}
}
