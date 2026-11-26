using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CopyForge.Engine;

public static class UsnIncremental
{
	public sealed class Prepared
	{
		public JobOptions Options { get; init; } = new JobOptions();


		public bool Incremental { get; init; }

		public int ChangedFiles { get; init; }

		public Action<JobResult> Commit { get; init; } = delegate
		{
		};

	}

	private sealed class State
	{
		public string Key { get; set; } = "";


		public ulong JournalId { get; set; }

		public long NextUsn { get; set; }

		public string LastFullUtc { get; set; } = "";

	}

	private struct USN_JOURNAL_DATA
	{
		public ulong UsnJournalID;

		public long FirstUsn;

		public long NextUsn;

		public long LowestValidUsn;

		public long MaxUsn;

		public ulong MaximumSize;

		public ulong AllocationDelta;
	}

	private struct READ_USN_JOURNAL_DATA
	{
		public long StartUsn;

		public uint ReasonMask;

		public uint ReturnOnlyOnClose;

		public ulong Timeout;

		public ulong BytesToWaitFor;

		public ulong UsnJournalID;
	}

	private struct FILE_ID_DESCRIPTOR
	{
		public uint dwSize;

		public int Type;

		public ulong FileId;

		public ulong Pad;
	}

	private const uint FSCTL_QUERY_USN_JOURNAL = 590068u;

	private const uint FSCTL_READ_USN_JOURNAL = 590011u;

	private const uint GENERIC_READ = 2147483648u;

	private const uint FILE_SHARE_ALL = 7u;

	private const uint OPEN_EXISTING = 3u;

	private const uint FILE_FLAG_BACKUP_SEMANTICS = 33554432u;

	private const uint REASON_FILE_CREATE = 256u;

	private const uint REASON_RENAME_NEW = 8192u;

	public static int FullScanEveryDays { get; set; } = 7;


