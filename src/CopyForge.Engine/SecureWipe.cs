using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace CopyForge.Engine;

public sealed record WipeResult(int Files, int Failed, long Bytes, IReadOnlyList<string> Errors);

/// <summary>Overwrites files with random data, then renames and deletes them.
/// Honest limits: on SSDs / USB flash (wear levelling), journaling or copy-on-write file systems,
/// cloud-synced folders and shadow copies, old data can survive. Full-disk encryption or a drive-level secure erase is the reliable way.</summary>
public static class SecureWipe
{
	public static WipeResult Run(string path, int passes, Action<string>? log, CancellationToken ct)
	{
		passes = Math.Clamp(passes, 1, 7);
		int files = 0;
		int failed = 0;
		long bytes = 0L;
		List<string> errors = new List<string>();
		string full = Path.GetFullPath(path);
		List<string> list = new List<string>();
		if (File.Exists(full))
		{
			list.Add(full);
		}
		else if (Directory.Exists(full))
		{
			EnumerationOptions eo = new EnumerationOptions
			{
				RecurseSubdirectories = true,
				AttributesToSkip = FileAttributes.None,
				IgnoreInaccessible = true
			};
			foreach (string f in Directory.EnumerateFiles(full, "*", eo))
			{
				list.Add(f);
			}
		}
		else
		{
			throw new FileNotFoundException("Nothing to wipe at " + full);
		}
		byte[] buf = new byte[1048576];
		foreach (string f2 in list)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				FileInfo fi = new FileInfo(f2);
				if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					fi.Delete();
					continue;
				}
				long len = fi.Length;
				fi.Attributes = FileAttributes.Normal;
				using (FileStream fs = new FileStream(f2, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
				{
					for (int p = 0; p < passes; p++)
					{
						fs.Position = 0L;
						long left = len;
						while (left > 0)
						{
							ct.ThrowIfCancellationRequested();
							int n = (int)Math.Min(buf.Length, left);
							RandomNumberGenerator.Fill(buf.AsSpan(0, n));
							fs.Write(buf, 0, n);
							left -= n;
						}
						fs.Flush(flushToDisk: true);
					}
					fs.SetLength(0L);
				}
				string dir = Path.GetDirectoryName(f2)!;
				string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N"));
				File.Move(f2, tmp);
				File.Delete(tmp);
				files++;
				bytes += len;
				log?.Invoke("wiped " + f2);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				failed++;
				errors.Add(f2 + ": " + ex.Message);
				log?.Invoke("FAILED " + f2 + ": " + ex.Message);
			}
		}
		if (Directory.Exists(full))
		{
			try
			{
				RemoveEmptyDirs(full);
			}
			catch
			{
			}
		}
		return new WipeResult(files, failed, bytes, errors);
	}

	private static void RemoveEmptyDirs(string root)
	{
		foreach (string d in Directory.GetDirectories(root))
		{
			if (!new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint))
			{
				RemoveEmptyDirs(d);
			}
		}
		if (Directory.GetFileSystemEntries(root).Length == 0)
		{
			Directory.Delete(root);
		}
	}
}

/// <summary>What to do with the PC when a job has finished without errors.</summary>
public static class PowerAction
{
	/// <summary>Runs a script or program the user named with --on-finish (no shell; the result code goes in CF_EXITCODE).</summary>
	public static bool RunHook(string path, int exitCode, out string message)
	{
		message = "";
		try
		{
			if (!Path.IsPathRooted(path) || !File.Exists(path))
			{
				message = "--on-finish: give the full path of an existing file (" + path + ").";
				return false;
			}
			string ext = Path.GetExtension(path).ToLowerInvariant();
			System.Diagnostics.ProcessStartInfo psi;
			if (ext == ".ps1")
			{
				string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
				psi = new System.Diagnostics.ProcessStartInfo(ps);
				psi.ArgumentList.Add("-NoProfile");
				psi.ArgumentList.Add("-File");
				psi.ArgumentList.Add(path);
			}
			else if (ext == ".cmd" || ext == ".bat")
			{
				psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
				psi.ArgumentList.Add("/c");
				psi.ArgumentList.Add(path);
			}
			else if (ext == ".exe")
			{
				psi = new System.Diagnostics.ProcessStartInfo(path);
			}
			else
			{
				message = "--on-finish: only .ps1, .cmd, .bat and .exe files are run.";
				return false;
			}
			psi.UseShellExecute = false;
			psi.CreateNoWindow = true;
			psi.Environment["CF_EXITCODE"] = exitCode.ToString();
			using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
			if (!p.WaitForExit(300000))
			{
				p.Kill(entireProcessTree: true);
				message = "--on-finish script was stopped after 5 minutes.";
				return false;
			}
			message = "--on-finish script finished with code " + p.ExitCode + ".";
			return p.ExitCode == 0;
		}
		catch (Exception ex)
		{
			message = "--on-finish failed: " + ex.Message;
			return false;
		}
	}

	public static bool Run(string action, out string message)
	{
		message = "";
		if (!OperatingSystem.IsWindows())
		{
			message = "Shut down / sleep is only supported on Windows.";
			return false;
		}
		string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
		try
		{
			switch (action.ToLowerInvariant())
			{
			case "shutdown":
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(sys, "shutdown.exe"), "/s /t 60 /c \"CopyForge: copy finished. Shutting down in 60 seconds. Run 'shutdown /a' to cancel.\"")
				{
					CreateNoWindow = true,
					UseShellExecute = false
				});
				message = "PC will shut down in 60 seconds. To cancel: open a command prompt and run  shutdown /a";
				return true;
			case "sleep":
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(sys, "rundll32.exe"), "powrprof.dll,SetSuspendState 0,1,0")
				{
					CreateNoWindow = true,
					UseShellExecute = false
				});
				message = "Putting the PC to sleep.";
				return true;
			}
		}
		catch (Exception ex)
		{
			message = "Could not run the power action: " + ex.Message;
			return false;
		}
		message = "Unknown action '" + action + "' (use shutdown or sleep).";
		return false;
	}
}
