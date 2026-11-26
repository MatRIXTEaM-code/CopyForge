using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine.Net;

/// <summary>
/// Receive-only LocalSend (protocol v2) endpoint, so phones and PCs running the LocalSend app can send files to a folder.
/// Nothing is stored unless the person at this PC accepts the transfer (or a PIN plus auto-accept was chosen). Files never
/// overwrite existing ones, names are cleaned, and only the sender that was accepted may upload.
/// </summary>
public sealed class LocalSendReceiver
{
	private sealed class Item
	{
		public string Id = "";
		public string Rel = "";
		public string Final = "";
		public long Size;
		public string? Sha256;
		public string Token = "";
		public DateTime? Modified;
		public bool Done;
	}

	private sealed class Session
	{
		public string Id = "";
		public IPAddress Ip = IPAddress.None;
		public Dictionary<string, Item> Items = new Dictionary<string, Item>();
		public DateTime Last = DateTime.UtcNow;
	}

	private const int MaxFiles = 10000;

	private readonly string _root;

	private readonly string _alias;

	private readonly string _pin;

	private readonly Func<string, string, int, long, bool> _accept;

	private readonly IPAddress _bind;

	private readonly bool _discovery;

	private readonly X509Certificate2 _cert;

	private readonly object _lock = new object();

	private Session? _session;

	private readonly ConcurrentDictionary<string, int> _badPins = new ConcurrentDictionary<string, int>();

	private TcpListener? _listener;

	public int Port { get; private set; }

	public string Fingerprint { get; }

	public event Action<string>? Log;

	public LocalSendReceiver(string root, string alias, string pin, Func<string, string, int, long, bool> accept, IPAddress bind, int port, string certPfx, bool discovery)
	{
		_root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
		Directory.CreateDirectory(_root);
		_alias = alias;
		_pin = pin;
		_accept = accept;
		_bind = bind;
		Port = port;
		_discovery = discovery;
		_cert = AgentServer.LoadOrCreateCert(certPfx);
		Fingerprint = Wire.Hex(SHA256.HashData(_cert.RawData)).ToLowerInvariant();
	}

	private void Say(string m)
	{
		this.Log?.Invoke(m);
	}

	public void Start()
	{
		_listener = new TcpListener(_bind, Port);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
	}

	public async Task RunAsync(CancellationToken ct)
	{
		if (_listener == null)
		{
			Start();
		}
		if (_discovery)
		{
			_ = Task.Run(() => DiscoveryAsync(ct), ct);
		}
		using (ct.Register(delegate
		{
			try
			{
				_listener!.Stop();
			}
			catch
			{
			}
		}))
		{
			while (!ct.IsCancellationRequested)
			{
				TcpClient c;
				try
				{
					c = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
				}
				catch
				{
					break;
				}
				_ = Task.Run(() => HandleAsync(c, ct), ct);
			}
		}
	}

	private string InfoJson(bool announce, bool withAnnounce)
	{
		Dictionary<string, object> d = new Dictionary<string, object>
		{
			["alias"] = _alias,
			["version"] = "2.0",
			["deviceModel"] = "Windows",
			["deviceType"] = "desktop",
			["fingerprint"] = Fingerprint,
			["port"] = Port,
			["protocol"] = "https",
			["download"] = false
		};
		if (withAnnounce)
		{
			d["announce"] = announce;
		}
		return JsonSerializer.Serialize(d);
	}

