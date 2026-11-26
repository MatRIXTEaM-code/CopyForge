using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CopyForge.Engine;

/// <summary>Keeps Windows from sleeping while a job runs. Does nothing on other systems.</summary>
public sealed class KeepAwake : IDisposable
{
	[DllImport("kernel32.dll")]
	private static extern uint SetThreadExecutionState(uint flags);

	private const uint Continuous = 0x80000000u;

	private const uint SystemRequired = 1u;

	private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);

	private readonly Thread? _t;

	public KeepAwake()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		_t = new Thread((ThreadStart)delegate
		{
			try
			{
				SetThreadExecutionState(Continuous | SystemRequired);
				_stop.Wait();
			}
			catch
			{
			}
			finally
			{
				try
				{
					SetThreadExecutionState(Continuous);
				}
				catch
				{
				}
			}
		})
		{
			IsBackground = true,
			Name = "CopyForge keep-awake"
		};
		_t.Start();
	}

	public void Dispose()
	{
		_stop.Set();
		_t?.Join(2000);
	}
}

/// <summary>Limits throughput to a number of bytes per second (shared by all worker threads).</summary>
public sealed class Throttle
{
	private readonly long _bps;

	private readonly object _gate = new object();

	private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

	private double _next;

	public Throttle(long bytesPerSecond)
	{
		_bps = Math.Max(1L, bytesPerSecond);
	}

	/// <summary>Blocks until it is time to account for <paramref name="n"/> more bytes.</summary>
	public void Wait(long n, CancellationToken ct)
	{
		double delaySec;
		lock (_gate)
		{
			double now = _sw.Elapsed.TotalSeconds;
			if (_next < now - 1.0)
			{
				_next = now;
			}
			delaySec = _next - now;
			_next += (double)n / (double)_bps;
		}
		while (delaySec > 0.0 && !ct.IsCancellationRequested)
		{
			int ms = (int)Math.Min(200.0, Math.Ceiling(delaySec * 1000.0));
			ct.WaitHandle.WaitOne(ms);
			delaySec -= ms / 1000.0;
		}
	}
}

/// <summary>Per-file CSV log of a job (status, source, destination, size, note).</summary>
public sealed class JobLog : IDisposable
{
	private readonly StreamWriter _w;

	private readonly object _gate = new object();

	public JobLog(string path)
	{
		string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		_w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		if (_w.BaseStream.Length == 0L)
		{
			_w.WriteLine("time,status,source,destination,bytes,note");
		}
	}

	private static string Q(string? s)
	{
		s ??= "";
		if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0)
		{
			s = "'" + s;
		}
		return "\"" + s.Replace("\"", "\"\"") + "\"";
	}

	public void Row(string status, string src, string dst, long bytes, string note = "")
	{
		lock (_gate)
		{
			try
			{
				_w.WriteLine(string.Join(",", Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), Q(status), Q(src), Q(dst), bytes.ToString(), Q(note)));
			}
			catch
			{
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			try
			{
				_w.Flush();
				_w.Dispose();
			}
			catch
			{
			}
		}
	}
}
