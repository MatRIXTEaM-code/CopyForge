using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CopyForge.Engine;
using CopyForge.Engine.Net;

namespace CopyForge.Cli;

internal static class Program
{
	private const string Usage = "\ncfcopy - CopyForge command line\n\n  cfcopy copy   SOURCE [SOURCE...] --to DEST [options]\n  cfcopy move   SOURCE [SOURCE...] --to DEST [options]\n  cfcopy sync   SOURCE_FOLDER      --to DEST [options]   (mirror: deletes extra files in DEST; needs --yes)\n  cfcopy delete SOURCE [SOURCE...] [options]             (needs --yes)\n  cfcopy watch  SOURCE [SOURCE...] --to DEST [options]   (keep DEST in step until Ctrl+C; re-syncs when the stick/share returns)\n  cfcopy receive FOLDER [--listen ADDR] [--port 9333] [--psk-file F]   (accept files from another PC into FOLDER; prints the fingerprint)\n  cfcopy send   SOURCE [SOURCE...] --to HOST[:PORT] --fingerprint HEX [--psk-file F]   (push files to a receiving PC)\n  cfcopy audit  FOLDER [--pin KEYID] [--extra]           (re-hash every file against cfmanifest.json; exit 1 if anything differs)\n  cfcopy compare SOURCE_FOLDER --to DEST                 (list what differs; changes nothing; exit 1 if they differ)\n  cfcopy run    JOBFILE                                  (run the commands in a text file one after another; # = comment)\n  cfcopy bench  [--from DIR] --to DIR [--size-mb 1024]   (speed test of the copy strategies on your drives)\n  cfcopy fakecheck FOLDER [--size-mb N]                  (fake-capacity drive test: fills the free space on the drive holding FOLDER with checkable data, reads it back, deletes it; N limits how many MB to test)\n  cfcopy wipe   PATH [PATH...] [--passes N] --yes         (overwrite with random data, then delete; not reliable on SSD/USB flash)\n  cfcopy schedule add NAME --daily HH:MM | --weekly MON HH:MM | --logon -- COMMAND...   (run a job from Windows Task Scheduler while you are logged on)\n  cfcopy schedule list | schedule remove NAME\n  cfcopy localsend FOLDER [--alias NAME] [--on-conflict rename|skip|overwrite] [--hash-log] [--pin PIN [--auto-accept]]   (receive files from the LocalSend app on phones / PCs; asks before accepting unless --auto-accept)\n  cfcopy lsfind                                          (list LocalSend devices on the network)\n  cfcopy lssend SOURCE... --to DEVICE [--pin PIN]        (send files/folders to a LocalSend device; DEVICE = name from lsfind, or IP[:PORT])\n  cfcopy protect FOLDER [--redundancy 5]                 (write repair data (Reed-Solomon) for every file into FOLDER\\.cf_parity; 1-50 percent)\n  cfcopy repair  FOLDER [--check]                        (find damaged files and rebuild them from the repair data; --check only reports)\n  cfcopy hashmake FOLDER [--out FILE]                    (write a standard SHA-256 checksum file, sha256sum format; default FOLDER\\checksums.sha256)\n  cfcopy hashcheck FILE.sha256 [--to FOLDER]             (verify files against a checksum file; FOLDER defaults to the file's folder; exit 1 if anything is bad or missing)\n  cfcopy lshistory [N] [--search DEVICE]                 (recent LocalSend transfers, sent and received)\n  cfcopy recheck [N]                                     (compare the Nth most recent job, default 1, with its destination again; changes nothing; exit 1 if anything is missing or different)\n  cfcopy history [N] [--search TEXT] [--clear --yes]     (list the last N jobs, default 20; --clear erases the history)\n\nOptions\n  --verify                 re-read and compare every file (bypasses the Windows cache)\n  --verify-algo ALGO       xxh3 (default, fast) | xxh128 | sha256 | blake3 ; implies --verify\n  --no-clone               do not use ReFS block cloning for same-volume copies\n  --parity N               copy/move/sync: after a clean job, write N percent repair data for DEST (see 'protect')\n  --versions               keep the old copy of every replaced (or sync-deleted) file in DEST\\.cf_versions\\date_time\n  --show-robocopy          copy/move/sync: print the nearest robocopy command(s) and exit without copying\n  --out FILE               hashmake: where to write the checksum file\n  --on-error SCRIPT        like --on-finish but only when the job ended with errors (exit code 1) or was cancelled\n  --on-finish SCRIPT       run a .ps1 / .cmd / .bat / .exe when the job ends (exit code in CF_EXITCODE); not run on dry runs\n  --resume-mb N            interrupted files of N MB or more keep their .cfpart and resume next run (default 256)   --no-resume  turn that off\n  --verify-skipped         with --verify: also compare files skipped as identical byte for byte (copies again if they differ)\n  --limit-mb N             speed limit in MB per second (uses the CopyForge pipeline)\n  --log FILE               append a per-file CSV log (copied / skipped / FAILED)\n  --after ACTION           shutdown | sleep the PC when the job finishes without errors\n  --no-keep-awake          do not stop Windows sleeping while the job runs\n  --eject                  safely eject a removable destination when the job finishes without errors\n  --safe-names             USB sticks: keep the temp name + rename for every file (slower, no partial file under the real name)\n  --overwrite MODE         different (default) | newer | always | never\n  --include \"a;b\"          wildcard include list        --exclude \"a;b/\"   wildcard exclude list (trailing / = folders)\n  --min-mb N  --max-mb N   size limits                  --days N          only files modified in the last N days\n  --threads N              files in parallel (0 = auto)  --buffer MB       block size 1-64 (default 4)\n  --io MODE                auto (default) | buffered | direct\n  --method MODE            auto (default) | pipeline | native\n  --chunks N               workers per big file, 0 = auto\n  --retries N              extra attempts per file (default 3)\n  --wait SECONDS           how long to wait for an unplugged destination to return (default 120)\n  --acl                    copy NTFS permissions and alternate data streams\n  --split                  FAT32 destination: write files over 4 GB as .001/.002 parts\n  --no-flush               do not force data to disk before finishing each file\n  --no-dirtimes            do not copy folder timestamps\n  --recycle                delete: send to the Recycle Bin (Windows)\n  --usn                    EXPERIMENTAL (Windows, NTFS, run as administrator): copy only what the NTFS change journal says changed\n  --full-scan              with --usn: force a full folder walk this time\n  --autotune               adjust the number of parallel files live while copying (with --threads 0)\n  --delta                  big files that already exist in DEST: rewrite only the blocks that changed\n  --delta-min-mb N         delta applies to files at least N MB (default 32)\n  --dry-run                preview only: show what would be copied/deleted, change nothing\n  --vss                    read files that another program holds open from a Volume Shadow Copy (Windows, administrator)\n  --also DEST              copy to a second destination too, afterwards (repeatable; the source is read again from cache)\n  --manifest               write cfmanifest.json (SHA-256 of every file) into DEST after the job\n  --sign KEYFILE           sign the manifest with an ECDSA key (created if missing); implies --manifest\n  --pin KEYID              audit: require the manifest to be signed by this key id\n  --extra                  audit: also list files that are not in the manifest\n  --quiet-secs N           watch: wait N seconds after the last change before syncing (default 5)\n  --mirror                 watch: also delete from DEST what was deleted from SOURCE (needs --yes)\n  --listen ADDR            receive: address to listen on (default 127.0.0.1 = this PC only; use 0.0.0.0 for the LAN)\n  --port N                 receive: TCP port (default 9333)\n  --psk-file FILE          receive/send: the shared secret file (created by 'receive' if missing; copy it to the sender by hand)\n  --fingerprint HEX        send: the receiver's certificate fingerprint, as printed by 'receive'\n  --yes                    confirm destructive modes (sync, delete)\n  --quiet                  no progress output\n\nExit codes: 0 ok, 1 finished with errors, 2 bad usage, 3 cancelled (Ctrl+C)\n";

