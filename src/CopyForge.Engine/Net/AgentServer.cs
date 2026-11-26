using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine.Net;

public sealed class AgentServer
{
	private readonly string _root;

	private readonly byte[] _psk;

	private readonly IPAddress _bind;

	private readonly X509Certificate2 _cert;

	private readonly byte[] _certHash;

	private readonly ConcurrentDictionary<string, (int fails, DateTime until)> _bad = new ConcurrentDictionary<string, (int, DateTime)>();

	private int _active;

	private readonly ConcurrentDictionary<string, int> _perIp = new ConcurrentDictionary<string, int>();

	public int MaxPerAddress { get; set; } = 2;


	private TcpListener? _listener;

	public int Port { get; private set; }

	public string Fingerprint => Wire.Hex(_certHash);

	public int MaxConnections { get; set; } = 4;


	public long MaxFileBytes { get; set; } = 274877906944L;


	public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60.0);


	public event Action<string>? Log;

	public AgentServer(string root, byte[] psk, IPAddress bind, int port, string certPfxPath)
	{
		_root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
		Directory.CreateDirectory(_root);
		_psk = psk;
		_bind = bind;
		Port = port;
		_cert = LoadOrCreateCert(certPfxPath);
		_certHash = SHA256.HashData(_cert.RawData);
	}

	internal static X509Certificate2 LoadOrCreateCert(string pfx)
	{
		if (File.Exists(pfx))
		{
			return X509CertificateLoader.LoadPkcs12FromFile(pfx, (string)null, X509KeyStorageFlags.Exportable);
		}
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		using X509Certificate2 x509Certificate = new CertificateRequest("CN=CopyForge receiver", key, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1.0), DateTimeOffset.UtcNow.AddYears(5));
		byte[] array = x509Certificate.Export(X509ContentType.Pfx);
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(pfx));
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllBytes(pfx, array);
		PrivateFile.Restrict(pfx);
		return X509CertificateLoader.LoadPkcs12(array, (string?)null, X509KeyStorageFlags.Exportable);
	}

	private void Say(string m)
	{
		this.Log?.Invoke(m);
	}

	public async Task RunAsync(CancellationToken ct)
	{
		_listener = new TcpListener(_bind, Port);
		_listener.Start(16);
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		Say($"Receiving into {_root} on {_bind}:{Port}. Certificate fingerprint: {Fingerprint}");
		using (ct.Register(delegate
		{
			try
			{
				_listener.Stop();
			}
			catch
			{
			}
		}))
		{
			ConcurrentDictionary<Task, byte> tasks = new ConcurrentDictionary<Task, byte>();
			try
			{
				while (!ct.IsCancellationRequested)
				{
					TcpClient c;
					try
					{
						c = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (Exception) when (ct.IsCancellationRequested)
					{
						break;
					}
					string ip = (c.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
					if (_bad.TryGetValue(ip, out (int, DateTime) value) && value.Item1 >= 5 && DateTime.UtcNow < value.Item2)
					{
						c.Dispose();
						continue;
					}
					if (_perIp.AddOrUpdate(ip, 1, (string _, int n) => n + 1) > MaxPerAddress)
					{
						_perIp.AddOrUpdate(ip, 0, (string _, int n) => n - 1);
						c.Dispose();
						continue;
					}
					if (Interlocked.Increment(ref _active) > MaxConnections)
					{
						Interlocked.Decrement(ref _active);
						_perIp.AddOrUpdate(ip, 0, (string _, int n) => n - 1);
						c.Dispose();
						continue;
					}
					Task task = Task.Run(async delegate
					{
						try
						{
							await HandleAsync(c, ip, ct).ConfigureAwait(continueOnCapturedContext: false);
						}
						catch (OperationCanceledException)
						{
						}
						catch (Exception ex3)
						{
							Say("[" + ip + "] connection ended: " + ex3.Message);
						}
						finally
						{
							Interlocked.Decrement(ref _active);
							_perIp.AddOrUpdate(ip, 0, (string _, int n) => n - 1);
							c.Dispose();
						}
					});
					tasks[task] = 0;
					task.ContinueWith((Task x) => tasks.TryRemove(x, out var _));
				}
			}
			finally
			{
				try
				{
					_listener.Stop();
				}
				catch
				{
				}
			}
			try
			{
				await Task.WhenAll(tasks.Keys).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
	}

	private async Task HandleAsync(TcpClient c, string ip, CancellationToken ct)
	{
		c.NoDelay = true;
		using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
		idle.CancelAfter(TimeSpan.FromSeconds(15.0));
		SslStream ssl = new SslStream(c.GetStream(), leaveInnerStreamOpen: false);
		try
		{
			await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
			{
				ServerCertificate = _cert,
				ClientCertificateRequired = false,
				EnabledSslProtocols = (SslProtocols.Tls12 | SslProtocols.Tls13)
			}, idle.Token).ConfigureAwait(continueOnCapturedContext: false);
			byte[] hello = new byte[4];
			await Wire.ReadExactAsync(ssl, hello, idle.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!hello.AsSpan().SequenceEqual(Wire.Magic))
			{
				throw new InvalidDataException("not a compatible CopyForge client (needs CopyForge 3.3.11 or newer)");
			}
			byte[] nonce = RandomNumberGenerator.GetBytes(32);
			await ssl.WriteAsync(nonce, idle.Token).ConfigureAwait(continueOnCapturedContext: false);
			byte[] resp = new byte[32];
			await Wire.ReadExactAsync(ssl, resp, idle.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!CryptographicOperations.FixedTimeEquals(resp, Wire.Mac(_psk, nonce, _certHash)))
			{
				(int, DateTime) orAdd = _bad.GetOrAdd(ip, (string _) => (fails: 0, until: DateTime.MinValue));
				_bad[ip] = (orAdd.Item1 + 1, DateTime.UtcNow.AddMinutes(1.0));
				Say("[" + ip + "] login refused (wrong secret)");
				await Task.Delay(1000, ct).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			_bad.TryRemove(ip, out (int, DateTime) _);
			await ssl.WriteAsync(new byte[1] { 65 }, idle.Token).ConfigureAwait(continueOnCapturedContext: false);
			Wire.EnableEncryption(ssl, _psk, nonce, _certHash, isClient: false);
			Say("[" + ip + "] connected");
			while (true)
			{
				using (CancellationTokenSource step = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					step.CancelAfter(IdleTimeout);
					var (b, p) = await Wire.ReadFrameAsync(ssl, step.Token).ConfigureAwait(continueOnCapturedContext: false);
					switch (b)
					{
					case 81:
						break;
					case 77:
						await HandleManifestAsync(ssl, p, ct).ConfigureAwait(continueOnCapturedContext: false);
						continue;
					default:
					{
						ReadOnlySpan<char> readOnlySpan = "unexpected message ";
						char reference = (char)b;
						throw new InvalidDataException(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)));
					}
					}
				}
				break;
			}
		}
		finally
		{
			if (ssl != null)
			{
				await ssl.DisposeAsync();
			}
		}
	}

	private async Task HandleManifestAsync(SslStream ssl, byte[] p, CancellationToken ct)
	{
		int num = 0;
		int num2 = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(num));
		num += 4;
		if (num2 < 0 || num2 > 4000)
		{
			throw new InvalidDataException("bad batch size");
		}
		string?[] rels = new string[num2];
		long[] sizes = new long[num2];
		long[] times = new long[num2];
		List<int> need = new List<int>();
		for (int i = 0; i < num2; i++)
		{
			int num3 = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(num));
			num += 2;
			if (num3 > 4096 || num + num3 + 16 > p.Length)
			{
				throw new InvalidDataException("bad entry");
			}
			rels[i] = Wire.SafeRel(Encoding.UTF8.GetString(p, num, num3));
			num += num3;
			sizes[i] = BinaryPrimitives.ReadInt64BigEndian(p.AsSpan(num));
			num += 8;
			times[i] = BinaryPrimitives.ReadInt64BigEndian(p.AsSpan(num));
			num += 8;
			if (rels[i] == null || sizes[i] < 0 || sizes[i] > MaxFileBytes)
			{
				rels[i] = null;
				continue;
			}
			string text = ResolveSafe(rels[i]);
			if (text == null)
			{
				rels[i] = null;
				continue;
			}
			FileInfo fileInfo = new FileInfo(text);
			if (!fileInfo.Exists || fileInfo.Length != sizes[i] || !(Math.Abs((double)(fileInfo.LastWriteTimeUtc.Ticks - times[i]) / 10000000.0) <= 2.0))
			{
				need.Add(i);
			}
		}
		byte[] array = new byte[4 + need.Count * 4];
		BinaryPrimitives.WriteInt32BigEndian(array, need.Count);
		for (int j = 0; j < need.Count; j++)
		{
			BinaryPrimitives.WriteInt32BigEndian(array.AsSpan(4 + j * 4), need[j]);
		}
		await Wire.WriteFrameAsync(ssl, 78, array, ct).ConfigureAwait(continueOnCapturedContext: false);
		foreach (int item in need)
		{
			string full = ResolveSafe(rels[item]);
			string text2;
			try
			{
				text2 = await ReceiveFileAsync(ssl, full, sizes[item], times[item], ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (InvalidDataException)
			{
				throw;
			}
			await Wire.WriteFrameAsync(ssl, (byte)((text2 == "") ? 75 : 88), Encoding.UTF8.GetBytes(text2), ct).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private string? ResolveSafe(string rel)
	{
		string fullPath = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
		ReadOnlySpan<char> readOnlySpan = _root;
		char reference = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.Ordinal))
		{
			return null;
		}
		string text = _root;
		string[] array = rel.Split('/');
		foreach (string path in array)
		{
			text = Path.Combine(text, path);
			try
			{
				if ((new FileInfo(text).Exists || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
				{
					return null;
				}
			}
			catch
			{
				return null;
			}
		}
		return fullPath;
	}

	private async Task<string> ReceiveFileAsync(SslStream ssl, string full, long size, long mtimeTicks, CancellationToken ct)
	{
		string tmp = full + ".cfpart";
		string err = null;
		FileStream fs = null;
		string result;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			if (ResolveSafe(Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/')) == null)
			{
				err = "path is not allowed";
			}
			else
			{
				try
				{
					fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
				}
				catch (Exception ex)
				{
					err = "cannot create file: " + ex.Message;
				}
			}
			using SHA256 sha = SHA256.Create();
			long got = 0L;
			while (true)
			{
				using (CancellationTokenSource step = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					step.CancelAfter(IdleTimeout);
					var (b, array) = await Wire.ReadFrameAsync(ssl, step.Token).ConfigureAwait(continueOnCapturedContext: false);
					switch (b)
					{
					case 68:
						if (array.Length > 1048576)
						{
							throw new InvalidDataException("chunk too large");
						}
						got += array.Length;
						if (got > size)
						{
							throw new InvalidDataException("sender sent more than it announced");
						}
						sha.TransformBlock(array, 0, array.Length, null, 0);
						if (fs != null && err == null)
						{
							try
							{
								await fs.WriteAsync(array, ct).ConfigureAwait(continueOnCapturedContext: false);
							}
							catch (IOException ex2)
							{
								err = "write failed: " + ex2.Message;
							}
						}
						continue;
					case 69:
						if (array.Length != 32)
						{
							throw new InvalidDataException("bad trailer");
						}
						sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
						if (got != size && err == null)
						{
							err = $"size mismatch ({got} of {size} bytes)";
						}
						if (err == null && !CryptographicOperations.FixedTimeEquals(sha.Hash, array))
						{
							err = "checksum mismatch - file not stored";
						}
						if (fs != null)
						{
							if (err == null)
							{
								await fs.FlushAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
							}
							await fs.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
							fs = null;
						}
						if (err == null)
						{
							File.SetLastWriteTimeUtc(tmp, new DateTime(Math.Clamp(mtimeTicks, DateTime.MinValue.Ticks + 1, DateTime.MaxValue.Ticks - 1), DateTimeKind.Utc));
							File.Move(tmp, full, overwrite: true);
						}
						result = err ?? "";
						break;
					default:
						throw new InvalidDataException("unexpected message in file transfer");
					}
				}
				break;
			}
		}
		finally
		{
			if (fs != null)
			{
				await fs.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			if (err != null || File.Exists(tmp))
			{
				try
				{
					File.Delete(tmp);
				}
				catch
				{
				}
			}
		}
		return result;
	}
}
