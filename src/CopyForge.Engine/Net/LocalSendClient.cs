using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine.Net;

public sealed record LsPeer(string Alias, string Ip, int Port, string Protocol, string Fingerprint, string DeviceType);

public sealed record LsSendResult(bool Ok, string Message, int Files, long Bytes);

/// <summary>Sends files to a device running the LocalSend app (protocol v2).</summary>
public static class LocalSendClient
{
	/// <summary>Announces this PC on the multicast group and collects the devices that answer.</summary>
	public static async Task<List<LsPeer>> DiscoverAsync(TimeSpan wait, CancellationToken ct)
	{
		Dictionary<string, LsPeer> found = new Dictionary<string, LsPeer>();
		string myFp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
		using UdpClient udp = new UdpClient(AddressFamily.InterNetwork);
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
		udp.Client.Bind(new IPEndPoint(IPAddress.Any, 53317));
		udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.167"));
		string hello = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["alias"] = "CopyForge " + Environment.MachineName,
			["version"] = "2.0",
			["deviceModel"] = "Windows",
			["deviceType"] = "desktop",
			["fingerprint"] = myFp,
			["port"] = 53317,
			["protocol"] = "https",
			["download"] = false,
			["announce"] = true
		});
		byte[] bytes = Encoding.UTF8.GetBytes(hello);
		IPEndPoint group = new IPEndPoint(IPAddress.Parse("224.0.0.167"), 53317);
		await udp.SendAsync(bytes, bytes.Length, group).ConfigureAwait(false);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(wait);
		int resend = 0;
		try
		{
			while (!cts.IsCancellationRequested)
			{
				UdpReceiveResult r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
				try
				{
					using JsonDocument doc = JsonDocument.Parse(r.Buffer);
					JsonElement e = doc.RootElement;
					string fp = e.TryGetProperty("fingerprint", out JsonElement f) ? (f.GetString() ?? "") : "";
					if (fp.Length == 0 || fp == myFp)
					{
						continue;
					}
					string alias = e.TryGetProperty("alias", out JsonElement a) ? (a.GetString() ?? "?") : "?";
					int port = e.TryGetProperty("port", out JsonElement p) && p.TryGetInt32(out int pi) ? pi : 53317;
					string proto = e.TryGetProperty("protocol", out JsonElement pr) ? (pr.GetString() ?? "https") : "https";
					string type = e.TryGetProperty("deviceType", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? (dt.GetString() ?? "") : "";
					if (proto != "http" && proto != "https")
					{
						continue;
					}
					alias = new string(alias.Where(ch => ch >= ' ').Take(60).ToArray());
					found[fp] = new LsPeer(alias, r.RemoteEndPoint.Address.ToString(), port, proto, fp, type);
				}
				catch (JsonException)
				{
				}
				if (resend++ == 0)
				{
					await udp.SendAsync(bytes, bytes.Length, group).ConfigureAwait(false);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		return found.Values.OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static HttpClient MakeHttp(LsPeer peer, bool pin)
	{
		HttpClientHandler h = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = delegate(HttpRequestMessage _, X509Certificate2? cert, X509Chain? _, SslPolicyErrors _)
			{
				if (cert == null)
				{
					return false;
				}
				if (!pin || peer.Fingerprint.Length == 0)
				{
					return true;
				}
				return string.Equals(Convert.ToHexString(SHA256.HashData(cert.RawData)), peer.Fingerprint, StringComparison.OrdinalIgnoreCase);
			}
		};
		return new HttpClient(h) { Timeout = TimeSpan.FromHours(6) };
	}

	private sealed class Entry
	{
		public string Id = "";
		public string Path = "";
		public string Name = "";
		public long Size;
		public string Sha = "";
		public DateTime Mod;
	}

	private static List<Entry> Collect(IEnumerable<string> sources)
	{
		List<Entry> list = new List<Entry>();
		int n = 0;
		foreach (string raw in sources)
		{
			string src = raw.TrimEnd('\\', '/');
			if (Directory.Exists(src))
			{
				string top = System.IO.Path.GetFileName(src);
				foreach (string f in Directory.EnumerateFiles(src, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
				{
					string rel = System.IO.Path.GetRelativePath(src, f).Replace('\\', '/');
					list.Add(new Entry { Id = "f" + n++, Path = f, Name = top + "/" + rel });
				}
			}
			else if (File.Exists(src))
			{
				list.Add(new Entry { Id = "f" + n++, Path = src, Name = System.IO.Path.GetFileName(src) });
			}
		}
		foreach (Entry e in list)
		{
			FileInfo fi = new FileInfo(e.Path);
			e.Size = fi.Length;
			e.Mod = fi.LastWriteTimeUtc;
		}
		return list;
	}

	public static async Task<LsSendResult> SendAsync(LsPeer peer, string myAlias, string pin, IEnumerable<string> sources, Action<string>? log, Action<long, long>? progress, CancellationToken ct)
	{
		List<Entry> files = Collect(sources);
		if (files.Count == 0)
		{
			return new LsSendResult(false, "Nothing to send.", 0, 0L);
		}
		long total = files.Sum(x => x.Size);
		log?.Invoke($"Checking {files.Count:N0} file(s) ({total:N0} bytes)...");
		foreach (Entry e in files)
		{
			ct.ThrowIfCancellationRequested();
			using FileStream fs = new FileStream(e.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
			e.Sha = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
			e.Size = fs.Length;
		}
		string baseUrl = peer.Protocol + "://" + (peer.Ip.Contains(':') ? "[" + peer.Ip + "]" : peer.Ip) + ":" + peer.Port + "/api/localsend/v2/";
		using HttpClient http = MakeHttp(peer, peer.Protocol == "https");
		Dictionary<string, object> fmap = new Dictionary<string, object>();
		foreach (Entry e in files)
		{
			fmap[e.Id] = new Dictionary<string, object>
			{
				["id"] = e.Id,
				["fileName"] = e.Name,
				["size"] = e.Size,
				["fileType"] = "application/octet-stream",
				["sha256"] = e.Sha,
				["metadata"] = new Dictionary<string, object> { ["modified"] = e.Mod.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) }
			};
		}
		string body = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["info"] = new Dictionary<string, object>
			{
				["alias"] = myAlias,
				["version"] = "2.0",
				["deviceModel"] = "Windows",
				["deviceType"] = "desktop",
				["fingerprint"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
				["port"] = 53317,
				["protocol"] = "https",
				["download"] = false
			},
			["files"] = fmap
		});
		log?.Invoke("Waiting for " + peer.Alias + " to accept...");
		HttpResponseMessage resp;
		try
		{
			resp = await http.PostAsync(baseUrl + "prepare-upload" + ((pin.Length > 0) ? ("?pin=" + Uri.EscapeDataString(pin)) : ""), new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
		}
		catch (HttpRequestException ex)
		{
			return new LsSendResult(false, "Cannot reach " + peer.Alias + " (" + (ex.InnerException?.Message ?? ex.Message).TrimEnd('.') + "). A changed fingerprint also ends up here.", 0, 0L);
		}
		using (resp)
		{
			switch ((int)resp.StatusCode)
			{
			case 204:
				return new LsSendResult(true, peer.Alias + " needs nothing sent.", 0, 0L);
			case 401:
				return new LsSendResult(false, peer.Alias + " wants a PIN (missing or wrong).", 0, 0L);
			case 403:
				return new LsSendResult(false, peer.Alias + " declined the transfer.", 0, 0L);
			case 409:
				return new LsSendResult(false, peer.Alias + " is busy with another transfer.", 0, 0L);
			case 429:
				return new LsSendResult(false, "Too many tries; wait a while.", 0, 0L);
			}
			if (!resp.IsSuccessStatusCode)
			{
				return new LsSendResult(false, peer.Alias + " answered " + (int)resp.StatusCode + ".", 0, 0L);
			}
			using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
			string session = doc.RootElement.GetProperty("sessionId").GetString() ?? "";
			JsonElement tokens = doc.RootElement.GetProperty("files");
			long sent = 0L;
			int done = 0;
			foreach (Entry e in files)
			{
				ct.ThrowIfCancellationRequested();
				if (!tokens.TryGetProperty(e.Id, out JsonElement tk))
				{
					continue;
				}
				string url = baseUrl + "upload?sessionId=" + Uri.EscapeDataString(session) + "&fileId=" + Uri.EscapeDataString(e.Id) + "&token=" + Uri.EscapeDataString(tk.GetString() ?? "");
				using FileStream fs = new FileStream(e.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan | FileOptions.Asynchronous);
				long before = sent;
				using StreamContent sc = new StreamContent(new CountingStream(fs, n => progress?.Invoke(before + n, total)), 1 << 16);
				sc.Headers.ContentLength = e.Size;
				sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
				using HttpResponseMessage up = await http.PostAsync(url, sc, ct).ConfigureAwait(false);
				if (!up.IsSuccessStatusCode)
				{
					try
					{
						await http.PostAsync(baseUrl + "cancel?sessionId=" + Uri.EscapeDataString(session), new StringContent("{}"), CancellationToken.None).ConfigureAwait(false);
					}
					catch
					{
					}
					return new LsSendResult(false, $"{e.Name}: {peer.Alias} answered {(int)up.StatusCode} ({(up.StatusCode == (HttpStatusCode)422 ? "checksum did not match" : "refused")}).", done, sent);
				}
				sent += e.Size;
				done++;
				log?.Invoke("Sent " + e.Name);
			}
			return new LsSendResult(true, $"Sent {done:N0} file(s) to {peer.Alias}.", done, sent);
		}
	}

	private sealed class CountingStream : Stream
	{
		private readonly Stream _s;

		private readonly Action<long> _on;

		private long _n;

		public CountingStream(Stream s, Action<long> on)
		{
			_s = s;
			_on = on;
		}

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => _s.Length;

		public override long Position
		{
			get => _n;
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] b, int o, int c)
		{
			int r = _s.Read(b, o, c);
			_on(_n += r);
			return r;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> m, CancellationToken ct = default)
		{
			int r = await _s.ReadAsync(m, ct).ConfigureAwait(false);
			_on(_n += r);
			return r;
		}

		public override long Seek(long o, SeekOrigin so)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long v)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] b, int o, int c)
		{
			throw new NotSupportedException();
		}
	}
}
