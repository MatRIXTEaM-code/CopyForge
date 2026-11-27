using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CopyForge.Engine;

/// <summary>Shows roughly what a job would look like as robocopy commands, for admins who want to compare or script it.</summary>
public static class RoboCommand
{
	public static (List<string> Commands, List<string> Notes) Build(JobOptions o)
	{
		List<string> cmds = new List<string>();
		List<string> notes = new List<string>();
		if (o.Mode == JobMode.Delete)
		{
			notes.Add("Delete jobs have no robocopy equivalent.");
			return (cmds, notes);
		}
		List<string> flags = new List<string>();
		switch (o.Overwrite)
		{
		case OverwriteMode.Skip:
			flags.Add("/XC /XN /XO");
			break;
		case OverwriteMode.IfNewer:
			flags.Add("/XO");
			break;
		case OverwriteMode.Always:
			flags.Add("/IS /IT");
			break;
		}
		flags.Add("/MT:" + ((o.Threads > 0) ? Math.Clamp(o.Threads, 1, 128) : 16));
		flags.Add("/R:" + o.Retries + " /W:" + o.RetryWaitSeconds);
		flags.Add(o.CopyNtfsExtras ? "/COPYALL /DCOPY:DAT" : "/COPY:DAT /DCOPY:DAT");
		if (o.MinSizeBytes > 0)
		{
			flags.Add("/MIN:" + o.MinSizeBytes);
		}
		if (o.MaxSizeBytes > 0)
		{
			flags.Add("/MAX:" + o.MaxSizeBytes);
		}
		if (o.ModifiedSinceUtc.HasValue)
		{
			flags.Add("/MAXAGE:" + Math.Max(1, (int)Math.Ceiling((DateTime.UtcNow - o.ModifiedSinceUtc.Value).TotalDays)));
		}
		string[] ex = o.Exclude.Split(new char[2] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string[] xf = ex.Where((string x) => !x.EndsWith("/")).ToArray();
		string[] xd = ex.Where((string x) => x.EndsWith("/")).Select((string x) => x.TrimEnd('/')).ToArray();
		if (xf.Length > 0)
		{
			flags.Add("/XF " + string.Join(" ", xf.Select(Q)));
		}
		if (xd.Length > 0)
		{
			flags.Add("/XD " + string.Join(" ", xd.Select(Q)));
		}
		string[] inc = o.Include.Split(new char[2] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string patterns = (inc.Length > 0) ? (" " + string.Join(" ", inc.Select(Q))) : "";
		foreach (string src in o.Sources)
		{
			string s = src.TrimEnd('\\', '/');
			bool isFile = File.Exists(src) || (!Directory.Exists(src) && Path.HasExtension(s));
			string cmd;
			if (isFile)
			{
				string dir = Path.GetDirectoryName(s) ?? s;
				cmd = "robocopy " + Q(dir) + " " + Q(o.Destination) + " " + Q(Path.GetFileName(s)) + " " + string.Join(" ", flags) + ((o.Mode == JobMode.Move) ? " /MOV" : "");
			}
			else
			{
				string dst = ((o.Mode == JobMode.Sync) ? o.Destination : Path.Combine(o.Destination, Path.GetFileName(s)));
				cmd = "robocopy " + Q(s) + " " + Q(dst) + patterns + " " + ((o.Mode == JobMode.Sync) ? "/MIR" : ((o.Mode == JobMode.Move) ? "/MOVE /E" : "/E")) + " " + string.Join(" ", flags);
			}
			cmds.Add(cmd);
		}
		if (o.Verify)
		{
			notes.Add("Verify: robocopy has no read-back verification (use cfcopy compare / audit).");
		}
		if (o.KeepVersions)
		{
			notes.Add("Versioned replace (--versions) has no robocopy equivalent.");
		}
		if (o.DeltaUpdate)
		{
			notes.Add("Delta updates have no robocopy equivalent (robocopy re-copies a changed file in full).");
		}
		if (o.LimitMBps > 0)
		{
			notes.Add("Speed limit: robocopy only has /IPG (a gap between packets), not a MB/s limit.");
		}
		if (o.WriteManifest || o.ManifestSignKey.Length > 0)
		{
			notes.Add("Manifest / signing has no robocopy equivalent.");
		}
		if (o.ExtraDestinations.Count > 0)
		{
			notes.Add("Extra destinations: run the command once per destination.");
		}
		if (o.ResumeMB > 0)
		{
			notes.Add("Resuming big interrupted files: robocopy needs /Z (slower) for that.");
		}
		return (cmds, notes);
	}

	private static string Q(string s)
	{
		return (s.IndexOfAny(new char[3] { ' ', '&', '(' }) >= 0 || s.Length == 0) ? ("\"" + s + "\"") : s;
	}
}
