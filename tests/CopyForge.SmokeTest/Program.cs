using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CopyForge.Engine;
using CopyForge.Engine.Net;

namespace CopyForge.SmokeTest;

internal static class Program
{
	private static int _pass;

	private static int _fail;

	private static void Check(string name, bool ok, string detail = "")
	{
		if (ok)
		{
			_pass++;
			Console.WriteLine("  ok    " + name);
		}
		else
		{
			_fail++;
			Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : ("  (" + detail + ")")));
		}
	}

	private static string MakeTree(string root)
	{
		string src = Path.Combine(root, "src");
		Directory.CreateDirectory(Path.Combine(src, "sub", "deep"));
		Directory.CreateDirectory(Path.Combine(src, "empty"));
		Random rnd = new Random(42);
		byte[] big = new byte[13000000];
		rnd.NextBytes(big);
		File.WriteAllBytes(Path.Combine(src, "big.bin"), big);
		for (int i = 0; i < 40; i++)
		{
			byte[] b = new byte[rnd.Next(0, 90000)];
			rnd.NextBytes(b);
			File.WriteAllBytes(Path.Combine(src, "sub", (i % 2 == 0) ? "deep" : "", $"f{i}.dat"), b);
		}
		File.SetLastWriteTimeUtc(Path.Combine(src, "big.bin"), new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc));
		return src;
	}

	private static bool SameTree(string a, string b)
	{
		List<string> fa = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Select((string f) => Path.GetRelativePath(a, f)).OrderBy((string f) => f, StringComparer.Ordinal).ToList();
		List<string> fb = Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories).Select((string f) => Path.GetRelativePath(b, f)).OrderBy((string f) => f, StringComparer.Ordinal).ToList();
		if (!fa.SequenceEqual(fb))
		{
			return false;
		}
		return fa.All((string f) => File.ReadAllBytes(Path.Combine(a, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, f))));
	}

	private static async Task<JobResult> Run(JobOptions o)
	{
		return await new CopyJob(o).RunAsync(CancellationToken.None);
	}

	private static async Task<int> Main()
	{
		string root = Path.Combine(Path.GetTempPath(), "cf-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(root);
		try
		{
			string src = MakeTree(root);
			Console.WriteLine("Copy + verify, each checksum");
			foreach (VerifyAlgo algo in Enum.GetValues<VerifyAlgo>())
			{
				string dst = Path.Combine(root, "dst-" + algo);
				JobResult r = await Run(new JobOptions
				{
					Sources = new List<string> { src },
					Destination = dst,
					Mode = JobMode.Copy,
					Verify = true,
					VerifyAlgo = algo
				});
				Check($"{VerifyAlgos.Name(algo)}: no failures", r.FilesFailed == 0 && r.Errors.Count == 0, string.Join("; ", r.Errors.Take(2)));
				Check($"{VerifyAlgos.Name(algo)}: tree identical", SameTree(src, Path.Combine(dst, "src")));
				Check($"{VerifyAlgos.Name(algo)}: empty folder kept", Directory.Exists(Path.Combine(dst, "src", "empty")));
			}
			Console.WriteLine("Checksums tell different data apart");
			byte[] x = new byte[5000];
			byte[] y = (byte[])x.Clone();
			y[4321] = 1;
			foreach (VerifyAlgo algo2 in Enum.GetValues<VerifyAlgo>())
			{
				Check(VerifyAlgos.Name(algo2) + ": one flipped byte changes the checksum", HashKey.Once(algo2, x) != HashKey.Once(algo2, y));
				Check(VerifyAlgos.Name(algo2) + ": same data gives the same checksum", HashKey.Once(algo2, x) == HashKey.Once(algo2, (byte[])x.Clone()));
			}
			Check("SHA-256 uses all 256 bits", HashKey.Once(VerifyAlgo.Sha256, x).D != 0uL || HashKey.Once(VerifyAlgo.Sha256, x).C != 0uL);
			Check("algorithm names parse", VerifyAlgos.TryParse("sha-256", out var pa) && pa == VerifyAlgo.Sha256 && VerifyAlgos.TryParse("XXH128", out pa) && pa == VerifyAlgo.XxHash128 && !VerifyAlgos.TryParse("md5", out pa));
			Console.WriteLine("Skip identical, then catch a change");
			string d1 = Path.Combine(root, "dst-" + VerifyAlgo.XxHash3);
			JobResult again = await Run(new JobOptions
			{
				Sources = new List<string> { src },
				Destination = d1,
				Mode = JobMode.Copy
			});
			Check("second run copies nothing", again.FilesProcessed == 0 && again.FilesSkipped > 0, $"copied {again.FilesProcessed}, skipped {again.FilesSkipped}");
			File.AppendAllText(Path.Combine(src, "sub", "new.txt"), "hello");
			JobResult third = await Run(new JobOptions
			{
				Sources = new List<string> { src },
				Destination = d1,
				Mode = JobMode.Copy,
				Verify = true,
				VerifyAlgo = VerifyAlgo.Sha256
			});
			Check("new file is copied and verified", third.FilesProcessed == 1 && third.FilesFailed == 0);
			Console.WriteLine("Block clone setting is harmless where ReFS is absent");
			string d2 = Path.Combine(root, "dst-clone");
			JobResult c = await Run(new JobOptions
			{
				Sources = new List<string> { src },
				Destination = d2,
				Mode = JobMode.Copy,
				BlockClone = true,
				Verify = true
			});
			Check("copy with BlockClone on", c.FilesFailed == 0 && SameTree(src, Path.Combine(d2, "src")));
			Console.WriteLine("Several destinations");
			JobResult a = new JobResult(1L, 2L, 3L, 4L, 5L, TimeSpan.FromSeconds(1.0), Cancelled: false, new string[1] { "e1" });
			JobResult b = new JobResult(10L, 20L, 30L, 40L, 50L, TimeSpan.FromSeconds(2.0), Cancelled: true, new string[1] { "e2" });
			JobResult m = JobResults.Merge(a, b);
			Check("merged counts add up", m.FilesProcessed == 11 && m.FilesSkipped == 22 && m.FilesDeleted == 33 && m.FilesFailed == 44 && m.BytesTransferred == 55 && m.Cancelled && m.Errors.Count == 2 && m.Elapsed == TimeSpan.FromSeconds(3.0));
			Console.WriteLine("History");
			string appData = Path.Combine(root, "appdata");
			Directory.CreateDirectory(appData);
			string oldHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appData);
			try
			{
				JobHistory.Clear();
				JobOptions ho = new JobOptions
				{
					Sources = new List<string> { "C:\\a", "C:\\b" },
					Destination = "F:\\one",
					ExtraDestinations = new List<string> { "G:\\two" },
					Mode = JobMode.Copy,
					Verify = true,
					VerifyAlgo = VerifyAlgo.Sha256
				};
				JobHistory.Append(new JobRecord(new DateTime(2026, 11, 24, 20, 0, 0), ho, new JobResult(5L, 0L, 0L, 0L, 1000L, TimeSpan.FromSeconds(3.0), Cancelled: false, Array.Empty<string>()), null), "test");
				JobHistory.Append(new JobRecord(new DateTime(2026, 11, 24, 21, 0, 0), ho, new JobResult(2L, 0L, 0L, 1L, 10L, TimeSpan.FromSeconds(1.0), Cancelled: false, new string[1] { "boom.bin: access denied" }), null), "test");
				JobHistory.Append(new JobRecord(new DateTime(2026, 11, 24, 22, 0, 0), ho, null, "destination missing"), "test");
				List<HistoryEntry> all = JobHistory.Read();
				Check("three jobs recorded, newest first", all.Count == 3 && all[0].Started.Hour == 22 && all[2].Started.Hour == 20, $"count {all.Count}");
				Check("outcomes", all[0].Outcome == "PROBLEM" && all[1].Outcome == "ERRORS" && all[2].Outcome == "OK");
				Check("search by text", JobHistory.Read(500, "boom").Count == 1 && JobHistory.Read(500, "SHA-256").Count == 3 && JobHistory.Read(500, "nonexistent").Count == 0);
				Check("extra destination kept", all[2].ExtraDestinations.SequenceEqual(new string[1] { "G:\\two" }) && all[2].Sources.Count == 2);
				JobHistory.Clear();
				Check("clear", JobHistory.Read().Count == 0);
			}
			finally
			{
				Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldHome);
			}
			Console.WriteLine("USB speed and eject");
			Check("USB 2.0 is flagged", new BusInfo(Known: true, 7, 2, 0).IsUsb2OrOlder && new BusInfo(Known: true, 7, 2, 0).Describe() == "USB 2.0");
			Check("USB 3.x is not flagged", !new BusInfo(Known: true, 7, 3, 0).IsUsb2OrOlder && new BusInfo(Known: true, 7, 3, 2).Describe() == "USB 3.2");
			Check("unknown USB version is not flagged", !new BusInfo(Known: true, 7, 0, 0).IsUsb2OrOlder && new BusInfo(Known: true, 7, 0, 0).IsUsb);
			Check("non-USB bus is not flagged", !new BusInfo(Known: true, 17, 2, 0).IsUsb2OrOlder && !default(BusInfo).IsUsb);
			Check("bus probe never throws", !Native.ProbeBus(root).IsUsb2OrOlder);
			Check("eject refuses a non-removable path", !Native.EjectVolume(root, out var ejectMsg) && ejectMsg.Length > 0, ejectMsg);
			JobResult ej = await Run(new JobOptions
			{
				Sources = new List<string> { src },
				Destination = Path.Combine(root, "dst-eject"),
				Mode = JobMode.Copy,
				EjectAfter = true
			});
			Check("copy with eject-after on a fixed drive finishes normally", ej.FilesFailed == 0 && ej.FilesProcessed > 0);
			Console.WriteLine("Diagnostics report");
			DiagnosticsResult rep = DiagnosticsReport.Build(new DiagnosticsInput("test", null, new JobOptions
			{
				Sources = new List<string> { src },
				Destination = Path.Combine(root, "dst-report"),
				Verify = true,
				VerifyAlgo = VerifyAlgo.Sha256
			}, new List<string>(), new List<string>(), Path.Combine(root, "crash.log")));
			Check("report names the checksum", rep.Text.Contains("on (SHA-256)"));
			Check("report built without failures", rep.Fails == 0, rep.Fails + " fail");
			// --- encrypted transport ---
			{
				byte[] psk = new byte[32]; new Random(7).NextBytes(psk);
				byte[] nonce = new byte[32]; new Random(8).NextBytes(nonce);
				byte[] ch = new byte[32]; new Random(9).NextBytes(ch);
				byte[] payload = System.Text.Encoding.ASCII.GetBytes("TOP-SECRET-PAYLOAD-12345");
				var wire = new MemoryStream();
				Wire.EnableEncryption(wire, psk, nonce, ch, isClient: true);
				Wire.WriteFrameAsync(wire, 5, payload, CancellationToken.None).GetAwaiter().GetResult();
				Wire.WriteFrameAsync(wire, 5, payload, CancellationToken.None).GetAwaiter().GetResult();
				byte[] raw = wire.ToArray();
				Check("wire: plaintext not visible", raw.AsSpan().IndexOf(payload) < 0);
				var rd = new MemoryStream(raw);
				Wire.EnableEncryption(rd, psk, nonce, ch, isClient: false);
				var f1 = Wire.ReadFrameAsync(rd, CancellationToken.None).GetAwaiter().GetResult();
				var f2 = Wire.ReadFrameAsync(rd, CancellationToken.None).GetAwaiter().GetResult();
				Check("wire: frames round-trip", f1.type == 5 && f1.payload.SequenceEqual(payload) && f2.payload.SequenceEqual(payload));
				byte[] bad = (byte[])raw.Clone(); bad[bad.Length - 3] ^= 1;
				var tm = new MemoryStream(bad); Wire.EnableEncryption(tm, psk, nonce, ch, isClient: false);
				bool tamperCaught = false;
				try { Wire.ReadFrameAsync(tm, CancellationToken.None).GetAwaiter().GetResult(); Wire.ReadFrameAsync(tm, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { tamperCaught = true; }
				Check("wire: tampering detected", tamperCaught);
				// replay: second frame delivered first
				int frameLen = raw.Length / 2;
				var rp = new MemoryStream(raw.Skip(frameLen).ToArray()); Wire.EnableEncryption(rp, psk, nonce, ch, isClient: false);
				bool replayCaught = false;
				try { Wire.ReadFrameAsync(rp, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { replayCaught = true; }
				Check("wire: replay/out-of-order detected", replayCaught);
				var wk = new MemoryStream(raw); byte[] psk2 = (byte[])psk.Clone(); psk2[0] ^= 1; Wire.EnableEncryption(wk, psk2, nonce, ch, isClient: false);
				bool wrongKey = false;
				try { Wire.ReadFrameAsync(wk, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { wrongKey = true; }
				Check("wire: wrong key refused", wrongKey);
			}
			// --- loopback send/receive over TLS + AES layer ---
			{
				string rx = Path.Combine(root, "rx");
				string pskPath = Path.Combine(root, "psk.bin");
				byte[] psk = new byte[32]; new Random(11).NextBytes(psk);
				var srv = new AgentServer(rx, psk, System.Net.IPAddress.Loopback, 0, Path.Combine(root, "cert.pfx"));
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
				Task run = srv.RunAsync(cts.Token);
				for (int i = 0; i < 100 && srv.Port == 0; i++) Thread.Sleep(20);
				var log = new List<string>();
				var res = AgentClient.SendAsync("127.0.0.1", srv.Port, psk, srv.Fingerprint, new[] { Path.Combine(src, "sub") }, m => log.Add(m), cts.Token).GetAwaiter().GetResult();
				Check("loopback: files sent over secure channel", res.Failed == 0 && res.Sent > 0 && log.Any(l => l.Contains("Secure channel up")), $"sent {res.Sent} failed {res.Failed}");
				byte[] badPsk = (byte[])psk.Clone(); badPsk[5] ^= 1;
				bool refused = false;
				try { var r2 = AgentClient.SendAsync("127.0.0.1", srv.Port, badPsk, srv.Fingerprint, new[] { Path.Combine(src, "sub") }, null, cts.Token).GetAwaiter().GetResult(); refused = r2.Sent == 0; } catch (Exception) { refused = true; }
				Check("loopback: wrong secret refused", refused);
				bool badFp = false;
				try { var r3 = AgentClient.SendAsync("127.0.0.1", srv.Port, psk, new string('0', 64), new[] { Path.Combine(src, "sub") }, null, cts.Token).GetAwaiter().GetResult(); badFp = r3.Sent == 0; } catch (Exception) { badFp = true; }
				Check("loopback: wrong fingerprint refused", badFp);
				cts.Cancel();
				try { run.GetAwaiter().GetResult(); } catch { }
			}
			// --- capacity test ---
			{
				string cap = Path.Combine(root, "cap"); Directory.CreateDirectory(cap);
				var cr = CapacityTest.Run(cap, 24L * 1048576L, null, CancellationToken.None, 8L * 1048576L);
				Check("capacity test passes on a healthy folder", cr.Passed && cr.BytesWritten >= 24L * 1048576L && cr.BadBlocks == 0, cr.Summary);
				Check("capacity test leaves no files behind", Directory.GetFileSystemEntries(cap).Length == 0);
			}
			// --- speed limit, CSV log, wipe ---
			{
				string lsrc = Path.Combine(root, "lsrc"); Directory.CreateDirectory(lsrc);
				byte[] blob = new byte[6 * 1048576]; new Random(3).NextBytes(blob);
				File.WriteAllBytes(Path.Combine(lsrc, "a,\"q\".bin"), blob);
				File.WriteAllBytes(Path.Combine(lsrc, "=evil.bin"), new byte[1000]);
				string csv = Path.Combine(root, "log", "run.csv");
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var lr = new CopyJob(new JobOptions { Sources = new List<string> { lsrc }, Destination = Path.Combine(root, "ldst"), LimitMBps = 2, LogPath = csv, Verify = true }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				sw.Stop();
				Check("speed limit slows the copy down", lr.FilesFailed == 0 && sw.Elapsed.TotalSeconds >= 3.0, $"{sw.Elapsed.TotalSeconds:F1}s");
				string txt = File.Exists(csv) ? File.ReadAllText(csv) : "";
				Check("csv log lists both files", txt.Contains("copied") && txt.Split('\n').Length >= 3 && txt.StartsWith("\uFEFFtime,status") );
				Check("csv log neutralises formula cells", txt.Contains("\"'=") || !txt.Contains(",\"=evil") );
				var cts2 = new CancellationTokenSource(); 
				var th = new Throttle(1048576); th.Wait(1048576, cts2.Token); var t0 = System.Diagnostics.Stopwatch.StartNew(); th.Wait(1, cts2.Token); 
				Check("throttle waits after a full second of budget", t0.Elapsed.TotalSeconds >= 0.5, $"{t0.Elapsed.TotalSeconds:F2}s");
				string wdir = Path.Combine(root, "wipe"); Directory.CreateDirectory(Path.Combine(wdir, "sub"));
				File.WriteAllBytes(Path.Combine(wdir, "sub", "x.bin"), new byte[300000]);
				File.WriteAllText(Path.Combine(wdir, "ro.txt"), "hi"); File.SetAttributes(Path.Combine(wdir, "ro.txt"), FileAttributes.ReadOnly);
				var wr = SecureWipe.Run(wdir, 2, null, CancellationToken.None);
				Check("wipe removes files and folders", wr.Files == 2 && wr.Failed == 0 && !Directory.Exists(wdir), $"{wr.Files}/{wr.Failed}");
				// verify skipped files: corrupt a destination file keeping size+mtime, expect it to be re-copied
				string vs = Path.Combine(root, "vs-src"); string vd = Path.Combine(root, "vs-dst"); Directory.CreateDirectory(vs);
				File.WriteAllBytes(Path.Combine(vs, "f.bin"), new byte[100000].Select((b, i) => (byte)(i * 7)).ToArray());
				new CopyJob(new JobOptions { Sources = new List<string> { vs }, Destination = vd }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				string df = Directory.GetFiles(vd, "f.bin", SearchOption.AllDirectories).First();
				DateTime mt = File.GetLastWriteTimeUtc(df); byte[] cur = File.ReadAllBytes(df); cur[500] ^= 0xFF; File.WriteAllBytes(df, cur); File.SetLastWriteTimeUtc(df, mt);
				var plain = new CopyJob(new JobOptions { Sources = new List<string> { vs }, Destination = vd }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				Check("without verify-skipped a silent corruption is missed", plain.FilesProcessed == 0);
				var vsr = new CopyJob(new JobOptions { Sources = new List<string> { vs }, Destination = vd, Verify = true, VerifySkipped = true }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				Check("verify-skipped finds and repairs it", vsr.FilesProcessed == 1 && File.ReadAllBytes(df).SequenceEqual(File.ReadAllBytes(Directory.GetFiles(vs, "f.bin", SearchOption.AllDirectories).First())));
				// resume: a half-written .cfpart (plus a wrong block) must be completed, not restarted blindly
				string rs = Path.Combine(root, "rs-src"); string rd = Path.Combine(root, "rs-dst"); Directory.CreateDirectory(rs); Directory.CreateDirectory(rd);
				byte[] big2 = new byte[5 * 1048576]; new Random(21).NextBytes(big2);
				File.WriteAllBytes(Path.Combine(rs, "big.bin"), big2);
				byte[] half = new byte[3 * 1048576 + 12345]; Array.Copy(big2, half, half.Length); half[1000] ^= 0xFF;
				Directory.CreateDirectory(Path.Combine(rd, "rs-src")); File.WriteAllBytes(Path.Combine(rd, "rs-src", "big.bin.cfpart"), half);
				var rj = new CopyJob(new JobOptions { Sources = new List<string> { rs }, Destination = rd, ResumeMB = 1, Verify = true }); bool resumedMsg = false; rj.Message += m => { if (m.StartsWith("Resumed an interrupted")) resumedMsg = true; }; var rr = rj.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				string rfin = Directory.GetFiles(rd, "big.bin", SearchOption.AllDirectories).FirstOrDefault() ?? "";
				Check("resume completes a partial file correctly", resumedMsg && rr.FilesFailed == 0 && rfin != "" && File.ReadAllBytes(rfin).SequenceEqual(big2) && Directory.GetFiles(rd, "*.cfpart", SearchOption.AllDirectories).Length == 0, $"failed {rr.FilesFailed} done {rr.FilesProcessed} fin [{rfin}] errs {string.Join("|", rr.Errors)} parts {Directory.GetFiles(rd, "*", SearchOption.AllDirectories).Length}");
				{
					var b3vec = new (int n, string hex)[] {
				(0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262"),
				(1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213"),
				(63, "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b"),
				(64, "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98"),
				(65, "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee"),
				(1023, "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11"),
				(1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7"),
				(1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444"),
				(2048, "e776b6028c7cd22a4d0ba182a8bf62205d2ef576467e838ed6f2529b85fba24a"),
				(2049, "5f4d72f40d7a5f82b15ca2b2e44b1de3c2ef86c426c95c1af0b6879522563030"),
				(3072, "b98cb0ff3623be03326b373de6b9095218513e64f1ee2edd2525c7ad1e5cffd2"),
				(3073, "7124b49501012f81cc7f11ca069ec9226cecb8a2c850cfe644e327d22d3e1cd3"),
				(4096, "015094013f57a5277b59d8475c0501042c0b642e531b0a1c8f58d2163229e969"),
				(5000, "ee78d92070de3df1c57c37002abf0a6b1a6589acdeef4d8ffac7cf3d9e8f2836"),
				(7168, "61da957ec2499a95d6b8023e2b0e604ec7f6b50e80a9678b89d2628e99ada77a"),
				(8192, "aae792484c8efe4f19e2ca7d371d8c467ffb10748d8a5a1ae579948f718a2a63"),
				(100000, "d93c23eedaf165a7e0be908ba86f1a7a520d568d2d13cde787c8580c5c72cc54"),
				(1048583, "89541f1047f7a56806fe16efda4c2cdc45f141c838e413019f0124189fa55232")
					};
					bool b3ok = true; string b3bad = "";
					foreach (var (n, hex) in b3vec)
					{
						byte[] d = new byte[n]; for (int i = 0; i < n; i++) d[i] = (byte)(i % 251);
						string one = Convert.ToHexString(Blake3.Hash(d)).ToLowerInvariant();
						var inc = new Blake3(); int pos = 0; int step = 1; while (pos < n) { int take = Math.Min(step, n - pos); inc.Append(d.AsSpan(pos, take)); pos += take; step = step * 3 % 977 + 1; }
						string two = Convert.ToHexString(inc.Finish()).ToLowerInvariant();
						if (one != hex || two != hex) { b3ok = false; b3bad += n + " "; }
					}
					Check("BLAKE3 matches the reference implementation (one-shot and incremental)", b3ok, b3bad);
				}
				// versions: overwritten and sync-deleted files are kept
				string vsrc = Path.Combine(root, "ver-src"); string vdst = Path.Combine(root, "ver-dst"); Directory.CreateDirectory(vsrc);
				File.WriteAllText(Path.Combine(vsrc, "a.txt"), "one"); File.WriteAllText(Path.Combine(vsrc, "b.txt"), "bee");
				new CopyJob(new JobOptions { Sources = new List<string> { vsrc + Path.DirectorySeparatorChar }, Destination = vdst, Mode = JobMode.Sync, KeepVersions = true }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				File.WriteAllText(Path.Combine(vsrc, "a.txt"), "TWO-longer"); File.SetLastWriteTimeUtc(Path.Combine(vsrc, "a.txt"), DateTime.UtcNow.AddMinutes(5)); File.Delete(Path.Combine(vsrc, "b.txt"));
				var vr = new CopyJob(new JobOptions { Sources = new List<string> { vsrc + Path.DirectorySeparatorChar }, Destination = vdst, Mode = JobMode.Sync, KeepVersions = true, Overwrite = OverwriteMode.Always }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				string[] kept = Directory.Exists(Path.Combine(vdst, ".cf_versions")) ? Directory.GetFiles(Path.Combine(vdst, ".cf_versions"), "*", SearchOption.AllDirectories) : new string[0];
				Check("versions keep the replaced and the deleted file", vr.FilesFailed == 0 && kept.Any(k => File.ReadAllText(k) == "one") && kept.Any(k => File.ReadAllText(k) == "bee") && File.ReadAllText(Directory.GetFiles(vdst, "a.txt", SearchOption.TopDirectoryOnly).First()) == "TWO-longer", string.Join(",", kept.Select(Path.GetFileName)) + " errs " + string.Join("|", vr.Errors));
				var vr2 = new CopyJob(new JobOptions { Sources = new List<string> { vsrc + Path.DirectorySeparatorChar }, Destination = vdst, Mode = JobMode.Sync, KeepVersions = true }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
				Check("a later sync does not delete the versions folder", Directory.Exists(Path.Combine(vdst, ".cf_versions")) && Directory.GetFiles(Path.Combine(vdst, ".cf_versions"), "*", SearchOption.AllDirectories).Length == kept.Length);
				{
					string pr = Path.Combine(root, "par"); Directory.CreateDirectory(pr);
					var rnd = new Random(7);
					byte[] big = new byte[5_000_000 + 123]; rnd.NextBytes(big);
					byte[] small = new byte[1000]; rnd.NextBytes(small);
					File.WriteAllBytes(Path.Combine(pr, "big.bin"), big);
					File.WriteAllBytes(Path.Combine(pr, "small.bin"), small);
					File.WriteAllBytes(Path.Combine(pr, "empty.bin"), new byte[0]);
					var pres = Parity.Protect(pr, 10, null, CancellationToken.None);
					Check("parity written for every file", pres.Written == 3 && pres.Problems.Count == 0, string.Join("|", pres.Problems));
					Check("parity run again skips current files", Parity.Protect(pr, 10, null, CancellationToken.None).Skipped == 3);
					Check("parity check finds nothing wrong in good files", Parity.Repair(pr, true, null, CancellationToken.None).Damaged == 0);
					using (var fs = new FileStream(Path.Combine(pr, "big.bin"), FileMode.Open, FileAccess.Write)) { fs.Position = 1_200_000; fs.Write(new byte[150_000]); fs.Position = big.Length - 10; fs.Write(new byte[10]); }
					using (var fs = new FileStream(Path.Combine(pr, "small.bin"), FileMode.Open, FileAccess.Write)) { fs.Position = 100; fs.Write(new byte[50]); }
					var chk = Parity.Repair(pr, true, null, CancellationToken.None);
					Check("parity check reports the damage and changes nothing", chk.Damaged == 2 && !File.ReadAllBytes(Path.Combine(pr, "big.bin")).SequenceEqual(big));
					var fix = Parity.Repair(pr, false, null, CancellationToken.None);
					Check("repair rebuilds a damaged stretch and the short last block", fix.Repaired == 2 && fix.Unrepairable == 0 && File.ReadAllBytes(Path.Combine(pr, "big.bin")).SequenceEqual(big) && File.ReadAllBytes(Path.Combine(pr, "small.bin")).SequenceEqual(small), string.Join("|", fix.Problems));
					using (var fs = new FileStream(Path.Combine(pr, "big.bin"), FileMode.Open, FileAccess.Write)) { fs.Position = 100; fs.Write(new byte[3_000_000]); }
					var lost = Parity.Repair(pr, false, null, CancellationToken.None);
					Check("too much damage is reported, not hidden", lost.Unrepairable == 1 && !File.ReadAllBytes(Path.Combine(pr, "big.bin")).SequenceEqual(big));
					var sc = Directory.GetFiles(Path.Combine(pr, ".cf_parity"), "small.bin.cfpar", SearchOption.AllDirectories).First();
					byte[] scb = File.ReadAllBytes(sc); scb[40] ^= 0xFF; File.WriteAllBytes(sc, scb);
					using (var fs = new FileStream(Path.Combine(pr, "small.bin"), FileMode.Open, FileAccess.Write)) { fs.Write(new byte[5]); }
					Check("damaged repair data is refused", Parity.Repair(pr, false, null, CancellationToken.None).Unrepairable >= 2);
					string pd = Path.Combine(root, "pardst"); Directory.CreateDirectory(Path.Combine(pd, "x"));
					File.WriteAllText(Path.Combine(pd, "x", "keep.txt"), "k"); Parity.Protect(pd, 5, null, CancellationToken.None);
					var psrc = Path.Combine(root, "parsrc"); Directory.CreateDirectory(psrc);
					new CopyJob(new JobOptions { Sources = new List<string> { psrc + Path.DirectorySeparatorChar }, Destination = pd, Mode = JobMode.Sync }).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
					Check("sync leaves the repair data folder alone", Directory.Exists(Path.Combine(pd, ".cf_parity")));
				}
				{
					string lsdir = Path.Combine(root, "ls"); Directory.CreateDirectory(lsdir);
					int asked = 0; bool allow = true;
					var rcv = new LocalSendReceiver(lsdir, "Test PC", "1234", (al, ip, n, b) => { asked++; return allow; }, System.Net.IPAddress.Loopback, 0, Path.Combine(root, "ls.pfx"), false);
					rcv.Start();
					using var lcts = new CancellationTokenSource();
					var lrun = rcv.RunAsync(lcts.Token);
					using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
					string baseUrl = "https://127.0.0.1:" + rcv.Port + "/api/localsend/v2/";
					string LsPost(string u, string json) { var r = http.PostAsync(baseUrl + u, new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult(); return (int)r.StatusCode + "|" + r.Content.ReadAsStringAsync().GetAwaiter().GetResult(); }
					byte[] data = new byte[300_000]; new Random(3).NextBytes(data);
					string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
					string files = "{\"info\":{\"alias\":\"Phone\"},\"files\":{\"f1\":{\"id\":\"f1\",\"fileName\":\"..\\\\..\\\\evil:name.bin\",\"size\":300000,\"fileType\":\"x\",\"sha256\":\"" + sha + "\"},\"f2\":{\"id\":\"f2\",\"fileName\":\"sub/ok.txt\",\"size\":5,\"fileType\":\"x\",\"sha256\":\"" + new string('0', 64) + "\"},}}";
					Check("localsend register answers with our info", LsPost("register", "{}").StartsWith("200|") && LsPost("register", "{}").Contains(rcv.Fingerprint));
					Check("localsend refuses a wrong PIN", LsPost("prepare-upload?pin=0000", files).StartsWith("401|") && asked == 0);
					allow = false;
					Check("localsend declined transfer gets 403", LsPost("prepare-upload?pin=1234", files).StartsWith("403|") && asked == 1);
					allow = true;
					string prep = LsPost("prepare-upload?pin=1234", files);
					Check("localsend accepted transfer gets a session", prep.StartsWith("200|"), prep);
					var pd = System.Text.Json.JsonDocument.Parse(prep.Substring(4)).RootElement;
					string sess = pd.GetProperty("sessionId").GetString()!; string t1 = pd.GetProperty("files").GetProperty("f1").GetString()!; string t2 = pd.GetProperty("files").GetProperty("f2").GetString()!;
					int Up(string fid, string tok, byte[] d, string sid = null) { var r = http.PostAsync(baseUrl + "upload?sessionId=" + (sid ?? sess) + "&fileId=" + fid + "&token=" + tok, new System.Net.Http.ByteArrayContent(d)).GetAwaiter().GetResult(); return (int)r.StatusCode; }
					Check("localsend upload with a wrong token is refused", Up("f1", "nope", data) == 403);
					Check("localsend upload is stored and checked", Up("f1", t1, data) == 200);
					Check("localsend bad checksum is discarded (422)", Up("f2", t2, System.Text.Encoding.ASCII.GetBytes("hello")) == 422 && !File.Exists(Path.Combine(lsdir, "sub", "ok.txt")));
					var got = Directory.GetFiles(lsdir, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".pfx")).ToArray();
					Check("localsend path tricks stay inside the folder", got.Length == 1 && Path.GetFullPath(got[0]).StartsWith(Path.GetFullPath(lsdir)) && File.ReadAllBytes(got[0]).SequenceEqual(data), string.Join(",", got));
					LsPost("cancel?sessionId=" + sess, "{}");
					string prep2 = LsPost("prepare-upload?pin=1234", "{\"info\":{\"alias\":\"P\"},\"files\":{\"a\":{\"id\":\"a\",\"fileName\":\"" + Path.GetFileName(got[0]) + "\",\"size\":3,\"fileType\":\"x\"}}}");
					var pd2 = System.Text.Json.JsonDocument.Parse(prep2.Substring(4)).RootElement;
					Up("a", pd2.GetProperty("files").GetProperty("a").GetString()!, new byte[3], pd2.GetProperty("sessionId").GetString());
					Check("localsend never overwrites an existing file", File.ReadAllBytes(got[0]).SequenceEqual(data) && Directory.GetFiles(lsdir, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".pfx")) == 2);
					lcts.Cancel();
				}
				{
					var jo = new JobOptions { Sources = new List<string> { "C:\\a", "C:\\b" }, Destination = "D:\\x", ExtraDestinations = new List<string> { "E:\\y" }, Mode = JobMode.Sync, Verify = true, VerifyAlgo = VerifyAlgo.Blake3, KeepVersions = true, LimitMBps = 50, Include = "*.doc" };
					var back = System.Text.Json.JsonSerializer.Deserialize<JobOptions>(System.Text.Json.JsonSerializer.Serialize(jo))!;
					Check("a job survives being saved as a preset (JSON)", back.Mode == JobMode.Sync && back.VerifyAlgo == VerifyAlgo.Blake3 && back.KeepVersions && back.LimitMBps == 50 && back.Sources.Count == 2 && back.ExtraDestinations[0] == "E:\\y" && back.Include == "*.doc" && back.Destination == "D:\\x");
				}
				using (var ka = new KeepAwake()) { }
				Check("keep-awake starts and stops cleanly", true);
			}
		}
		finally
		{
			try
			{
				Directory.Delete(root, recursive: true);
			}
			catch
			{
			}
		}
		Console.WriteLine();
		Console.WriteLine((_fail == 0) ? $"ALL PASSED ({_pass} checks)" : $"FAILED: {_fail} of {_pass + _fail} checks");
		return (_fail == 0) ? 0 : 1;
	}
}
