using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine;

public sealed class Watcher
{
	private readonly JobOptions _template;

	private readonly TimeSpan _debounce;

	private readonly TimeSpan _poll;

	private readonly List<FileSystemWatcher> _fsw = new List<FileSystemWatcher>();

	private long _lastEventTicks;

	private int _dirty;

	private DateTime _retryAfterUtc = DateTime.MinValue;

	public int JobsRun { get; private set; }

	public event Action<WatchEvent>? Event;

	public Watcher(JobOptions template, TimeSpan? debounce = null, TimeSpan? poll = null)
	{
		JobMode mode = template.Mode;
		if ((mode != 0 && mode != JobMode.Sync) || 1 == 0)
		{
			throw new ArgumentException("Watch mode supports copy or sync only.");
		}
		if (template.Sources.Count == 0 || string.IsNullOrWhiteSpace(template.Destination))
		{
			throw new ArgumentException("Watch needs at least one source and a destination.");
		}
		_template = template.Clone();
		_template.Sources = new List<string>(template.Sources);
		_debounce = debounce ?? TimeSpan.FromSeconds(5.0);
		_poll = poll ?? TimeSpan.FromSeconds(1.0);
	}

	private void Say(string kind, string text, JobResult? r = null)
	{
		this.Event?.Invoke(new WatchEvent(kind, text, r));
	}

	private bool DestinationReady()
	{
		try
		{
			string pathRoot = Path.GetPathRoot(Path.GetFullPath(_template.Destination));
			if (!string.IsNullOrEmpty(pathRoot) && !Directory.Exists(pathRoot))
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void Touch(string? path)
	{
		Interlocked.Exchange(ref _lastEventTicks, Stopwatch.GetTimestamp());
		Interlocked.Exchange(ref _dirty, 1);
	}

	private void Arm(string src)
	{
		FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(Directory.Exists(src) ? src : (Path.GetDirectoryName(Path.GetFullPath(src)) ?? src))
		{
			IncludeSubdirectories = Directory.Exists(src),
			NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime),
			InternalBufferSize = 65536
		};
		fileSystemWatcher.Created += delegate(object _, FileSystemEventArgs e)
		{
			Touch(e.FullPath);
		};
		fileSystemWatcher.Changed += delegate(object _, FileSystemEventArgs e)
		{
			Touch(e.FullPath);
		};
		fileSystemWatcher.Renamed += delegate(object _, RenamedEventArgs e)
		{
			Touch(e.FullPath);
		};
		fileSystemWatcher.Deleted += delegate(object _, FileSystemEventArgs e)
		{
			if (_template.Mode == JobMode.Sync)
			{
				Touch(e.FullPath);
			}
		};
		fileSystemWatcher.Error += delegate(object _, ErrorEventArgs e)
		{
			Say("warn", "Change buffer overflowed - a full re-scan will run: " + e.GetException().Message);
			Touch(null);
		};
		fileSystemWatcher.EnableRaisingEvents = true;
		_fsw.Add(fileSystemWatcher);
	}

	public async Task RunAsync(CancellationToken ct)
	{
		foreach (string source in _template.Sources)
		{
			try
			{
				Arm(source);
			}
			catch (Exception ex)
			{
				Say("error", "Cannot watch " + source + ": " + ex.Message);
			}
		}
		if (_fsw.Count == 0)
		{
			throw new InvalidOperationException("No source could be watched.");
		}
		Say("info", $"Watching {_fsw.Count} location(s) -> {_template.Destination} (waits {_debounce.TotalSeconds:0} s after the last change).");
		bool destWas = DestinationReady();
		Touch(null);
		try
		{
			while (!ct.IsCancellationRequested)
			{
				await Task.Delay(_poll, ct).ConfigureAwait(continueOnCapturedContext: false);
				bool flag = DestinationReady();
				if (flag && !destWas)
				{
					Say("info", "Destination is back - catching up.");
					Touch(null);
					_retryAfterUtc = DateTime.MinValue;
				}
				if (!flag && destWas)
				{
					Say("warn", "Destination went away - waiting for it to return.");
				}
				destWas = flag;
				if (Volatile.Read(ref _dirty) != 0 && flag && !(DateTime.UtcNow < _retryAfterUtc) && !((double)(Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastEventTicks)) / (double)Stopwatch.Frequency < _debounce.TotalSeconds))
				{
					Interlocked.Exchange(ref _dirty, 0);
					CopyJob copyJob = new CopyJob(_template.Clone());
					copyJob.Message += delegate(string m)
					{
						Say("log", m);
					};
					Say("start", "Change detected - syncing.");
					JobResult jobResult;
					try
					{
						jobResult = await copyJob.RunAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex3)
					{
						Say("error", ex3.Message);
						Interlocked.Exchange(ref _dirty, 1);
						_retryAfterUtc = DateTime.UtcNow.AddSeconds(30.0);
						continue;
					}
					if (jobResult.Cancelled)
					{
						break;
					}
					JobsRun++;
					Say("done", $"Pass done: {jobResult.FilesProcessed:N0} copied, {jobResult.FilesSkipped:N0} unchanged, {jobResult.FilesDeleted:N0} removed, {jobResult.FilesFailed:N0} failed.", jobResult);
					if (jobResult.FilesFailed > 0)
					{
						Interlocked.Exchange(ref _dirty, 1);
						_retryAfterUtc = DateTime.UtcNow.AddSeconds(30.0);
						Say("warn", "Some files failed (maybe still being written). Retrying in 30 s.");
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			foreach (FileSystemWatcher item in _fsw)
			{
				try
				{
					item.EnableRaisingEvents = false;
					item.Dispose();
				}
				catch
				{
				}
			}
			_fsw.Clear();
		}
		Say("info", "Watch stopped.");
	}
}