	public static string StateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyForge", "usn");

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint sec, uint disp, uint flags, nint tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref READ_USN_JOURNAL_DATA inb, int inSize, byte[] outb, int outSize, out int ret, nint ov);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle h, uint code, nint inb, int inSize, out USN_JOURNAL_DATA outb, int outSize, out int ret, nint ov);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, ref FILE_ID_DESCRIPTOR id, uint access, uint share, nint sec, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
	private static extern int GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder sb, int len, uint flags);

	public static Prepared Prepare(JobOptions o, Action<string> info, bool forceFull = false)
	{
		Prepared result = new Prepared
		{
			Options = o
		};
		try
		{
			if (!Native.IsWindows)
			{
				info("USN scan: Windows only - normal scan.");
				return result;
			}
			if (o.Mode != 0 || o.ModifiedSinceUtc.HasValue || o.ExplicitPairs != null)
			{
				info("USN scan: only for plain copy jobs - normal scan.");
				return result;
			}
			if (o.Sources.Count == 0 || o.Sources.Any((string s) => !Directory.Exists(s)))
			{
				info("USN scan: sources must be folders - normal scan.");
				return result;
			}
			string vol = Path.GetPathRoot(Path.GetFullPath(o.Sources[0]));
			if (vol.Length < 2 || vol[1] != ':' || o.Sources.Any((string s) => !string.Equals(Path.GetPathRoot(Path.GetFullPath(s)), vol, StringComparison.OrdinalIgnoreCase)))
			{
				info("USN scan: all sources must be on one local drive - normal scan.");
				return result;
			}
			if (!new DriveInfo(vol).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
			{
				info("USN scan: the source drive is not NTFS - normal scan.");
				return result;
			}
			using SafeFileHandle safeFileHandle = CreateFile("\\\\.\\" + vol.TrimEnd('\\'), 2147483648u, 7u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				info("USN scan: needs an elevated (administrator) process to read the change journal - normal scan.");
				return result;
			}
			if (!DeviceIoControl(safeFileHandle, 590068u, IntPtr.Zero, 0, out var jd, Marshal.SizeOf<USN_JOURNAL_DATA>(), out var _, IntPtr.Zero))
			{
				info("USN scan: this drive has no change journal - normal scan.");
				return result;
			}
			string key = Key(o);
			State st = Load(key);
			long baseline = jd.NextUsn;
			Action<JobResult> commit = delegate(JobResult r)
			{
				if (!r.Cancelled && r.FilesFailed <= 0)
				{
					Save(key, new State
					{
						Key = key,
						JournalId = jd.UsnJournalID,
						NextUsn = baseline,
						LastFullUtc = DateTime.UtcNow.ToString("o")
					});
				}
			};
			string text = null;
			DateTime result2;
			if (forceFull)
			{
				text = "forced";
			}
			else if (st == null)
			{
				text = "first run";
			}
			else if (st.JournalId != jd.UsnJournalID)
			{
				text = "journal was recreated";
			}
			else if (st.NextUsn < jd.LowestValidUsn || st.NextUsn < jd.FirstUsn)
			{
				text = "journal history no longer reaches back far enough";
			}
			else if (!DateTime.TryParse(st.LastFullUtc, null, DateTimeStyles.RoundtripKind, out result2) || (DateTime.UtcNow - result2).TotalDays >= (double)FullScanEveryDays)
			{
				text = $"periodic safety re-scan (every {FullScanEveryDays} days)";
			}
			if (text != null)
			{
				info("USN scan: running a full scan (" + text + "); the change position is saved for next time.");
				return new Prepared
				{
					Options = o,
					Commit = commit
				};
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!ReadChanges(safeFileHandle, st, jd, hashSet, hashSet2, info))
			{
				info("USN scan: could not read the journal reliably - running a full scan instead.");
				return new Prepared
				{
					Options = o,
					Commit = commit
				};
			}
			PathFilter pathFilter = new PathFilter(o);
			List<FilePair> list = new List<FilePair>();
			HashSet<string> hashSet3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string item in hashSet2)
			{
				foreach (string item2 in SafeEnum(item))
				{
					hashSet.Add(item2);
				}
			}
			foreach (string item3 in hashSet)
			{
				foreach (string source in o.Sources)
				{
					string text2 = Path.GetFullPath(source).TrimEnd('\\');
					if (!item3.StartsWith(text2 + "\\", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					string text3 = item3.Substring(text2.Length + 1);
					FileInfo fi = new FileInfo(item3);
					if (fi.Exists && ((fi.Attributes & FileAttributes.ReparsePoint) == 0 || !Safe(() => fi.LinkTarget != null)) && !ExcludedByFolder(pathFilter, text3) && pathFilter.FileAccepted(text3, fi.Name, fi.Length, fi.LastWriteTimeUtc))
					{
						string text4 = Path.Combine(o.Destination, CopyJob.SafeName(new DirectoryInfo(text2)), text3);
						if (hashSet3.Add(text4))
						{
							list.Add(new FilePair(item3, text4));
						}
					}
					break;
				}
			}
			info($"USN scan: {list.Count:N0} changed file(s) since the last run - skipping the full folder walk.");
			JobOptions jobOptions = o.Clone();
			jobOptions.ExplicitPairs = list;
			return new Prepared
			{
				Options = jobOptions,
				Incremental = true,
				ChangedFiles = list.Count,
				Commit = delegate(JobResult r)
				{
					if (!r.Cancelled && r.FilesFailed <= 0)
					{
						Save(key, new State
						{
							Key = key,
							JournalId = jd.UsnJournalID,
							NextUsn = baseline,
							LastFullUtc = st.LastFullUtc
						});
					}
				}
			};
		}
		catch (Exception ex)
		{
			info("USN scan: unexpected problem (" + ex.Message + ") - normal scan.");
			return result;
		}
	}

	private static bool Safe(Func<bool> f)
	{
		try
		{
			return f();
		}
		catch
		{
			return true;
		}
	}

	private static bool ExcludedByFolder(PathFilter f, string rel)
	{
		string[] array = rel.Split('\\');
		string text = "";
		for (int i = 0; i < array.Length - 1; i++)
		{
			text = ((text.Length == 0) ? array[i] : (text + "\\" + array[i]));
			if (f.DirExcluded(text, array[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<string> SafeEnum(string dir)
	{
		List<string> list = new List<string>();
		try
		{
			foreach (FileInfo item in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions
			{
				RecurseSubdirectories = true,
				IgnoreInaccessible = true,
				AttributesToSkip = FileAttributes.None
			}))
			{
				list.Add(item.FullName);
			}
		}
		catch
		{
		}
		return list;
	}

	private static string Key(JobOptions o)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in from x in o.Sources
			select Path.GetFullPath(x).TrimEnd('\\').ToLowerInvariant() into x
			orderby x
			select x)
		{
			stringBuilder.Append(item).Append('|');
		}
		stringBuilder.Append(Path.GetFullPath(o.Destination).TrimEnd('\\').ToLowerInvariant()).Append('|').Append(o.Include)
			.Append('|')
			.Append(o.Exclude)
			.Append('|')
			.Append(o.MinSizeBytes)
			.Append('|')
			.Append(o.MaxSizeBytes);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stringBuilder.ToString()))).Substring(0, 24).ToLowerInvariant();
	}

	private static State? Load(string key)
	{
		try
		{
			string path = Path.Combine(StateDir, key + ".json");
			if (!File.Exists(path))
			{
				return null;
			}
			State state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
			return (state != null && state.Key == key) ? state : null;
		}
		catch
		{
			return null;
		}
	}

	private static void Save(string key, State s)
	{
		try
		{
			Directory.CreateDirectory(StateDir);
			string text = Path.Combine(StateDir, key + ".json");
			File.WriteAllText(text + ".tmp", JsonSerializer.Serialize(s));
			File.Move(text + ".tmp", text, overwrite: true);
		}
		catch
		{
		}
	}

	private static bool ReadChanges(SafeFileHandle vol, State st, USN_JOURNAL_DATA jd, HashSet<string> files, HashSet<string> dirs, Action<string> info)
	{
		byte[] array = new byte[65536];
		long num = st.NextUsn;
		HashSet<ulong> hashSet = new HashSet<ulong>();
		HashSet<ulong> hashSet2 = new HashSet<ulong>();
		int num2 = 0;
		while (num < jd.NextUsn)
		{
			if (++num2 > 1000000)
			{
				return false;
			}
			READ_USN_JOURNAL_DATA rEAD_USN_JOURNAL_DATA = default(READ_USN_JOURNAL_DATA);
			rEAD_USN_JOURNAL_DATA.StartUsn = num;
			rEAD_USN_JOURNAL_DATA.ReasonMask = uint.MaxValue;
			rEAD_USN_JOURNAL_DATA.ReturnOnlyOnClose = 1u;
			rEAD_USN_JOURNAL_DATA.Timeout = 0uL;
			rEAD_USN_JOURNAL_DATA.BytesToWaitFor = 0uL;
			rEAD_USN_JOURNAL_DATA.UsnJournalID = jd.UsnJournalID;
			READ_USN_JOURNAL_DATA inb = rEAD_USN_JOURNAL_DATA;
			if (!DeviceIoControl(vol, 590011u, ref inb, Marshal.SizeOf<READ_USN_JOURNAL_DATA>(), array, array.Length, out var ret, IntPtr.Zero))
			{
				return false;
			}
			if (ret < 8)
			{
				return false;
			}
			long num3 = BitConverter.ToInt64(array, 0);
			int num4;
			for (int i = 8; i + 60 <= ret; i += num4)
			{
				num4 = BitConverter.ToInt32(array, i);
				if (num4 < 60 || i + num4 > ret)
				{
					return false;
				}
				if (BitConverter.ToUInt16(array, i + 4) != 2)
				{
					return false;
				}
				ulong item = BitConverter.ToUInt64(array, i + 8);
				uint num5 = BitConverter.ToUInt32(array, i + 40);
				if ((BitConverter.ToUInt32(array, i + 52) & 0x10u) != 0)
				{
					if ((num5 & 0x2100u) != 0)
					{
						hashSet2.Add(item);
					}
				}
				else
				{
					hashSet.Add(item);
				}
			}
			if (num3 <= num)
			{
				break;
			}
			num = num3;
		}
		foreach (ulong item2 in hashSet)
		{
			string text = PathOf(vol, item2);
			if (text != null)
			{
				files.Add(text);
			}
		}
		foreach (ulong item3 in hashSet2)
		{
			string text2 = PathOf(vol, item3);
			if (text2 != null)
			{
				dirs.Add(text2);
			}
		}
		info($"USN scan: journal reports {hashSet.Count:N0} changed file(s), {hashSet2.Count:N0} new/renamed folder(s).");
		return true;
	}

	private static string? PathOf(SafeFileHandle vol, ulong fileRef)
	{
		FILE_ID_DESCRIPTOR fILE_ID_DESCRIPTOR = default(FILE_ID_DESCRIPTOR);
		fILE_ID_DESCRIPTOR.dwSize = (uint)Marshal.SizeOf<FILE_ID_DESCRIPTOR>();
		fILE_ID_DESCRIPTOR.Type = 0;
		fILE_ID_DESCRIPTOR.FileId = fileRef;
		FILE_ID_DESCRIPTOR id = fILE_ID_DESCRIPTOR;
		using SafeFileHandle safeFileHandle = OpenFileById(vol, ref id, 0u, 7u, IntPtr.Zero, 33554432u);
		if (safeFileHandle.IsInvalid)
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(1024);
		int finalPathNameByHandle = GetFinalPathNameByHandle(safeFileHandle, stringBuilder, stringBuilder.Capacity, 0u);
		if (finalPathNameByHandle <= 0 || finalPathNameByHandle >= stringBuilder.Capacity)
		{
			return null;
		}
		string text = stringBuilder.ToString();
		return text.StartsWith("\\\\?\\", StringComparison.Ordinal) ? text.Substring(4) : text;
	}
}