	private static readonly CancellationTokenSource Ctl = new CancellationTokenSource();

	private static string AgentDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyForge", "agent");

	private static async Task<int> Receive(string folder, string listen, int port, string pskFile)
	{
		if (pskFile.Length == 0)
		{
			pskFile = Path.Combine(AgentDir, "psk.txt");
		}
		bool num = !File.Exists(pskFile);
		byte[] psk = Wire.LoadOrCreatePsk(pskFile);
		if (!IPAddress.TryParse(listen, out IPAddress address))
		{
			throw new ArgumentException("bad --listen address");
		}
		AgentServer agentServer = new AgentServer(folder, psk, address, port, Path.Combine(AgentDir, "cert.pfx"));
		agentServer.Log += delegate(string m)
		{
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
		};
		if (num)
		{
			Console.WriteLine("Created a new shared secret: " + pskFile + "\nCopy this file to the sending PC (USB stick, not email) and pass it with --psk-file.");
		}
		if (!address.Equals(IPAddress.Loopback))
		{
			Console.WriteLine("NOTE: listening beyond this PC. Only machines holding the secret file AND the fingerprint below can send; allow the port in your firewall only for trusted networks.");
		}
		Console.WriteLine("Sender command:  cfcopy send <files> --to <this-pc>:" + port + " --fingerprint " + agentServer.Fingerprint + " --psk-file <secret file>");
		try
		{
			await agentServer.RunAsync(Ctl.Token);
		}
		catch (SocketException ex)
		{
			Console.Error.WriteLine("cfcopy: cannot listen: " + ((Exception)(object)ex).Message);
			return 1;
		}
		return 0;
	}

	private static async Task<int> Send(List<string> sources, string to, int defaultPort, string pskFile, string fingerprint)
	{
		if (pskFile.Length == 0)
		{
			pskFile = Path.Combine(AgentDir, "psk.txt");
		}
		if (!File.Exists(pskFile))
		{
			Console.Error.WriteLine("cfcopy: secret file not found: " + pskFile);
			return 2;
		}
		byte[] psk = Wire.LoadOrCreatePsk(pskFile);
		string host = to;
		int port = defaultPort;
		int num = to.LastIndexOf(':');
		if (num > 0)
		{
			int num2 = num + 1;
			if (int.TryParse(to.Substring(num2, to.Length - num2), out var result))
			{
				host = to.Substring(0, num);
				port = result;
			}
		}
		try
		{
			SendResult sendResult = await AgentClient.SendAsync(host, port, psk, fingerprint, sources, delegate(string m)
			{
				Console.WriteLine(m);
			}, Ctl.Token);
			Console.WriteLine($"Sent {sendResult.Sent:N0} of {sendResult.Files:N0} files ({Fmt(sendResult.Bytes)}); {sendResult.Failed:N0} failed.");
			foreach (string item in sendResult.Errors.Take(20))
			{
				Console.Error.WriteLine("  ERROR: " + item);
			}
			return (sendResult.Failed > 0) ? 1 : 0;
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("cancelled");
			return 3;
		}
		catch (Exception ex2)
		{
			Console.Error.WriteLine("cfcopy: " + ex2.Message);
			return 1;
		}
	}

