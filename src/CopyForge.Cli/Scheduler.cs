using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CopyForge.Cli;

/// <summary>Creates, lists and removes Windows scheduled tasks that run a saved CopyForge job (via schtasks.exe).
/// Tasks run as the current user, only while that user is logged on (no password is stored).</summary>
internal static class Scheduler
{
	private static readonly Regex NameRx = new Regex("^[A-Za-z0-9][A-Za-z0-9 _-]{0,39}$");

	private static readonly Regex TimeRx = new Regex("^([01]?[0-9]|2[0-3]):[0-5][0-9]$");

	private static readonly string[] Days = new string[7] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };

	private const string Folder = "CopyForge";

	public static string JobsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "jobs");

	private static string Quote(string a)
	{
		return (a.Length == 0 || a.Any(char.IsWhiteSpace)) ? ("\"" + a + "\"") : a;
	}

	private static int Schtasks(string args, out string output)
	{
		string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
		ProcessStartInfo psi = new ProcessStartInfo(exe, args)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		using Process p = Process.Start(psi);
		string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
		p.WaitForExit();
		output = o.Trim();
		return p.ExitCode;
	}

	public static int Run(string[] args)
	{
		if (!OperatingSystem.IsWindows())
		{
			Console.Error.WriteLine("cfcopy: scheduling uses Windows Task Scheduler and only works on Windows.");
			return 2;
		}
		string sub = (args.Length > 1) ? args[1].ToLowerInvariant() : "";
		switch (sub)
		{
		case "list":
		{
			int rc = Schtasks("/Query /FO LIST /TN \\" + Folder + "\\", out var o);
			Console.WriteLine((rc == 0) ? o : "No CopyForge scheduled tasks.");
			if (Directory.Exists(JobsDir))
			{
				foreach (string f in Directory.GetFiles(JobsDir, "*.cfjob"))
				{
					Console.WriteLine(Path.GetFileNameWithoutExtension(f) + ":  " + File.ReadAllText(f).Trim());
				}
			}
			return 0;
		}
		case "remove":
		{
			if (args.Length < 3 || !NameRx.IsMatch(args[2]))
			{
				Console.Error.WriteLine("cfcopy: schedule remove NAME");
				return 2;
			}
			int rc2 = Schtasks("/Delete /F /TN \"\\" + Folder + "\\" + args[2] + "\"", out var o2);
			string jf = Path.Combine(JobsDir, args[2] + ".cfjob");
			if (File.Exists(jf))
			{
				File.Delete(jf);
			}
			Console.WriteLine((rc2 == 0) ? ("Removed scheduled task " + args[2] + ".") : o2);
			return (rc2 == 0) ? 0 : 1;
		}
		case "add":
			return Add(args);
		default:
			Console.Error.WriteLine("cfcopy: schedule add NAME --daily HH:MM | --weekly DAY HH:MM | --logon  -- COMMAND...\n        e.g.  cfcopy schedule add nightly --daily 22:30 -- copy D:\\Data --to E:\\Backup --verify --log C:\\Logs\\nightly.csv\n        schedule list | schedule remove NAME");
			return 2;
		}
	}

	private static int Add(string[] args)
	{
		if (args.Length < 4 || !NameRx.IsMatch(args[2]))
		{
			Console.Error.WriteLine("cfcopy: schedule add NAME (letters, digits, space, _ or -; up to 40) ...");
			return 2;
		}
		string name = args[2];
		int dash = Array.IndexOf(args, "--");
		if (dash < 0 || dash == args.Length - 1)
		{
			Console.Error.WriteLine("cfcopy: put the job after '--', e.g.  -- copy D:\\Data --to E:\\Backup");
			return 2;
		}
		string[] when = args.Skip(3).Take(dash - 3).ToArray();
		string sc;
		if (when.Length == 2 && when[0] == "--daily" && TimeRx.IsMatch(when[1]))
		{
			sc = "/SC DAILY /ST " + when[1].PadLeft(5, '0');
		}
		else if (when.Length == 3 && when[0] == "--weekly" && Days.Contains(when[1].ToUpperInvariant()) && TimeRx.IsMatch(when[2]))
		{
			sc = "/SC WEEKLY /D " + when[1].ToUpperInvariant() + " /ST " + when[2].PadLeft(5, '0');
		}
		else if (when.Length == 1 && when[0] == "--logon")
		{
			sc = "/SC ONLOGON";
		}
		else
		{
			Console.Error.WriteLine("cfcopy: when? use --daily HH:MM, --weekly MON..SUN HH:MM or --logon");
			return 2;
		}
		string[] job = args.Skip(dash + 1).ToArray();
		string verb = job[0].ToLowerInvariant();
		if (verb != "copy" && verb != "move" && verb != "sync" && verb != "watch" && verb != "audit" && verb != "repair" && verb != "protect")
		{
			Console.Error.WriteLine("cfcopy: scheduled jobs can be copy, move, sync, watch, audit, protect or repair (not delete or wipe).");
			return 2;
		}
		if (job.Any((string a) => a.Contains('"')))
		{
			Console.Error.WriteLine("cfcopy: quote characters are not allowed inside a scheduled job.");
			return 2;
		}
		if (job.Any((string a) => a == "--after"))
		{
			Console.Error.WriteLine("cfcopy: --after (shut down / sleep) is not allowed in a scheduled job.");
			return 2;
		}
		Directory.CreateDirectory(JobsDir);
		string file = Path.Combine(JobsDir, name + ".cfjob");
		File.WriteAllText(file, string.Join(" ", job.Select(Quote)) + Environment.NewLine);
		string self = Environment.ProcessPath ?? "cfcopy.exe";
		string tr = "\\\"" + self + "\\\" run \\\"" + file + "\\\"";
		int rc3 = Schtasks("/Create /F /TN \"\\" + Folder + "\\" + name + "\" " + sc + " /TR \"" + tr + "\"", out var o3);
		Console.WriteLine((rc3 == 0) ? ("Scheduled '" + name + "'. It runs while you are logged on. Job file: " + file) : ("Could not create the task: " + o3));
		return (rc3 == 0) ? 0 : 1;
	}
}