	private async Task DiscoveryAsync(CancellationToken ct)
	{
		try
		{
			using UdpClient udp = new UdpClient(AddressFamily.InterNetwork);
			udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, 53317));
			udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.167"));
			byte[] hello = Encoding.UTF8.GetBytes(InfoJson(announce: true, withAnnounce: true));
			await udp.SendAsync(hello, hello.Length, new IPEndPoint(IPAddress.Parse("224.0.0.167"), 53317)).ConfigureAwait(false);
			Say("Visible to LocalSend devices on this network as '" + _alias + "'.");
			using HttpClient http = new HttpClient(new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (HttpRequestMessage _, X509Certificate2? _, X509Chain? _, SslPolicyErrors _) => true
			})
			{
				Timeout = TimeSpan.FromSeconds(5)
			};
			while (!ct.IsCancellationRequested)
			{
				UdpReceiveResult r = await udp.ReceiveAsync(ct).ConfigureAwait(false);
				try
				{
					using JsonDocument doc = JsonDocument.Parse(r.Buffer);
					JsonElement e = doc.RootElement;
					string fp = e.TryGetProperty("fingerprint", out JsonElement f) ? (f.GetString() ?? "") : "";
					bool ann = e.TryGetProperty("announce", out JsonElement a) && a.ValueKind == JsonValueKind.True;
					if (!ann || fp == Fingerprint)
					{
						continue;
					}
					int port = e.TryGetProperty("port", out JsonElement p) && p.TryGetInt32(out int pi) ? pi : 53317;
					string proto = e.TryGetProperty("protocol", out JsonElement pr) ? (pr.GetString() ?? "https") : "https";
					if (proto != "http" && proto != "https")
					{
						continue;
					}
					string url = proto + "://" + r.RemoteEndPoint.Address + ":" + port + "/api/localsend/v2/register";
					using StringContent body = new StringContent(InfoJson(announce: false, withAnnounce: false), Encoding.UTF8, "application/json");
					await http.PostAsync(url, body, ct).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Say("Automatic discovery is off (" + ex.Message.TrimEnd('.') + "). Senders can still type this PC's address.");
		}
	}

	// ---------- minimal HTTP/1.1 over TLS ----------

	private sealed class Req
	{
		public string Method = "";
		public string Path = "";
		public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.Ordinal);
		public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class Conn
	{
		public Stream S = Stream.Null;
		public byte[] Buf = new byte[65536];
		public int Pos;
		public int Len;
	}

	private static async Task<int> FillAsync(Conn c, CancellationToken ct)
	{
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(60));
		if (c.Pos == c.Len)
		{
			c.Pos = 0;
			c.Len = 0;
		}
		int n = await c.S.ReadAsync(c.Buf.AsMemory(c.Len), cts.Token).ConfigureAwait(false);
		c.Len += n;
		return n;
	}

	private static async Task<Req?> ReadHeadAsync(Conn c, CancellationToken ct)
	{
		int scanned = 0;
		while (true)
		{
			int end = -1;
			for (int i = Math.Max(c.Pos, c.Pos + scanned - 3); i + 3 < c.Len; i++)
			{
				if (c.Buf[i] == 13 && c.Buf[i + 1] == 10 && c.Buf[i + 2] == 13 && c.Buf[i + 3] == 10)
				{
					end = i;
					break;
				}
			}
			if (end >= 0)
			{
				string head = Encoding.ASCII.GetString(c.Buf, c.Pos, end - c.Pos);
				c.Pos = end + 4;
				return ParseHead(head);
			}
			scanned = c.Len - c.Pos;
			if (c.Len - c.Pos > 16384 || c.Len == c.Buf.Length && c.Pos == 0)
			{
				return null;
			}
			if (c.Pos > 0 && c.Len == c.Buf.Length)
			{
				Buffer.BlockCopy(c.Buf, c.Pos, c.Buf, 0, c.Len - c.Pos);
				c.Len -= c.Pos;
				c.Pos = 0;
			}
			if (await FillAsync(c, ct).ConfigureAwait(false) == 0)
			{
				return null;
			}
		}
	}

	private static Req? ParseHead(string head)
	{
		string[] lines = head.Split("\r\n");
		string[] rl = lines[0].Split(' ');
		if (rl.Length < 3)
		{
			return null;
		}
		Req r = new Req { Method = rl[0].ToUpperInvariant() };
		string target = rl[1];
		int q = target.IndexOf('?');
		r.Path = (q < 0) ? target : target.Substring(0, q);
		if (q >= 0)
		{
			foreach (string kv in target.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
			{
				int eq = kv.IndexOf('=');
				string k = Uri.UnescapeDataString((eq < 0) ? kv : kv.Substring(0, eq));
				string v = (eq < 0) ? "" : Uri.UnescapeDataString(kv.Substring(eq + 1).Replace('+', ' '));
				r.Query[k] = v;
			}
		}
		for (int i = 1; i < lines.Length; i++)
		{
			int colon = lines[i].IndexOf(':');
			if (colon > 0)
			{
				r.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
			}
		}
		return r;
	}

	/// <summary>Streams the request body to sink; returns false when it is longer than limit or malformed.</summary>
	private static async Task<bool> ReadBodyAsync(Conn c, Req r, long limit, Func<ReadOnlyMemory<byte>, Task> sink, CancellationToken ct)
	{
		long total = 0;
		if (r.Headers.TryGetValue("Transfer-Encoding", out string? te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
		{
			while (true)
			{
				string line = await ReadLineAsync(c, ct).ConfigureAwait(false);
				if (line.Length == 0 || line.Length > 20)
				{
					return false;
				}
				int sc = line.IndexOf(';');
				if (!long.TryParse(sc < 0 ? line : line.Substring(0, sc), System.Globalization.NumberStyles.HexNumber, null, out long size) || size < 0)
				{
					return false;
				}
				if (size == 0)
				{
					while ((await ReadLineAsync(c, ct).ConfigureAwait(false)).Length > 0)
					{
					}
					return true;
				}
				total += size;
				if (total > limit)
				{
					return false;
				}
				if (!await CopyAsync(c, size, sink, ct).ConfigureAwait(false) || (await ReadLineAsync(c, ct).ConfigureAwait(false)).Length != 0)
				{
					return false;
				}
			}
		}
		long len = 0;
		if (r.Headers.TryGetValue("Content-Length", out string? cl) && !long.TryParse(cl, out len))
		{
			return false;
		}
		if (len < 0 || len > limit)
		{
			return false;
		}
		return await CopyAsync(c, len, sink, ct).ConfigureAwait(false);
	}

	private static async Task<bool> CopyAsync(Conn c, long n, Func<ReadOnlyMemory<byte>, Task> sink, CancellationToken ct)
	{
		while (n > 0)
		{
			if (c.Pos == c.Len && await FillAsync(c, ct).ConfigureAwait(false) == 0)
			{
				return false;
			}
			int take = (int)Math.Min(n, c.Len - c.Pos);
			await sink(new ReadOnlyMemory<byte>(c.Buf, c.Pos, take)).ConfigureAwait(false);
			c.Pos += take;
			n -= take;
		}
		return true;
	}

	private static async Task<string> ReadLineAsync(Conn c, CancellationToken ct)
	{
		StringBuilder sb = new StringBuilder();
		while (true)
		{
			if (c.Pos == c.Len && await FillAsync(c, ct).ConfigureAwait(false) == 0)
			{
				return "";
			}
			byte b = c.Buf[c.Pos++];
			if (b == 10)
			{
				return sb.ToString().TrimEnd('\r');
			}
			sb.Append((char)b);
			if (sb.Length > 64)
			{
				return "";
			}
		}
	}

	private static async Task RespondAsync(Stream s, int code, string text, string? json, CancellationToken ct)
	{
		byte[] body = (json == null) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json);
		string head = $"HTTP/1.1 {code} {text}\r\n" + ((json != null) ? "Content-Type: application/json\r\n" : "") + $"Content-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n";
		await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
		if (body.Length > 0)
		{
			await s.WriteAsync(body, ct).ConfigureAwait(false);
		}
		await s.FlushAsync(ct).ConfigureAwait(false);
	}

	private async Task HandleAsync(TcpClient tcp, CancellationToken ct)
	{
		using (tcp)
		{
			try
			{
				IPAddress ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
				using SslStream ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
				using (CancellationTokenSource hs = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					hs.CancelAfter(TimeSpan.FromSeconds(15));
					await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
					{
						ServerCertificate = _cert,
						ClientCertificateRequired = false,
						EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
					}, hs.Token).ConfigureAwait(false);
				}
				Conn c = new Conn { S = ssl };
				for (int n = 0; n < 100000 && !ct.IsCancellationRequested; n++)
				{
					Req? r = await ReadHeadAsync(c, ct).ConfigureAwait(false);
					if (r == null)
					{
						return;
					}
					if (!await RouteAsync(c, r, ip, ct).ConfigureAwait(false))
					{
						return;
					}
				}
			}
			catch
			{
			}
		}
	}

	private async Task<bool> DrainJsonAsync(Conn c, Req r, long limit, Action<byte[]> got, CancellationToken ct)
	{
		using MemoryStream ms = new MemoryStream();
		bool ok = await ReadBodyAsync(c, r, limit, delegate(ReadOnlyMemory<byte> m)
		{
			ms.Write(m.Span);
			return Task.CompletedTask;
		}, ct).ConfigureAwait(false);
		if (ok)
		{
			got(ms.ToArray());
		}
		return ok;
	}

	private async Task<bool> RouteAsync(Conn c, Req r, IPAddress ip, CancellationToken ct)
	{
		Stream s = c.S;
		const string P = "/api/localsend/v2/";
		if (!r.Path.StartsWith(P, StringComparison.Ordinal))
		{
			await RespondAsync(s, 404, "Not Found", null, ct).ConfigureAwait(false);
			return false;
		}
		string ep = r.Path.Substring(P.Length);
		if (r.Method == "GET" && ep == "info")
		{
			await RespondAsync(s, 200, "OK", InfoJson(announce: false, withAnnounce: false), ct).ConfigureAwait(false);
			return true;
		}
		if (r.Method != "POST")
		{
			await RespondAsync(s, 405, "Method Not Allowed", null, ct).ConfigureAwait(false);
			return false;
		}
		switch (ep)
		{
		case "register":
		{
			if (!await DrainJsonAsync(c, r, 65536, _ => { }, ct).ConfigureAwait(false))
			{
				return false;
			}
			await RespondAsync(s, 200, "OK", InfoJson(announce: false, withAnnounce: false), ct).ConfigureAwait(false);
			return true;
		}
		case "prepare-upload":
		{
			byte[] body = Array.Empty<byte>();
			if (!await DrainJsonAsync(c, r, 8 * 1024 * 1024, b => body = b, ct).ConfigureAwait(false))
			{
				await RespondAsync(s, 400, "Bad Request", null, ct).ConfigureAwait(false);
				return false;
			}
			(int code, string text, string? json) = await Task.Run(() => Prepare(r, ip, body), ct).ConfigureAwait(false);
			await RespondAsync(s, code, text, json, ct).ConfigureAwait(false);
			return true;
		}
		case "upload":
			return await UploadAsync(c, r, ip, ct).ConfigureAwait(false);
		case "cancel":
		{
			if (!await DrainJsonAsync(c, r, 65536, _ => { }, ct).ConfigureAwait(false))
			{
				return false;
			}
			lock (_lock)
			{
				if (_session != null && r.Query.TryGetValue("sessionId", out string? sid) && sid == _session.Id && ip.Equals(_session.Ip))
				{
					DropSession("The sender cancelled the transfer.");
				}
			}
			await RespondAsync(s, 200, "OK", null, ct).ConfigureAwait(false);
			return true;
		}
		}
		await RespondAsync(s, 404, "Not Found", null, ct).ConfigureAwait(false);
		return false;
	}

	private void DropSession(string why)
	{
		Session? s = _session;
		_session = null;
		if (s == null)
		{
			return;
		}
		foreach (Item it in s.Items.Values)
		{
			if (!it.Done)
			{
				try
				{
					File.Delete(it.Final + ".cfpart");
				}
				catch
				{
				}
			}
		}
		Say(why);
	}

	/// <summary>Cleans one file name from the sender so it is a safe relative path inside the receive folder.</summary>
	public static string? CleanName(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw) || raw.Length > 1000)
		{
			return null;
		}
		List<string> parts = new List<string>();
		foreach (string part0 in raw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			StringBuilder sb = new StringBuilder();
			foreach (char ch in part0)
			{
				sb.Append((ch < ' ' || "<>:\"|?*".IndexOf(ch) >= 0) ? '_' : ch);
			}
			string p = sb.ToString().TrimEnd(' ', '.');
			if (p.Length == 0 || p == "." || p == "..")
			{
				p = "_";
			}
			if (p.Length > 200)
			{
				p = p.Substring(0, 200);
			}
			string stem = p.Split('.')[0].ToUpperInvariant();
			if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])) || p.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase))
			{
				p = "_" + p;
			}
			parts.Add(p);
		}
		if (parts.Count == 0 || parts.Count > 20)
		{
			return null;
		}
		return Wire.SafeRel(string.Join('/', parts));
	}

	private (int, string, string?) Prepare(Req r, IPAddress ip, byte[] body)
	{
		string ipKey = ip.ToString();
		if (_pin.Length > 0)
		{
			r.Query.TryGetValue("pin", out string? given);
			if (_badPins.GetValueOrDefault(ipKey) >= 5)
			{
				return (429, "Too Many Requests", null);
			}
			byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(given ?? ""));
			byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(_pin));
			if (!CryptographicOperations.FixedTimeEquals(a, b))
			{
				_badPins.AddOrUpdate(ipKey, 1, (_, v) => v + 1);
				return (401, "Unauthorized", null);
			}
		}
		string senderAlias = "unknown";
		List<Item> items = new List<Item>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("info", out JsonElement info) && info.TryGetProperty("alias", out JsonElement al))
			{
				senderAlias = new string((al.GetString() ?? "unknown").Where(ch => ch >= ' ').Take(60).ToArray());
			}
			if (!root.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Object)
			{
				return (400, "Bad Request", null);
			}
			HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty fp in files.EnumerateObject())
			{
				if (items.Count >= MaxFiles)
				{
					return (400, "Bad Request", null);
				}
				JsonElement f = fp.Value;
				string name = f.TryGetProperty("fileName", out JsonElement fn) ? (fn.GetString() ?? "") : "";
				long size = f.TryGetProperty("size", out JsonElement sz) && sz.TryGetInt64(out long sv) ? sv : -1L;
				string? rel = CleanName(name);
				if (rel == null || size < 0 || fp.Name.Length == 0 || fp.Name.Length > 200)
				{
					return (400, "Bad Request", null);
				}
				string sha = (f.TryGetProperty("sha256", out JsonElement sh) && sh.ValueKind == JsonValueKind.String) ? (sh.GetString() ?? "") : "";
				DateTime? mod = null;
				if (f.TryGetProperty("metadata", out JsonElement md) && md.ValueKind == JsonValueKind.Object && md.TryGetProperty("modified", out JsonElement mo) && mo.ValueKind == JsonValueKind.String && DateTime.TryParse(mo.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime md2) && md2.Year >= 1980)
				{
					mod = md2;
				}
				string final = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
				if (!final.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					return (400, "Bad Request", null);
				}
				string stem = Path.Combine(Path.GetDirectoryName(final)!, Path.GetFileNameWithoutExtension(final));
				string ext = Path.GetExtension(final);
				for (int n = 1; File.Exists(final) || Directory.Exists(final) || File.Exists(final + ".cfpart") || !used.Add(final); n++)
				{
					final = stem + " (" + n + ")" + ext;
				}
				items.Add(new Item { Id = fp.Name, Rel = rel, Final = final, Size = size, Sha256 = (sha.Length > 0) ? sha.ToLowerInvariant() : null, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), Modified = mod });
			}
		}
		catch (JsonException)
		{
			return (400, "Bad Request", null);
		}
		if (items.Count == 0)
		{
			return (400, "Bad Request", null);
		}
		long total = 0L;
		foreach (Item it in items)
		{
			total += it.Size;
		}
		try
		{
			long free = new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace;
			if (total > free - 64L * 1024 * 1024)
			{
				Say($"Refused {senderAlias} ({ip}): {total:N0} bytes will not fit on this drive.");
				return (403, "Forbidden", null);
			}
		}
		catch
		{
		}
		lock (_lock)
		{
			if (_session != null && DateTime.UtcNow - _session.Last < TimeSpan.FromMinutes(10))
			{
				return (409, "Conflict", null);
			}
			if (_session != null)
			{
				DropSession("Previous transfer timed out.");
			}
			_session = new Session { Id = Guid.NewGuid().ToString("N"), Ip = ip };
		}
		bool ok;
		try
		{
			ok = _accept(senderAlias, ipKey, items.Count, total);
		}
		catch
		{
			ok = false;
		}
		lock (_lock)
		{
			if (!ok)
			{
				_session = null;
				Say($"Declined {items.Count:N0} file(s) from {senderAlias} ({ip}).");
				return (403, "Forbidden", null);
			}
			foreach (Item it2 in items)
			{
				_session!.Items[it2.Id] = it2;
			}
			_session!.Last = DateTime.UtcNow;
			Say($"Accepted {items.Count:N0} file(s), {total:N0} bytes, from {senderAlias} ({ip}).");
			Dictionary<string, string> tokens = new Dictionary<string, string>();
			foreach (Item it3 in items)
			{
				tokens[it3.Id] = it3.Token;
			}
			return (200, "OK", JsonSerializer.Serialize(new Dictionary<string, object> { ["sessionId"] = _session.Id, ["files"] = tokens }));
		}
	}

	private async Task<bool> UploadAsync(Conn c, Req r, IPAddress ip, CancellationToken ct)
	{
		Stream s = c.S;
		Item? it = null;
		lock (_lock)
		{
			if (_session != null && r.Query.TryGetValue("sessionId", out string? sid) && r.Query.TryGetValue("fileId", out string? fid) && r.Query.TryGetValue("token", out string? tok) && sid == _session.Id && ip.Equals(_session.Ip) && _session.Items.TryGetValue(fid, out Item? found) && found.Token == tok && !found.Done)
			{
				it = found;
				_session.Last = DateTime.UtcNow;
			}
		}
		if (it == null)
		{
			await RespondAsync(s, 403, "Forbidden", null, ct).ConfigureAwait(false);
			return false;
		}
		string part = it.Final + ".cfpart";
		Directory.CreateDirectory(Path.GetDirectoryName(it.Final)!);
		long written = 0L;
		bool ok;
		using (SHA256 sha = SHA256.Create())
		{
			using (FileStream fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
			{
				ok = await ReadBodyAsync(c, r, it.Size, async delegate(ReadOnlyMemory<byte> m)
				{
					sha.TransformBlock(m.ToArray(), 0, m.Length, null, 0);
					await fs.WriteAsync(m, ct).ConfigureAwait(false);
					written += m.Length;
				}, ct).ConfigureAwait(false);
			}
			sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
			if (!ok || written != it.Size)
			{
				try
				{
					File.Delete(part);
				}
				catch
				{
				}
				await RespondAsync(s, 400, "Bad Request", null, ct).ConfigureAwait(false);
				return false;
			}
			if (it.Sha256 != null && !string.Equals(Convert.ToHexString(sha.Hash!), it.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					File.Delete(part);
				}
				catch
				{
				}
				Say("Checksum mismatch, discarded: " + it.Rel);
				await RespondAsync(s, 422, "Unprocessable Entity", null, ct).ConfigureAwait(false);
				return true;
			}
		}
		File.Move(part, it.Final, overwrite: false);
		if (it.Modified.HasValue)
		{
			try
			{
				File.SetLastWriteTimeUtc(it.Final, it.Modified.Value);
			}
			catch
			{
			}
		}
		lock (_lock)
		{
			it.Done = true;
			Say("Received " + it.Rel + " (" + it.Size.ToString("N0") + " bytes)");
			if (_session != null && _session.Items.Values.All(x => x.Done))
			{
				_session = null;
				Say("Transfer complete.");
			}
		}
		await RespondAsync(s, 200, "OK", null, ct).ConfigureAwait(false);
		return true;
	}
}
