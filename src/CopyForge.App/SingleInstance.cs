using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.App;

internal sealed class SingleInstance : IDisposable
{
	private const string PipeName = "CopyForge.Pipe.v1";

	private readonly Mutex _mutex;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	public bool IsFirst { get; }

	public SingleInstance()
	{
		_mutex = new Mutex(initiallyOwned: true, "Local\\CopyForge.SingleInstance", out var createdNew);
		IsFirst = createdNew;
	}

	public static bool Forward(string[] args)
	{
		try
		{
			using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "CopyForge.Pipe.v1", PipeDirection.Out, PipeOptions.CurrentUserOnly);
			namedPipeClientStream.Connect(5000);
			using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream);
			foreach (string value in args)
			{
				streamWriter.WriteLine(value);
			}
			streamWriter.Flush();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public void Listen(Action<string[]> onPaths)
	{
		Action<string[]> onPaths2 = onPaths;
		Task.Run(async delegate
		{
			string i = default(string);
			while (!_cts.IsCancellationRequested)
			{
				try
				{
					using (NamedPipeServerStream s = new NamedPipeServerStream("CopyForge.Pipe.v1", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous))
					{
						await s.WaitForConnectionAsync(_cts.Token);
						using StreamReader r = new StreamReader(s);
						List<string> lines = new List<string>();
						while (true)
						{
							bool flag = lines.Count < 256;
							if (flag)
							{
								string text;
								i = (text = await r.ReadLineAsync());
								flag = text != null;
							}
							if (!flag)
							{
								break;
							}
							int length = i.Length;
							if (length > 0 && length < 32768)
							{
								lines.Add(i);
							}
						}
						if (lines.Count > 0)
						{
							onPaths2(lines.ToArray());
						}
					}
					i = null;
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch
				{
					await Task.Delay(200);
				}
			}
		});
	}

	public void Dispose()
	{
		_cts.Cancel();
		_mutex.Dispose();
	}
}
