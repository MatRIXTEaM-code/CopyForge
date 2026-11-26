using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine.Net;

public static class AgentClient
{
	[StructLayout(LayoutKind.Auto)]
	[CompilerGenerated]
	private struct _003C_003Ec__DisplayClass0_1
	{
		public MemoryStream ms;
	}

	public static async Task<SendResult> SendAsync(string host, int port, byte[] psk, string fingerprintHex, IEnumerable<string> sources, Action<string>? log, CancellationToken ct)
	{
		string want = fingerprintHex.Replace(":", "").Replace(" ", "").ToLowerInvariant();
		using TcpClient tcp = new TcpClient
		{
			NoDelay = true
		};
		await tcp.ConnectAsync(host, port, ct).ConfigureAwait(continueOnCapturedContext: false);
		byte[] seenHash = Array.Empty<byte>();
		SslStream ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, delegate(object s, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errs)
		{
			if (cert == null)
			{
				return false;
			}
			seenHash = SHA256.HashData(cert.GetRawCertData());
			return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Wire.Hex(seenHash)), Encoding.ASCII.GetBytes(want));
		});
		SendResult result;
		try
		{
			await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
			{
				TargetHost = host,
				EnabledSslProtocols = (SslProtocols.Tls12 | SslProtocols.Tls13)
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			await ssl.WriteAsync(Wire.Magic, ct).ConfigureAwait(continueOnCapturedContext: false);
			byte[] nonce = new byte[32];
			await Wire.ReadExactAsync(ssl, nonce, ct).ConfigureAwait(continueOnCapturedContext: false);
			await ssl.WriteAsync(Wire.Mac(psk, nonce, seenHash), ct).ConfigureAwait(continueOnCapturedContext: false);
			byte[] ack = new byte[1];
			try
			{
				await Wire.ReadExactAsync(ssl, ack, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
				throw new UnauthorizedAccessException("The receiver refused the login (wrong pre-shared secret?).");
			}
			if (ack[0] != 65)
			{
				throw new UnauthorizedAccessException("The receiver refused the login.");
			}
			Wire.EnableEncryption(ssl, psk, nonce, seenHash, isClient: true);
			log?.Invoke("Secure channel up: TLS plus a second AES-256 layer keyed from the shared secret." + ((psk.Length < 32) ? " (Note: the shared secret is under 32 bytes, so the extra layer is weaker against a future quantum computer.)" : ""));
			List<(string full, string rel)> files = new List<(string, string)>();
			foreach (string source in sources)
			{
				if (File.Exists(source))
				{
					files.Add((Path.GetFullPath(source), Path.GetFileName(source)));
					continue;
				}
				if (Directory.Exists(source))
				{
					string text = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
					string name = new DirectoryInfo(text).Name;
					foreach (FileInfo item in new DirectoryInfo(text).EnumerateFiles("*", new EnumerationOptions
					{
						RecurseSubdirectories = true,
						IgnoreInaccessible = true
					}))
					{
						if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
						{
							files.Add((item.FullName, name + "/" + Path.GetRelativePath(text, item.FullName).Replace(Path.DirectorySeparatorChar, '/')));
						}
					}
					continue;
				}
				throw new FileNotFoundException("Source not found: " + source);
			}
			int sent = 0;
			int failed = 0;
			long bytes = 0L;
			List<string> errors = new List<string>();
			_003C_003Ec__DisplayClass0_1 CS_0024_003C_003E8__locals1 = default(_003C_003Ec__DisplayClass0_1);
			for (int b = 0; b < files.Count; b += 4000)
			{
				List<(string full, string rel)> batch = files.Skip(b).Take(4000).ToList();
				CS_0024_003C_003E8__locals1.ms = new MemoryStream();
				try
				{
					List<FileInfo> infos = batch.Select(((string full, string rel) x) => new FileInfo(x.full)).ToList();
					BE32(batch.Count);
					for (int i = 0; i < batch.Count; i++)
					{
						byte[] bytes2 = Encoding.UTF8.GetBytes(batch[i].rel);
						byte[] array = new byte[2];
						BinaryPrimitives.WriteUInt16BigEndian(array, (ushort)bytes2.Length);
						CS_0024_003C_003E8__locals1.ms.Write(array);
						CS_0024_003C_003E8__locals1.ms.Write(bytes2);
						BE64(infos[i].Length);
						BE64(infos[i].LastWriteTimeUtc.Ticks);
					}
					await Wire.WriteFrameAsync(ssl, 77, CS_0024_003C_003E8__locals1.ms.ToArray(), ct).ConfigureAwait(continueOnCapturedContext: false);
					var (b2, array2) = await Wire.ReadFrameAsync(ssl, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (b2 != 78)
					{
						throw new InvalidDataException("unexpected reply");
					}
					int num = BinaryPrimitives.ReadInt32BigEndian(array2);
					List<int> list = new List<int>();
					for (int j = 0; j < num; j++)
					{
						list.Add(BinaryPrimitives.ReadInt32BigEndian(array2.AsSpan(4 + j * 4)));
					}
					foreach (int idx in list)
					{
						var (path, rel) = batch[idx];
						try
						{
							using SHA256 sha = SHA256.Create();
							FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous);
							try
							{
								byte[] buf = new byte[1048576];
								long total = 0L;
								int num2;
								while ((num2 = await fs.ReadAsync(buf, ct).ConfigureAwait(continueOnCapturedContext: false)) > 0)
								{
									total += num2;
									if (total > infos[idx].Length)
									{
										throw new IOException("file grew while sending");
									}
									sha.TransformBlock(buf, 0, num2, null, 0);
									await Wire.WriteFrameAsync(ssl, 68, buf.AsMemory(0, num2), ct).ConfigureAwait(continueOnCapturedContext: false);
								}
								sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
								await Wire.WriteFrameAsync(ssl, 69, sha.Hash, ct).ConfigureAwait(continueOnCapturedContext: false);
								var (b3, bytes3) = await Wire.ReadFrameAsync(ssl, ct).ConfigureAwait(continueOnCapturedContext: false);
								if (b3 == 75)
								{
									sent++;
									bytes += total;
									log?.Invoke("sent " + rel);
								}
								else
								{
									failed++;
									errors.Add(rel + ": " + Encoding.UTF8.GetString(bytes3));
								}
							}
							finally
							{
								if (fs != null)
								{
									await fs.DisposeAsync();
								}
							}
						}
						catch (IOException ex2) when (!(ex2 is EndOfStreamException) && ex2.GetType() == typeof(IOException))
						{
							failed++;
							errors.Add(rel + ": " + ex2.Message);
							throw;
						}
					}
				}
				finally
				{
					if (CS_0024_003C_003E8__locals1.ms != null)
					{
						((IDisposable)CS_0024_003C_003E8__locals1.ms).Dispose();
					}
				}
				CS_0024_003C_003E8__locals1 = default(_003C_003Ec__DisplayClass0_1);
				void BE32(int v)
				{
					byte[] array4 = new byte[4];
					BinaryPrimitives.WriteInt32BigEndian(array4, v);
					CS_0024_003C_003E8__locals1.ms.Write(array4);
				}
				void BE64(long v)
				{
					byte[] array3 = new byte[8];
					BinaryPrimitives.WriteInt64BigEndian(array3, v);
					CS_0024_003C_003E8__locals1.ms.Write(array3);
				}
			}
			await Wire.WriteFrameAsync(ssl, 81, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(continueOnCapturedContext: false);
			result = new SendResult(files.Count, sent, failed, bytes, errors);
		}
		finally
		{
			if (ssl != null)
			{
				await ssl.DisposeAsync();
			}
		}
		return result;
	}
}