	private static async Task<int> Watch(JobOptions o, int quietSecs, bool quiet)
	{
		o.EjectAfter = false;
		Watcher watcher = new Watcher(o, TimeSpan.FromSeconds((double)Math.Clamp(quietSecs, 1, 3600)));
		watcher.Event += delegate(WatchEvent e)
		{
			if (!(e.Kind == "log" && quiet))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[");
				defaultInterpolatedStringHandler.AppendFormatted(DateTime.Now, "HH:mm:ss");
				defaultInterpolatedStringHandler.AppendLiteral("] ");
				string kind = e.Kind;
				bool flag = ((kind == "warn" || kind == "error") ? true : false);
				defaultInterpolatedStringHandler.AppendFormatted(flag ? (e.Kind.ToUpperInvariant() + ": ") : "");
				defaultInterpolatedStringHandler.AppendFormatted(e.Text);
				Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		};
		try
		{
			await watcher.RunAsync(Ctl.Token);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("cfcopy: " + ex.Message);
			return 1;
		}
		return 0;
	}

	private static async Task<int> Audit(string folder, int threads, bool extra, string? pin, bool quiet)
	{
		if (!Directory.Exists(folder))
		{
			Console.Error.WriteLine("cfcopy: folder not found: " + folder);
			return 2;
		}
		if (threads <= 0)
		{
			threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
		}
		int last = -1;
		Progress<(int, int)> progress = (quiet ? null : new Progress<(int, int)>(delegate((int done, int total) p)
		{
			int num = ((p.total == 0) ? 100 : (p.done * 100 / p.total));
			if (num != last)
			{
				last = num;
				Console.Write($"\rAuditing... {num,3}% ({p.done:N0}/{p.total:N0})");
			}
		}));
		AuditResult auditResult;
		try
		{
			auditResult = await Manifest.AuditAsync(folder, threads, extra, pin, progress, Ctl.Token);
		}
		catch (OperationCanceledException)
		{
			Console.WriteLine();
			Console.Error.WriteLine("cancelled");
			return 3;
		}
		if (!quiet)
		{
			Console.WriteLine();
		}
		if (!auditResult.ManifestFound)
		{
			Console.Error.WriteLine("No readable cfmanifest.json in " + folder + ". Copy with --manifest first.");
			return 2;
		}
		Console.WriteLine($"Checked {auditResult.Checked:N0} files: {auditResult.Ok:N0} OK, {auditResult.Changed.Count:N0} changed, {auditResult.Missing.Count:N0} missing.");
		Console.WriteLine("Signature: " + auditResult.Signature + ((auditResult.KeyFingerprint != null) ? (" (key id " + auditResult.KeyFingerprint + ")") : ""));
		if (auditResult.Signature == "valid" && string.IsNullOrWhiteSpace(pin))
		{
			Console.WriteLine("  Note: the signature matches the key stored beside the manifest, but that key is not pinned, so it only proves the file was not edited by accident. Use --pin <key id> to prove who signed it.");
		}
		foreach (string item in auditResult.Changed)
		{
			Console.WriteLine("  CHANGED  " + item);
		}
		foreach (string item2 in auditResult.Missing)
		{
			Console.WriteLine("  MISSING  " + item2);
		}
		foreach (string item3 in auditResult.Unlisted)
		{
			Console.WriteLine("  EXTRA    " + item3);
		}
		Console.WriteLine(auditResult.Clean ? "RESULT: clean." : "RESULT: PROBLEMS FOUND.");
		return (!auditResult.Clean) ? 1 : 0;
	}

	private static async Task<int> Main(string[] args)
	{
		Console.CancelKeyPress += delegate(object? _, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
			if (!Ctl.IsCancellationRequested)
			{
				Console.Error.WriteLine("\nCancelling...");
			}
			Ctl.Cancel();
		};
		if (args.Length >= 2 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
		{
			return await RunJobFile(args[1]);
		}
		if (args.Length >= 1 && args[0].Equals("schedule", StringComparison.OrdinalIgnoreCase))
		{
			return Scheduler.Run(args);
		}
		return await RunCli(args);
	}

	private static async Task<int> RunJobFile(string path)
	{
		if (!File.Exists(path))
		{
			Console.Error.WriteLine("cfcopy: job file not found: " + path);
			return 2;
		}
		List<string> lines = (from l in File.ReadAllLines(path)
			select l.Trim() into l
			where l.Length > 0 && !l.StartsWith('#')
			select l).ToList();
		int worst = 0;
		int i = 0;
		foreach (string item in lines)
		{
			if (Ctl.IsCancellationRequested)
			{
				Console.WriteLine("Queue cancelled.");
				return 3;
			}
			i++;
			Console.WriteLine($"\n===== Job {i} of {lines.Count}: {item}");
			int num = await RunCli(SplitArgs(item));
			Console.WriteLine($"===== Job {i} finished with exit code {num}");
			if (num == 3)
			{
				return 3;
			}
			worst = Math.Max(worst, num);
		}
		Console.WriteLine($"\nQueue finished: {lines.Count} job(s), " + ((worst == 0) ? "all OK." : "some had problems (see above)."));
		return worst;
	}

	private static string[] SplitArgs(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		bool flag2 = false;
		foreach (char c in line)
		{
			if (c == '"')
			{
				flag = !flag;
				flag2 = true;
			}
			else if (char.IsWhiteSpace(c) && !flag)
			{
				if (flag2 || stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					flag2 = false;
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (flag2 || stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list.ToArray();
	}

	private static async Task<int> RunCli(string[] args)
	{
		string[] args2 = args;
		bool flag = args2.Length == 0;
		if (!flag)
		{
			bool flag2;
			switch (args2[0])
			{
			case "-h":
			case "--help":
			case "/?":
			case "help":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			Console.WriteLine(Usage);
			return (args2.Length == 0) ? 2 : 0;
		}
		string text = args2[0].ToLowerInvariant();
		List<string> list = new List<string>();
		string text2 = null;
		string from = null;
		JobOptions o = new JobOptions();
		bool flag3 = false;
		bool quiet = false;
		bool extra = false;
		string pin = null;
		int quietSecs = 5;
		bool flag4 = false;
		bool useUsn = false;
		bool fullScan = false;
		List<string> also = new List<string>();
		string search = "";
		bool clearHistory = false;
		string listen = "127.0.0.1";
		string pskFile = "";
		string text3 = "";
		int num = 9333;
		int sizeMb = 1024;
		bool sizeGiven = false;
		string afterAction = "";
		string onFinish = "";
		string onError = "";
		bool showRobo = false;
		string outFile = "";
		int wipePasses = 1;
		int parityPct = 0;
		string lsAlias = "";
		string lsConflict = "rename";
		bool lsHashLog = false;
		bool autoAccept = false;
		bool listenGiven = false;
		bool portGiven = false;
		bool checkOnly = false;
		double? num2 = null;
		try
		{
			int i;
			for (i = 1; i < args2.Length; i++)
			{
				string a = args2[i];
				switch (a.ToLowerInvariant())
				{
				case "--to":
					text2 = Next();
					break;
				case "--from":
					from = Next();
					break;
				case "--verify":
					o.Verify = true;
					break;
				case "--verify-algo":
				{
					string algoText = Next();
					if (!VerifyAlgos.TryParse(algoText, out var algo))
					{
						throw new ArgumentException("unknown --verify-algo '" + algoText + "' (use xxh3, xxh128 or sha256)");
					}
					o.VerifyAlgo = algo;
					o.Verify = true;
					break;
				}
				case "--no-clone":
					o.BlockClone = false;
					break;
				case "--safe-names":
					o.DirectWriteRemovable = false;
					break;
				case "--eject":
					o.EjectAfter = true;
					break;
				case "--versions":
					o.KeepVersions = true;
					break;
				case "--on-finish":
					onFinish = Next();
					break;
				case "--show-robocopy":
					showRobo = true;
					break;
				case "--out":
					outFile = Next();
					break;
				case "--on-error":
					onError = Next();
					break;
				case "--no-resume":
					o.ResumeMB = 0;
					break;
				case "--resume-mb":
					o.ResumeMB = Int();
					break;
				case "--verify-skipped":
					o.VerifySkipped = true;
					o.Verify = true;
					break;
				case "--limit-mb":
					o.LimitMBps = Int();
					break;
				case "--log":
					o.LogPath = Next();
					break;
				case "--no-keep-awake":
					o.KeepAwake = false;
					break;
				case "--after":
					afterAction = Next().ToLowerInvariant();
					if (afterAction != "shutdown" && afterAction != "sleep")
					{
						throw new ArgumentException("--after must be shutdown or sleep");
					}
					break;
				case "--parity":
				case "--redundancy":
					parityPct = Int();
					break;
				case "--check":
					checkOnly = true;
					break;
				case "--passes":
					wipePasses = Int();
					break;
				case "--search":
					search = Next();
					break;
				case "--clear":
					clearHistory = true;
					break;
				case "--overwrite":
				{
					JobOptions jobOptions = o;
					string text4 = Next().ToLowerInvariant();
					jobOptions.Overwrite = text4 switch
					{
						"different" => OverwriteMode.IfDifferent, 
						"newer" => OverwriteMode.IfNewer, 
						"always" => OverwriteMode.Always, 
						"never" => OverwriteMode.Skip, 
						_ => throw new ArgumentException("unknown overwrite mode '" + text4 + "'"), 
					};
					break;
				}
				case "--include":
					o.Include = Next();
					break;
				case "--exclude":
					o.Exclude = Next();
					break;
				case "--min-mb":
					o.MinSizeBytes = (long)Int() * 1024L * 1024;
					break;
				case "--max-mb":
					o.MaxSizeBytes = (long)Int() * 1024L * 1024;
					break;
				case "--days":
					num2 = double.Parse(Next(), CultureInfo.InvariantCulture);
					break;
				case "--threads":
					o.Threads = Int();
					break;
				case "--buffer":
					o.BufferSizeMB = Int();
					break;
				case "--io":
				{
					JobOptions jobOptions = o;
					string text6 = Next().ToLowerInvariant();
					jobOptions.Io = text6 switch
					{
						"auto" => IoMode.Auto, 
						"buffered" => IoMode.Buffered, 
						"direct" => IoMode.Direct, 
						_ => throw new ArgumentException("unknown I/O mode '" + text6 + "'"), 
					};
					break;
				}
				case "--method":
				{
					JobOptions jobOptions = o;
					string text5 = Next().ToLowerInvariant();
					jobOptions.Method = text5 switch
					{
						"auto" => CopyMethod.Auto, 
						"pipeline" => CopyMethod.Pipeline, 
						"native" => CopyMethod.Native, 
						_ => throw new ArgumentException("unknown method '" + text5 + "'"), 
					};
					break;
				}
				case "--chunks":
					o.ChunkWorkers = Int();
					break;
				case "--retries":
					o.Retries = Int();
					break;
				case "--wait":
					o.ReconnectWaitSeconds = Int();
					break;
				case "--acl":
					o.CopyNtfsExtras = true;
					break;
				case "--split":
					o.SplitOversize = true;
					break;
				case "--no-flush":
					o.FlushToDisk = false;
					break;
				case "--no-dirtimes":
					o.PreserveDirTimes = false;
					break;
				case "--recycle":
					o.RecycleBin = true;
					break;
				case "--alias":
					lsAlias = Next();
					break;
				case "--hash-log":
					lsHashLog = true;
					break;
				case "--on-conflict":
					lsConflict = Next();
					break;
				case "--auto-accept":
					autoAccept = true;
					break;
				case "--listen":
					listenGiven = true;
					listen = Next();
					break;
				case "--port":
					portGiven = true;
					num = Int();
					break;
				case "--psk-file":
					pskFile = Next();
					break;
				case "--fingerprint":
					text3 = Next();
					break;
				case "--dry-run":
					o.DryRun = true;
					break;
				case "--vss":
					o.UseVss = true;
					break;
				case "--also":
					also.Add(Next());
					break;
				case "--usn":
					useUsn = true;
					break;
				case "--full-scan":
					fullScan = true;
					break;
				case "--autotune":
					o.AutoTune = true;
					break;
				case "--delta":
					o.DeltaUpdate = true;
					break;
				case "--delta-min-mb":
					o.DeltaMinMB = Int();
					break;
				case "--manifest":
					o.WriteManifest = true;
					break;
				case "--sign":
					o.ManifestSignKey = Next();
					o.WriteManifest = true;
					break;
				case "--pin":
					pin = Next();
					break;
				case "--extra":
					extra = true;
					break;
				case "--quiet-secs":
					quietSecs = Int();
					break;
				case "--mirror":
					flag4 = true;
					break;
				case "--yes":
				case "-y":
					flag3 = true;
					break;
				case "--quiet":
				case "-q":
					quiet = true;
					break;
				case "--size-mb":
					sizeMb = Int();
					sizeGiven = true;
					break;
				default:
					if (a.StartsWith("--"))
					{
						throw new ArgumentException("unknown option " + a);
					}
					list.Add(a);
					break;
				}
				int Int()
				{
					return int.Parse(Next(), CultureInfo.InvariantCulture);
				}
				string Next()
				{
					if (i + 1 >= args2.Length)
					{
						throw new ArgumentException(a + " needs a value");
					}
					return args2[++i];
				}
			}
			if (num2.HasValue)
			{
				o.ModifiedSinceUtc = DateTime.UtcNow.AddDays(0.0 - num2.Value);
			}
			switch (text)
			{
			case "history":
				return ShowHistory(list, search, clearHistory, flag3);
			case "hashmake":
			{
				if (list.Count != 1 || !Directory.Exists(list[0]))
				{
					throw new ArgumentException("hashmake needs one FOLDER");
				}
				string hf = (outFile.Length > 0) ? outFile : Path.Combine(list[0], "checksums.sha256");
				int cnt = await HashFile.MakeAsync(list[0], hf, o.Threads, quiet ? null : ((Action<string>)Console.WriteLine), Ctl.Token);
				Console.WriteLine($"Wrote {cnt:N0} checksum(s) to {hf}");
				return 0;
			}
			case "hashcheck":
			{
				if (list.Count != 1 || !File.Exists(list[0]))
				{
					throw new ArgumentException("hashcheck needs one checksum FILE");
				}
				string hroot = text2 ?? (Path.GetDirectoryName(Path.GetFullPath(list[0])) ?? ".");
				HashFileResult hr = await HashFile.VerifyAsync(list[0], hroot, o.Threads, quiet ? null : ((Action<string>)Console.WriteLine), Ctl.Token);
				foreach (string b in hr.Bad)
				{
					Console.WriteLine("  MISMATCH  " + b);
				}
				foreach (string m in hr.Missing)
				{
					Console.WriteLine("  MISSING   " + m);
				}
				Console.WriteLine($"Checked {hr.Checked:N0} file(s): {hr.Ok:N0} OK, {hr.Bad.Count:N0} mismatched, {hr.Missing.Count:N0} missing." + ((hr.Lines > hr.Checked) ? $" ({hr.Lines - hr.Checked:N0} line(s) not understood)" : ""));
				bool good = hr.Bad.Count == 0 && hr.Missing.Count == 0 && hr.Checked > 0;
				Console.WriteLine(good ? "RESULT: clean." : "RESULT: PROBLEMS FOUND.");
				return good ? 0 : 1;
			}
			case "lshistory":
			{
				int lsn = 20;
				if (list.Count > 0 && (!int.TryParse(list[0], out lsn) || lsn < 1))
				{
					throw new ArgumentException("lshistory: '" + list[0] + "' is not a number");
				}
				List<LsHistoryEntry> lsl = LsHistory.Read(lsn, search);
				if (lsl.Count == 0)
				{
					Console.WriteLine("No LocalSend transfers recorded.");
				}
				foreach (LsHistoryEntry le in lsl)
				{
					Console.WriteLine(LsHistory.Line(le));
				}
				return 0;
			}
			case "recheck":
				return await Recheck(list, quiet);
			case "wipe":
				if (list.Count == 0)
				{
					throw new ArgumentException("wipe needs at least one file or folder");
				}
				if (!flag3)
				{
					throw new ArgumentException("'wipe' permanently destroys files. Add --yes to confirm.");
				}
				{
					int wipeRc = 0;
					foreach (string wp in list)
					{
						WipeResult wr = SecureWipe.Run(wp, wipePasses, quiet ? null : ((Action<string>)Console.WriteLine), Ctl.Token);
						Console.WriteLine($"Wiped {wr.Files:N0} files ({wr.Bytes:N0} bytes) in {wp}; {wr.Failed:N0} failed.");
						if (wr.Failed > 0)
						{
							wipeRc = 1;
						}
					}
					Console.WriteLine("Note: on SSDs and USB flash drives, wear levelling can leave old copies. For those, use full-disk encryption or the drive's secure-erase.");
					return wipeRc;
				}
			case "localsend":
				if (list.Count != 1)
				{
					throw new ArgumentException("localsend needs exactly one FOLDER to receive into");
				}
				if (autoAccept && string.IsNullOrEmpty(pin))
				{
					throw new ArgumentException("--auto-accept needs a --pin, so that only people who know it can send");
				}
				return await LocalSendRun(list[0], listenGiven ? listen : "0.0.0.0", portGiven ? num : 53317, lsAlias, pin ?? "", autoAccept, lsConflict, lsHashLog);
			case "lsfind":
				return await LsFind();
			case "lssend":
				if (list.Count == 0 || text2 == null)
				{
					throw new ArgumentException("lssend needs SOURCE... and --to DEVICE (name from 'lsfind', or IP[:PORT])");
				}
				return await LsSend(list, text2, pin ?? "", lsAlias, quiet);
			case "protect":
				if (list.Count != 1)
				{
					throw new ArgumentException("protect needs exactly one FOLDER");
				}
				return ProtectFolder(list[0], parityPct > 0 ? parityPct : 5, quiet);
			case "repair":
				if (list.Count != 1)
				{
					throw new ArgumentException("repair needs exactly one FOLDER");
				}
				return RepairFolder(list[0], checkOnly, quiet);
			case "audit":
				if (list.Count != 1)
				{
					throw new ArgumentException("audit needs exactly one FOLDER");
				}
				return await Audit(list[0], o.Threads, extra, pin, quiet);
			case "fakecheck":
				if (list.Count != 1)
				{
					throw new ArgumentException("fakecheck needs exactly one FOLDER on the drive to test");
				}
				return FakeCheck(list[0], sizeGiven ? sizeMb : 0, quiet);
			case "bench":
				if (text2 == null)
				{
					throw new ArgumentException("bench needs --to DIR");
				}
				return await Bench(from, text2, sizeMb);
			case "receive":
				if (list.Count != 1)
				{
					throw new ArgumentException("receive needs exactly one FOLDER");
				}
				return await Receive(list[0], listen, num, pskFile);
			case "send":
				if (list.Count == 0 || text2 == null)
				{
					throw new ArgumentException("send needs SOURCE and --to HOST[:PORT]");
				}
				if (text3.Length == 0)
				{
					throw new ArgumentException("send needs --fingerprint (shown by the receiver)");
				}
				return await Send(list, text2, num, pskFile, text3);
			case "watch":
				if (list.Count == 0 || text2 == null)
				{
					throw new ArgumentException("watch needs SOURCE and --to DEST");
				}
				if (flag4 && !flag3)
				{
					throw new ArgumentException("--mirror deletes files in DEST. Add --yes to confirm.");
				}
				o.Sources = list;
				o.Destination = text2;
				o.Mode = (flag4 ? JobMode.Sync : JobMode.Copy);
				return await Watch(o, quietSecs, quiet);
			case "compare":
				if (list.Count != 1 || text2 == null || !Directory.Exists(list[0]))
				{
					throw new ArgumentException("compare needs one source FOLDER and --to DEST");
				}
				o.Mode = JobMode.Sync;
				o.DryRun = true;
				o.Sources = list;
				o.Destination = text2;
				return await RunJob(o, quiet, useUsn: false, fullScan: false, compare: true);
			default:
			{
				o.Mode = text switch
				{
					"copy" => JobMode.Copy, 
					"move" => JobMode.Move, 
					"sync" => JobMode.Sync, 
					"delete" => JobMode.Delete, 
					_ => throw new ArgumentException("unknown command '" + text + "'"), 
				};
				if (list.Count == 0)
				{
					throw new ArgumentException("no source given");
				}
				if (o.Mode != JobMode.Delete && text2 == null)
				{
					throw new ArgumentException("missing --to DEST");
				}
				JobMode mode = o.Mode;
				flag = (uint)(mode - 2) <= 1u;
				if (flag && !flag3)
				{
					throw new ArgumentException("'" + text + "' deletes files. Add --yes to confirm.");
				}
				o.Sources = list;
				o.Destination = text2 ?? "";
				break;
			}
			}
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is FormatException) ? 1 : 0) != 0)
		{
			Console.Error.WriteLine("cfcopy: " + ex.Message);
			Console.Error.WriteLine("Run 'cfcopy help' for usage.");
			return 2;
		}
		if (showRobo)
		{
			(List<string> rc, List<string> rn) = RoboCommand.Build(o);
			foreach (string c in rc)
			{
				Console.WriteLine(c);
			}
			foreach (string n in rn)
			{
				Console.WriteLine("REM " + n);
			}
			return 0;
		}
		int num3 = await RunJob(o, quiet, useUsn, fullScan, compare: false);
		foreach (string item in also)
		{
			if (!Ctl.IsCancellationRequested && num3 != 3)
			{
				Console.WriteLine("\n--- Also copying to " + item);
				JobOptions jobOptions2 = o.Clone();
				jobOptions2.Destination = item;
				int val = num3;
				num3 = Math.Max(val, await RunJob(jobOptions2, quiet, useUsn: false, fullScan: false, compare: false));
				continue;
			}
			break;
		}
		if (onFinish.Length > 0 && !o.DryRun)
		{
			PowerAction.RunHook(onFinish, num3, out var hookMsg);
			Console.WriteLine(hookMsg);
		}
		if (onError.Length > 0 && num3 != 0 && !o.DryRun)
		{
			PowerAction.RunHook(onError, num3, out var errHookMsg);
			Console.WriteLine(errHookMsg.Replace("--on-finish", "--on-error"));
		}
		if (parityPct > 0 && num3 == 0 && !o.DryRun && o.Mode != JobMode.Delete && o.Destination.Length > 0)
		{
			num3 = Math.Max(num3, ProtectFolder(o.Destination, parityPct, quiet));
		}
		if (afterAction.Length > 0 && num3 == 0 && !o.DryRun)
		{
			PowerAction.Run(afterAction, out var powerMsg);
			Console.WriteLine(powerMsg);
		}
		return num3;
	}

	private static async Task<int> LsFind()
	{
		Console.WriteLine("Looking for LocalSend devices (3 s)...");
		List<LsPeer> peers = await LocalSendClient.DiscoverAsync(TimeSpan.FromSeconds(3), Ctl.Token);
		if (peers.Count == 0)
		{
			Console.WriteLine("None found. Is LocalSend open on the other device, on the same network? Is UDP 53317 allowed in the firewall?");
			return 1;
		}
		foreach (LsPeer p in peers)
		{
			Console.WriteLine($"  {p.Alias,-30} {p.Ip}:{p.Port}  {p.DeviceType}");
		}
		return 0;
	}

	private static async Task<int> LsSend(List<string> sources, string to, string pin, string alias, bool quiet)
	{
		LsPeer? peer = null;
		string host = to;
		int port = 53317;
		int colon = to.LastIndexOf(':');
		if (colon > 0 && int.TryParse(to.Substring(colon + 1), out int pp) && IPAddress.TryParse(to.Substring(0, colon), out _))
		{
			host = to.Substring(0, colon);
			port = pp;
		}
		if (IPAddress.TryParse(host, out _))
		{
			peer = new LsPeer(host, host, port, "https", "", "");
			Console.WriteLine("NOTE: sending to an address you typed; the device's identity cannot be checked. Prefer the device name from 'lsfind'.");
		}
		else
		{
			List<LsPeer> peers = await LocalSendClient.DiscoverAsync(TimeSpan.FromSeconds(3), Ctl.Token);
			List<LsPeer> match = peers.Where(x => string.Equals(x.Alias, to, StringComparison.OrdinalIgnoreCase)).ToList();
			if (match.Count == 0)
			{
				match = peers.Where(x => x.Alias.Contains(to, StringComparison.OrdinalIgnoreCase)).ToList();
			}
			if (match.Count != 1)
			{
				Console.Error.WriteLine(match.Count == 0 ? ("cfcopy: no LocalSend device called '" + to + "' found (try 'cfcopy lsfind').") : ("cfcopy: '" + to + "' matches several devices: " + string.Join(", ", match.Select(x => x.Alias))));
				return 1;
			}
			peer = match[0];
		}
		if (alias.Length == 0)
		{
			alias = "CopyForge " + Environment.MachineName;
		}
		LsSendResult r = await LocalSendClient.SendAsync(peer, alias, pin, sources, quiet ? null : ((Action<string>)Console.WriteLine), null, Ctl.Token);
		Console.WriteLine(r.Message);
		return r.Ok ? 0 : 1;
	}

	private static readonly object PromptLock = new object();

	private static async Task<int> LocalSendRun(string folder, string listen, int port, string alias, string pin, bool autoAccept, string conflict = "rename", bool hashLog = false)
	{
		if (!IPAddress.TryParse(listen, out IPAddress address))
		{
			throw new ArgumentException("bad --listen address");
		}
		if (alias.Length == 0)
		{
			alias = "CopyForge " + Environment.MachineName;
		}
		LocalSendReceiver rcv = new LocalSendReceiver(folder, alias, pin, delegate(string who, string ip, int count, long bytes)
		{
			if (autoAccept)
			{
				return true;
			}
			lock (PromptLock)
			{
				Console.Write($"\n{who} ({ip}) wants to send {count:N0} file(s), {bytes / 1048576.0:N1} MB, into {folder}. Accept? [y/N] ");
				string ans = Console.ReadLine() ?? "";
				return ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
			}
		}, address, port, Path.Combine(AgentDir, "localsend-cert.pfx"), discovery: true);
		rcv.Conflict = LocalSendReceiver.ParseConflict(conflict);
		rcv.HashLog = hashLog;
		rcv.Log += delegate(string m)
		{
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");
		};
		try
		{
			rcv.Start();
		}
		catch (SocketException ex)
		{
			Console.Error.WriteLine("cfcopy: cannot listen on port " + port + ": " + ex.Message + " (is the LocalSend app running on this PC? close it first)");
			return 1;
		}
		Console.WriteLine($"Receiving LocalSend transfers into {Path.GetFullPath(folder)} as '{alias}' on port {rcv.Port}. Ctrl+C to stop.");
		Console.WriteLine(pin.Length > 0 ? "Senders must enter the PIN." : "No PIN: you will be asked about every transfer.");
		if (!address.Equals(IPAddress.Loopback))
		{
			Console.WriteLine("NOTE: listening on the network. Allow the port in the Windows firewall for PRIVATE networks only.");
		}
		await rcv.RunAsync(Ctl.Token);
		return 0;
	}

	private static int ProtectFolder(string folder, int pct, bool quiet)
	{
		if (!Directory.Exists(folder))
		{
			Console.Error.WriteLine("cfcopy: folder not found: " + folder);
			return 2;
		}
		try
		{
			ParityResult r = Parity.Protect(folder, pct, quiet ? null : ((Action<string>)Console.WriteLine), Ctl.Token);
			Console.WriteLine($"Repair data: {r.Written:N0} written, {r.Skipped:N0} already current, {r.Problems.Count:N0} failed (in {Parity.DirName}).");
			foreach (string p in r.Problems)
			{
				Console.Error.WriteLine("  " + p);
			}
			return (r.Problems.Count > 0) ? 1 : 0;
		}
		catch (OperationCanceledException)
		{
			return 3;
		}
	}

	private static int RepairFolder(string folder, bool check, bool quiet)
	{
		if (!Directory.Exists(folder))
		{
			Console.Error.WriteLine("cfcopy: folder not found: " + folder);
			return 2;
		}
		try
		{
			ParityResult r = Parity.Repair(folder, check, quiet ? null : ((Action<string>)Console.WriteLine), Ctl.Token);
			Console.WriteLine($"{r.Files:N0} files checked; {r.Damaged:N0} damaged, {r.Repaired:N0} repaired, {r.Unrepairable:N0} could not be repaired, {r.NoParity:N0} without repair data.");
			foreach (string p in r.Problems)
			{
				Console.Error.WriteLine("  " + p);
			}
			return (r.Unrepairable > 0 || (check && r.Damaged > 0)) ? 1 : 0;
		}
		catch (OperationCanceledException)
		{
			return 3;
		}
	}

	private static async Task<int> Recheck(List<string> args, bool quiet)
	{
		int n = 1;
		if (args.Count > 0 && (!int.TryParse(args[0], out n) || n < 1))
		{
			throw new ArgumentException("recheck: '" + args[0] + "' is not a job number (1 = the most recent)");
		}
		List<HistoryEntry> all = JobHistory.Read(n);
		if (all.Count < n)
		{
			Console.WriteLine("The history has only " + all.Count + " job(s).");
			return 2;
		}
		HistoryEntry e = all[n - 1];
		if (e.Mode.Equals("delete", StringComparison.OrdinalIgnoreCase) || e.Destination.Length == 0)
		{
			Console.WriteLine("That job (" + e.Mode + ") has no destination to re-check.");
			return 2;
		}
		if (e.Mode.Equals("move", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("That was a move job: the sources no longer exist, so there is nothing to compare. Use 'cfcopy audit' if it wrote a manifest.");
			return 2;
		}
		List<string> present = e.Sources.Where((string s) => File.Exists(s) || Directory.Exists(s)).ToList();
		if (present.Count == 0)
		{
			Console.WriteLine("None of that job's sources exist any more.");
			return 2;
		}
		Console.WriteLine($"Re-checking job of {e.Started:yyyy-MM-dd HH:mm}: {present.Count} source(s) -> {e.Destination}");
		JobOptions o = new JobOptions();
		o.Mode = JobMode.Copy;
		o.DryRun = true;
		o.Sources = present;
		o.Destination = e.Destination;
		int rc = await RunJob(o, quiet, useUsn: false, fullScan: false, compare: true);
		foreach (string extra in e.ExtraDestinations)
		{
			Console.WriteLine("\n--- Also " + extra);
			JobOptions o2 = o.Clone();
			o2.Destination = extra;
			rc = Math.Max(rc, await RunJob(o2, quiet, useUsn: false, fullScan: false, compare: true));
		}
		return rc;
	}

	private static int ShowHistory(List<string> args, string search, bool clear, bool yes)
	{
		if (clear)
		{
			if (!yes)
			{
				throw new ArgumentException("--clear erases the whole history. Add --yes to confirm.");
			}
			JobHistory.Clear();
			Console.WriteLine("History cleared.");
			return 0;
		}
		int n = 20;
		if (args.Count > 0 && (!int.TryParse(args[0], out n) || n < 1))
		{
			throw new ArgumentException("history: '" + args[0] + "' is not a number of jobs");
		}
		List<HistoryEntry> list = JobHistory.Read(n, search);
		if (list.Count == 0)
		{
			Console.WriteLine("No jobs in the history" + (string.IsNullOrWhiteSpace(search) ? "." : " match that search.") + "  (" + JobHistory.FilePath + ")");
			return 0;
		}
		foreach (HistoryEntry e in list)
		{
			string dest = e.Destination + ((e.ExtraDestinations.Count > 0) ? $" (+{e.ExtraDestinations.Count})" : "");
			string src = (e.Sources.Count == 0) ? "?" : (e.Sources[0] + ((e.Sources.Count > 1) ? $" (+{e.Sources.Count - 1})" : ""));
			Console.WriteLine($"{e.Started:yyyy-MM-dd HH:mm}  {e.Outcome,-9} {e.Mode.ToUpperInvariant(),-6} {e.FilesProcessed,7:N0} files {Fmt(e.Bytes),10}  {e.FilesSkipped:N0} skipped  {e.FilesFailed:N0} failed  {TimeSpan.FromSeconds(e.ElapsedSeconds):hh\\:mm\\:ss}" + (e.Verify ? ("  [verify " + e.VerifyAlgo + "]") : "") + "  [" + e.Source + "]");
			Console.WriteLine("    " + src + "  ->  " + dest);
			if (!string.IsNullOrEmpty(e.Problem))
			{
				Console.WriteLine("    PROBLEM: " + e.Problem);
			}
			foreach (string err in e.Errors.Take(3))
			{
				Console.WriteLine("    ERROR: " + err);
			}
		}
		return 0;
	}

	private static async Task<int> RunJob(JobOptions o, bool quiet, bool useUsn, bool fullScan, bool compare)
	{
		UsnIncremental.Prepared usn = null;
		if (useUsn)
		{
			usn = UsnIncremental.Prepare(o, delegate(string m)
			{
				Console.WriteLine(m);
			}, fullScan);
			o = usn.Options;
		}
		CopyJob job = new CopyJob(o);
		job.Message += delegate(string m)
		{
			lock (Console.Out)
			{
				ClearLine();
				Console.WriteLine(m);
			}
		};
		Stopwatch.StartNew();
		Task<JobResult> run = job.RunAsync(Ctl.Token);
		Task.Run(async delegate
		{
			bool tty = !Console.IsOutputRedirected;
			while (!run.IsCompleted)
			{
				await Task.Delay(tty ? 500 : 5000);
				if (!quiet && !run.IsCompleted)
				{
					ProgressSnapshot snapshot = job.GetSnapshot();
					string text = $"{snapshot.Phase,-10} {snapshot.Fraction * 100.0,5:0.0}%  {Fmt(snapshot.BytesDone)}/{Fmt(snapshot.BytesTotal)}  {Fmt((long)snapshot.BytesPerSecond)}/s  files {snapshot.FilesDone}/{snapshot.FilesTotal}  failed {snapshot.FilesFailed}";
					lock (Console.Out)
					{
						if (tty)
						{
							Console.Write("\r" + text.PadRight((Console.WindowWidth > 1) ? (Console.WindowWidth - 1) : text.Length));
						}
						else
						{
							Console.WriteLine(text);
						}
					}
				}
			}
		});
		JobResult jobResult;
		try
		{
			jobResult = await run;
		}
		catch (Exception ex)
		{
			ClearLine();
			Console.Error.WriteLine("cfcopy: " + ex.Message);
			return 2;
		}
		ClearLine();
		usn?.Commit(jobResult);
		if (jobResult.Preview == null)
		{
			JobHistory.Append(new JobRecord(DateTime.Now - jobResult.Elapsed, o, jobResult, null), "cli");
		}
		if (jobResult.Preview == null)
		{
			Console.WriteLine($"{(jobResult.Cancelled ? "CANCELLED" : ((jobResult.FilesFailed > 0) ? "DONE WITH ERRORS" : "DONE"))}: {jobResult.FilesProcessed} files, {Fmt(jobResult.BytesTransferred)}, {jobResult.FilesSkipped} skipped, {jobResult.FilesDeleted} deleted, {jobResult.FilesFailed} failed, {jobResult.Elapsed:hh\\:mm\\:ss}" + ((jobResult.Elapsed.TotalSeconds > 0.0 && jobResult.BytesTransferred > 0) ? (", avg " + Fmt((long)((double)jobResult.BytesTransferred / jobResult.Elapsed.TotalSeconds)) + "/s") : ""));
		}
		foreach (string item in jobResult.Errors.Take(20))
		{
			Console.Error.WriteLine("  ERROR: " + item);
		}
		if (jobResult.Errors.Count > 20)
		{
			Console.Error.WriteLine($"  ... and {jobResult.Errors.Count - 20} more");
		}
		PlanPreview preview = jobResult.Preview;
		if ((object)preview != null)
		{
			foreach (string item2 in preview.CopyList.Take(200))
			{
				Console.WriteLine((compare ? "  DIFFERENT/NEW  " : "  WOULD COPY     ") + item2);
			}
			foreach (string item3 in preview.DeleteList.Take(200))
			{
				Console.WriteLine((compare ? "  ONLY IN DEST   " : "  WOULD DELETE   ") + item3);
			}
			if (preview.CopyList.Count > 200 || preview.DeleteList.Count > 200 || preview.Truncated)
			{
				Console.WriteLine("  ... list shortened (counts above are complete)");
			}
			Console.WriteLine($"PREVIEW: {preview.CopyCount:N0} to copy ({Fmt(preview.CopyBytes)}), {preview.SkipCount:N0} unchanged, {preview.DeleteCount:N0} to delete. Nothing was changed.");
			if (compare)
			{
				Console.WriteLine((preview.CopyCount + preview.DeleteCount == 0L) ? "RESULT: identical." : "RESULT: the folders differ.");
				return (preview.CopyCount + preview.DeleteCount != 0) ? 1 : 0;
			}
		}
		return jobResult.Cancelled ? 3 : ((jobResult.FilesFailed > 0) ? 1 : 0);
	}

	private static void ClearLine()
	{
		if (!Console.IsOutputRedirected)
		{
			try
			{
				Console.Write("\r" + new string(' ', Math.Max(Console.WindowWidth - 1, 1)) + "\r");
			}
			catch
			{
			}
		}
	}

	private static string Fmt(long b)
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
		return $"{num:0.0} {array[num2]}";
	}

	private static int FakeCheck(string folder, int maxMb, bool quiet)
	{
		using CancellationTokenSource cts = new CancellationTokenSource();
		Console.CancelKeyPress += delegate(object? _, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
			cts.Cancel();
		};
		try
		{
			CapacityResult capacityResult = CapacityTest.Run(folder, (long)maxMb * 1048576L, quiet ? null : new Action<string>(Console.WriteLine), cts.Token);
			Console.WriteLine(capacityResult.Summary);
			return capacityResult.Passed ? 0 : 1;
		}
		catch (OperationCanceledException)
		{
			Console.WriteLine("Capacity test cancelled; test files removed.");
			return 3;
		}
	}

	private static async Task<int> Bench(string? from, string to, int sizeMb)
	{
		if (from == null)
		{
			from = Path.GetTempPath();
		}
		Directory.CreateDirectory(from);
		Directory.CreateDirectory(to);
		string work = Path.Combine(from, "cf_bench_src_" + Guid.NewGuid().ToString("N").Substring(0, 6));
		string outDir = Path.Combine(to, "cf_bench_dst_" + Guid.NewGuid().ToString("N").Substring(0, 6));
		Directory.CreateDirectory(work);
		string file = Path.Combine(work, "bench.bin");
		Console.WriteLine($"Benchmark: {sizeMb} MB file   from {from}   to {to}");
		Console.WriteLine("Creating test file...");
		Random random = new Random(1);
		byte[] array = new byte[4194304];
		using (FileStream fileStream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
		{
			for (long num = 0L; num < (long)sizeMb * 1024L * 1024; num += array.Length)
			{
				random.NextBytes(array);
				fileStream.Write(array, 0, (int)Math.Min(array.Length, (long)sizeMb * 1024L * 1024 - num));
			}
		}
		(string, Action<JobOptions>)[] array2 = new(string, Action<JobOptions>)[8]
		{
			("pipeline, cached I/O, 4 MB", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Pipeline;
				o.Io = IoMode.Buffered;
				o.ChunkWorkers = 1;
			}),
			("pipeline, direct I/O, 4 MB", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Pipeline;
				o.Io = IoMode.Direct;
				o.ChunkWorkers = 1;
			}),
			("pipeline, direct I/O, 16 MB", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Pipeline;
				o.Io = IoMode.Direct;
				o.ChunkWorkers = 1;
				o.BufferSizeMB = 16;
			}),
			("direct I/O, 3 parallel chunks", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Pipeline;
				o.Io = IoMode.Direct;
				o.ChunkWorkers = 3;
			}),
			("direct I/O, 6 parallel chunks", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Pipeline;
				o.Io = IoMode.Direct;
				o.ChunkWorkers = 6;
			}),
			("windows native (CopyFileEx)", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Native;
				o.Io = IoMode.Buffered;
			}),
			("windows native, no buffering", delegate(JobOptions o)
			{
				o.Method = CopyMethod.Native;
				o.Io = IoMode.Direct;
			}),
			("AUTO (what the app would pick)", delegate
			{
			})
		};
		Console.WriteLine();
		Console.WriteLine($"{"strategy",-36} {"time",8} {"speed",12}");
		try
		{
			(string Name, Action<JobOptions> Set)[] array3 = array2;
			for (int i = 0; i < array3.Length; i++)
			{
				var (name, action) = array3[i];
				if (!OperatingSystem.IsWindows() && (name.Contains("native") || name.Contains("direct")))
				{
					Console.WriteLine($"{name,-36} skipped (Windows only)");
					continue;
				}
				if (Directory.Exists(outDir))
				{
					Directory.Delete(outDir, recursive: true);
				}
				JobOptions jobOptions = new JobOptions
				{
					Sources = { file },
					Destination = outDir,
					Overwrite = OverwriteMode.Always,
					Threads = 1
				};
				action(jobOptions);
				try
				{
					JobResult jobResult = await new CopyJob(jobOptions).RunAsync(CancellationToken.None);
					if (jobResult.FilesFailed > 0)
					{
						Console.WriteLine($"{name,-36} failed: {jobResult.Errors.FirstOrDefault()}");
					}
					else
					{
						Console.WriteLine($"{name,-36} {jobResult.Elapsed.TotalSeconds,7:0.0}s {Fmt((long)((double)sizeMb * 1048576.0 / jobResult.Elapsed.TotalSeconds)),10}/s");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"{name,-36} skipped: {ex.Message}");
				}
			}
		}
		finally
		{
			try
			{
				Directory.Delete(work, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (Directory.Exists(outDir))
				{
					Directory.Delete(outDir, recursive: true);
				}
			}
			catch
			{
			}
		}
		Console.WriteLine();
		Console.WriteLine("Notes: cached-I/O results can look faster after the source file is in memory; native entries need Windows.");
		Console.WriteLine("Use a file larger than your RAM cache effects (e.g. --size-mb 4096) for the most honest numbers.");
		return 0;
	}
}
