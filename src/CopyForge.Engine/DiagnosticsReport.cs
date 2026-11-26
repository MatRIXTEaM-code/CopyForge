using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace CopyForge.Engine;

public static class DiagnosticsReport
{
	public static DiagnosticsResult Build(DiagnosticsInput d)
	{
		StringBuilder sb = new StringBuilder();
		int fails = 0;
		int warns = 0;
		JobOptions current = d.Current;
		long num = 0L;
		try
		{
			num = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		}
		catch
		{
		}
		Sec("System");
		Kv("CopyForge", d.AppVersion);
		Kv("Engine", typeof(DiagnosticsReport).Assembly.GetName().Version?.ToString() ?? "?");
		Kv("Runtime", RuntimeInformation.FrameworkDescription);
		Kv("OS", RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture.ToString() + ")");
		Kv("Process", (Environment.Is64BitProcess ? "64-bit" : "32-bit") + ", " + RuntimeInformation.ProcessArchitecture);
		Kv("Logical cores", Environment.ProcessorCount.ToString());
		Kv("Memory (available)", (num > 0) ? FormatBytes(num) : "unknown");
		Kv("Process memory", FormatBytes(Environment.WorkingSet));
		Kv("Time zone", TimeZoneInfo.Local.DisplayName);
		Kv("Culture", CultureInfo.CurrentUICulture.Name);
		Kv("System uptime", TimeSpan.FromMilliseconds((double)Environment.TickCount64).ToString("d\\.hh\\:mm\\:ss"));
		try
		{
			if (OperatingSystem.IsWindows())
			{
				bool flag = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
				Kv("Elevated", flag ? "yes (administrator)" : "no");
			}
		}
		catch
		{
		}
		string text = null;
		bool flag2 = false;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = (from s in current.Sources
			where !string.IsNullOrWhiteSpace(s)
			select s.Trim().Trim('"')).ToList();
		foreach (string item in list)
		{
			string text2 = RootOf(item);
			if (text2 != null)
			{
				hashSet.Add(text2);
			}
		}
		if (!string.IsNullOrWhiteSpace(current.Destination))
		{
			text = RootOf(current.Destination.Trim().Trim('"'));
		}
		Sec("Drives");
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				string text3 = "";
				if (hashSet.Contains(driveInfo.Name))
				{
					text3 += "  <- source";
				}
				if (text != null && string.Equals(text, driveInfo.Name, StringComparison.OrdinalIgnoreCase))
				{
					text3 += "  <- destination";
				}
				try
				{
					if (!driveInfo.IsReady)
					{
						Line("INFO", $"{driveInfo.Name}  {driveInfo.DriveType}  not ready{text3}");
						continue;
					}
					long totalSize = driveInfo.TotalSize;
					long availableFreeSpace = driveInfo.AvailableFreeSpace;
					if (totalSize != 0L || text3.Length != 0)
					{
						double num2 = ((totalSize > 0) ? (100.0 * (double)availableFreeSpace / (double)totalSize) : 0.0);
						string value = (string.IsNullOrEmpty(driveInfo.VolumeLabel) ? "" : ("  \"" + driveInfo.VolumeLabel + "\""));
						string t2 = $"{driveInfo.Name}  {driveInfo.DriveType}  {driveInfo.DriveFormat}  {FormatBytes(totalSize)} total, {FormatBytes(availableFreeSpace)} free ({num2:0}%){value}{text3}";
						long spaceNeeded = d.LastJob?.Result?.Preview?.CopyBytes ?? d.LastJob?.Result?.BytesTransferred ?? 0L;
						bool veryLow = totalSize > 0 && availableFreeSpace < 1073741824L;
						bool tooSmall = totalSize > 0 && text3.Contains("destination") && spaceNeeded > 0 && availableFreeSpace < spaceNeeded;
						if (tooSmall)
						{
							t2 += "  <- less free space than the last job needed (" + FormatBytes(spaceNeeded) + ")";
						}
						else if (veryLow)
						{
							t2 += "  <- under 1 GB free";
						}
						Line((veryLow || tooSmall) ? "WARN" : "INFO", t2);
						bool num3 = text3.Contains("destination");
						if (num3 && driveInfo.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
						{
							flag2 = true;
						}
						if (num3 && driveInfo.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
						{
							Line("INFO", "Destination is FAT32: files over 4 GB cannot be stored and timestamps are only 2 s accurate.");
						}
						if (text3.Length > 0)
						{
							BusInfo bus = Native.ProbeBus(driveInfo.Name);
							if (bus.IsUsb2OrOlder)
							{
								Line("WARN", $"{driveInfo.Name}  is connected over {bus.Describe()}: about 30-40 MB/s at best. Move it to a USB 3 (blue or SS) port for faster copies.");
							}
							else if (bus.IsUsb)
							{
								Line("INFO", driveInfo.Name + "  is connected over " + bus.Describe() + ".");
							}
						}
						if (text3.Contains("source") && num3 && driveInfo.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
						{
							Line("INFO", "Source and destination are on the same ReFS volume: Windows 11 24H2 / Server 2025 and later can block-clone supported copies (a metadata operation, no data re-written).");
						}
						if (num3 && driveInfo.DriveType == DriveType.Network)
						{
							Line("INFO", "Destination is a network drive: use 1-4 threads; verify re-reads over the network.");
						}
						if (num3 && driveInfo.DriveType == DriveType.Removable)
						{
							Line("INFO", "Destination is removable: speed is limited by the device; safely eject after the copy.");
						}
					}
				}
				catch (Exception ex)
				{
					Line("WARN", driveInfo.Name + "  could not be read: " + ex.Message + text3);
				}
			}
		}
		catch (Exception ex2)
		{
			Line("WARN", "Could not enumerate drives: " + ex2.Message);
		}
		Sec("Current settings and pre-flight checks");
		Kv("Mode", current.Mode.ToString());
		Kv("Overwrite", current.Overwrite.ToString());
		Kv("Verify", current.Verify ? ("on (" + VerifyAlgos.Name(current.VerifyAlgo) + ")") : "off");
		Kv("Block cloning", current.BlockClone ? "on (same-volume ReFS copies)" : "off");
		Kv("Eject when done", current.EjectAfter ? "on (removable destination, after a clean job)" : "off");
		Kv("Post-quantum crypto", PqcSupport());
		Kv("Direct write on USB", current.DirectWriteRemovable ? "on (small files skip the temp name + rename)" : "off (temp name + rename)");
		Kv("Threads / buffer", $"{((current.Threads == 0) ? "auto" : current.Threads.ToString())} / {current.BufferSizeMB} MB");
		Kv("I/O / method", $"{current.Io} / {current.Method}   chunk workers: {((current.ChunkWorkers == 0) ? "auto" : current.ChunkWorkers.ToString())}");
		Kv("Safety", $"flush to disk {(current.FlushToDisk ? "on" : "off")}, retries {current.Retries}, wait for reconnect {current.ReconnectWaitSeconds}s");
		Kv("Extras", $"NTFS permissions+streams {(current.CopyNtfsExtras ? "on" : "off")}, split >4 GB {(current.SplitOversize ? "on" : "off")}, folder times {(current.PreserveDirTimes ? "on" : "off")}");
		Kv("Include", string.IsNullOrWhiteSpace(current.Include) ? "(everything)" : current.Include);
		Kv("Exclude", string.IsNullOrWhiteSpace(current.Exclude) ? "(nothing)" : current.Exclude);
		Kv("Size limits", "min " + FormatBytes(current.MinSizeBytes) + ", max " + ((current.MaxSizeBytes > 0) ? FormatBytes(current.MaxSizeBytes) : "none"));
		Kv("Modified since", current.ModifiedSinceUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "(any)");
		sb.Append('\n');
		if (list.Count == 0)
		{
			Line("INFO", "No source selected.");
		}
		foreach (string item2 in list)
		{
			try
			{
				if (File.Exists(item2))
				{
					using FileStream fileStream = new FileStream(item2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					Line("OK", $"Source file readable: {item2}  ({FormatBytes(fileStream.Length)})");
				}
				else if (Directory.Exists(item2))
				{
					Directory.EnumerateFileSystemEntries(item2).FirstOrDefault();
					Line("OK", "Source folder readable: " + item2);
				}
				else
				{
					Line("FAIL", "Source not found: " + item2);
				}
			}
			catch (UnauthorizedAccessException)
			{
				Line("FAIL", "Access denied reading source: " + item2);
			}
			catch (IOException ex4)
			{
				Line("WARN", $"Source may be in use: {item2}  ({ex4.Message})");
			}
			catch (Exception ex5)
			{
				Line("FAIL", $"Source problem: {item2}  ({ex5.Message})");
			}
		}
		if (flag2 && current.Mode != JobMode.Delete && list.Count > 0)
		{
			var (num4, value2, flag3) = FindOversize(list, 4294967295L);
			if (num4 > 0 && current.SplitOversize)
			{
				Line("OK", $"{num4}{(flag3 ? "" : "+")} source file(s) are over 4 GB (e.g. {value2}); 'Split files over 4 GB' is on, so they will be written as .001, .002 ... parts.");
			}
			else if (num4 > 0)
			{
				Line("FAIL", $"{num4}{(flag3 ? "" : "+")} source file(s) are over 4 GB and cannot be copied to this FAT32 destination (e.g. {value2}). " + "Reformat the destination as exFAT/NTFS (this erases it) or split the files.");
			}
			else
			{
				Line("OK", flag3 ? "No source file exceeds the FAT32 4 GB limit." : "No file over the FAT32 4 GB limit found in the part of the source that was checked.");
			}
		}
		if (current.Mode != JobMode.Delete)
		{
			string text4 = (current.Destination ?? "").Trim().Trim('"');
			if (text4.Length == 0)
			{
				Line("INFO", "No destination selected.");
			}
			else
			{
				try
				{
					string fullPath = Path.GetFullPath(text4);
					if (Directory.Exists(fullPath))
					{
						Line("OK", "Destination exists: " + fullPath);
						double mbs = WriteTest(fullPath, Line);
						BusInfo destBus = Native.ProbeBus(fullPath);
						if (mbs > 0.0 && destBus.IsUsb && !destBus.IsUsb2OrOlder && destBus.Major <= 0 && mbs < 30.0)
						{
							Line("INFO", $"The USB drive wrote at only {mbs:0} MB/s and Windows does not say which USB version it is on. If you expected more, try a USB 3 (blue or SS) port; cheap sticks can also be this slow.");
						}
						try
						{
							int num5 = Directory.EnumerateFiles(fullPath, "*.cfpart", SearchOption.TopDirectoryOnly).Count();
							if (num5 > 0)
							{
								Line("WARN", $"{num5} leftover *.cfpart temp file(s) in the destination (interrupted copy). Safe to delete.");
							}
						}
						catch
						{
						}
					}
					else
					{
						string directoryName = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
						if (directoryName != null && Directory.Exists(directoryName))
						{
							Line("INFO", "Destination does not exist yet; it will be created: " + fullPath);
						}
						else
						{
							Line("WARN", "Destination and its parent do not exist: " + fullPath);
						}
					}
					if (text != null && hashSet.Contains(text))
					{
						Line("INFO", (current.Mode == JobMode.Move) ? "Source and destination are on the same volume: Move is an instant rename." : "Source and destination are on the same volume: copies share one disk and are slower on hard drives (try 1-2 threads).");
					}
				}
				catch (Exception ex6)
				{
					Line("FAIL", $"Destination path invalid: {text4}  ({ex6.Message})");
				}
			}
		}
		int num6 = ((current.Threads == 0) ? 8 : Math.Clamp(current.Threads, 1, 64));
		int num7 = Math.Clamp(current.BufferSizeMB, 1, 64);
		long num8 = (long)num6 * 6L * num7 * 1024 * 1024;
		if (num > 0 && num8 > num / 4)
		{
			Line("WARN", "Threads x buffer may use up to ~" + FormatBytes(num8) + " of memory (more than 25% of available). Lower threads or buffer size.");
		}
		else
		{
			Line("OK", $"Memory budget fine: up to ~{FormatBytes(num8)} for {((current.Threads == 0) ? "up to " : "")}{num6} thread(s) x {num7} MB buffers.");
		}
		if (num6 > Environment.ProcessorCount * 4)
		{
			Line("WARN", $"{num6} threads is far more than the {Environment.ProcessorCount} logical cores; no speed gain expected.");
		}
		if (current.Verify)
		{
			Line("INFO", "Verify re-reads each destination file (bypassing the Windows cache where possible, so it checks what the device really stored). Expect ~2x I/O.");
		}
		if (current.Mode == JobMode.Sync)
		{
			Line("INFO", "Sync deletes destination files that are not in the source.");
		}
		if (current.Mode == JobMode.Delete)
		{
			Line("INFO", current.RecycleBin ? "Delete sends files to the Recycle Bin (network paths are deleted permanently)." : "Delete is permanent (no Recycle Bin).");
		}
		Sec("Last job this session");
		JobRecord lastJob = d.LastJob;
		if (lastJob == null)
		{
			Line("INFO", "No job has been run in this session.");
		}
		else
		{
			JobOptions options = lastJob.Options;
			Kv("Started", lastJob.Started.ToString("yyyy-MM-dd HH:mm:ss"));
			Kv("Mode", options.Mode.ToString() + (options.Verify ? " + verify" : ""));
			Kv("Overwrite", options.Overwrite.ToString());
			Kv("Threads / buffer", $"{((options.Threads == 0) ? "auto" : options.Threads.ToString())} / {options.BufferSizeMB} MB");
			Kv("Sources", string.Join(" | ", options.Sources.Take(5)) + ((options.Sources.Count > 5) ? $" (+{options.Sources.Count - 5} more)" : ""));
			if (options.Mode != JobMode.Delete)
			{
				Kv("Destination", options.Destination);
			}
			if (!string.IsNullOrWhiteSpace(options.Include))
			{
				Kv("Include", options.Include);
			}
			if (!string.IsNullOrWhiteSpace(options.Exclude))
			{
				Kv("Exclude", options.Exclude);
			}
			sb.Append('\n');
			if (lastJob.Problem != null)
			{
				Line("FAIL", "Job did not run: " + lastJob.Problem);
			}
			else if (lastJob.Result == null)
			{
				Line("INFO", "Job is still running or did not finish.");
			}
			else
			{
				JobResult result = lastJob.Result;
				double num9 = Math.Max(result.Elapsed.TotalSeconds, 0.001);
				Line(result.Cancelled ? "WARN" : ((result.FilesFailed > 0) ? "FAIL" : "OK"), result.Cancelled ? "Aborted by user" : ((result.FilesFailed > 0) ? "Completed with errors" : "Completed successfully"));
				Kv("Files processed", result.FilesProcessed.ToString("N0"));
				Kv("Skipped", result.FilesSkipped.ToString("N0"));
				Kv("Deleted", result.FilesDeleted.ToString("N0"));
				Kv("Failed", result.FilesFailed.ToString("N0"));
				Kv("Data transferred", FormatBytes(result.BytesTransferred));
				Kv("Elapsed", result.Elapsed.ToString("hh\\:mm\\:ss\\.f"));
				if (options.Mode != JobMode.Delete)
				{
					Kv("Average throughput", FormatBytes((long)((double)result.BytesTransferred / num9)) + "/s" + (options.Verify ? "  (includes verify reads)" : ""));
				}
				else
				{
					Kv("Average rate", $"{(double)result.FilesDeleted / num9:0} files/s");
				}
				if (result.Errors.Count > 0)
				{
					sb.Append('\n').Append("  Errors (").Append(result.Errors.Count)
						.Append("):\n");
					foreach (string item3 in result.Errors.Take(200))
					{
						sb.Append("    ").Append(item3).Append('\n');
					}
					if (result.Errors.Count > 200)
					{
						sb.Append("    ... ").Append(result.Errors.Count - 200).Append(" more\n");
					}
					if (result.Errors.Any((string e) => e.Contains("4 GB per-file limit", StringComparison.OrdinalIgnoreCase) || e.Contains("FAT32", StringComparison.OrdinalIgnoreCase)))
					{
						Line("INFO", "Cause: some files exceed the 4 GB limit of a FAT32 destination. Reformat it as exFAT/NTFS or split the files.");
					}
					else if (result.Errors.Any((string e) => e.Contains("not enough free space", StringComparison.OrdinalIgnoreCase)))
					{
						Line("INFO", "Cause: the destination ran out of space. Free some space or choose another drive.");
					}
					else
					{
						Line("INFO", "Common causes: file locked by another program, access denied, path too long, destination full, or antivirus interference.");
					}
				}
			}
		}
		Sec("Event log (most recent)");
		if (d.EventLog.Count == 0)
		{
			Line("INFO", "Event log is empty.");
		}
		foreach (string item4 in d.EventLog.TakeLast(200))
		{
			sb.Append("  ").Append(item4).Append('\n');
		}
		Sec("Unexpected errors");
		if (d.Crashes.Count == 0)
		{
			Line("OK", "No unhandled errors recorded this session.");
		}
		else
		{
			Line("FAIL", $"{d.Crashes.Count} unhandled error(s) recorded this session.");
			foreach (string item5 in d.Crashes.TakeLast(10))
			{
				string[] array = item5.Split('\n');
				foreach (string text5 in array)
				{
					sb.Append("  ").Append(text5.TrimEnd('\r')).Append('\n');
				}
			}
		}
		Kv("Crash log file", d.CrashLogPath);
		string value3 = ((fails > 0) ? "ATTENTION NEEDED" : ((warns > 0) ? "OK WITH WARNINGS" : "ALL CHECKS PASSED"));
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("COPYFORGE DIAGNOSTIC REPORT\n");
		stringBuilder.Append("Generated: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
		StringBuilder stringBuilder2 = stringBuilder.Append("Result:    ").Append(value3);
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, stringBuilder2);
		handler.AppendLiteral("  (");
		handler.AppendFormatted(fails);
		handler.AppendLiteral(" fail, ");
		handler.AppendFormatted(warns);
		handler.AppendLiteral(" warn)\n");
		stringBuilder2.Append(ref handler);
		return new DiagnosticsResult(stringBuilder.Append(sb).ToString(), fails, warns);
		void Kv(string k, string v)
		{
			sb.Append("  ").Append(k.PadRight(22)).Append(v)
				.Append('\n');
		}
		void Line(string lvl, string t)
		{
			if (lvl == "FAIL")
			{
				fails++;
			}
			if (lvl == "WARN")
			{
				warns++;
			}
			sb.Append(lvl switch
			{
				"OK" => "[ OK ]", 
				"WARN" => "[WARN]", 
				"FAIL" => "[FAIL]", 
				_ => "[INFO]", 
			}).Append(' ').Append(t)
				.Append('\n');
		}
		void Sec(string t)
		{
			sb.Append('\n').Append("== ").Append(t.ToUpperInvariant())
				.Append(" ==")
				.Append('\n');
		}
	}

	private static string PqcSupport()
	{
#pragma warning disable SYSLIB5006
		try
		{
			bool kem = System.Security.Cryptography.MLKem.IsSupported;
			bool dsa = System.Security.Cryptography.MLDsa.IsSupported;
			bool slh = System.Security.Cryptography.SlhDsa.IsSupported;
			return $"ML-KEM {(kem ? "yes" : "no")}, ML-DSA {(dsa ? "yes" : "no")}, SLH-DSA {(slh ? "yes" : "no")} (this PC; not used by CopyForge yet)";
		}
		catch
		{
			return "unknown";
		}
#pragma warning restore SYSLIB5006
	}

	private static double WriteTest(string dir, Action<string, string> line)
	{
		string path = Path.Combine(dir, ".cf_diag_" + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			byte[] array = new byte[1048576];
			new Random(1).NextBytes(array);
			Stopwatch stopwatch = Stopwatch.StartNew();
			using (FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, FileOptions.WriteThrough))
			{
				for (int i = 0; i < 16; i++)
				{
					fileStream.Write(array, 0, array.Length);
				}
				fileStream.Flush(flushToDisk: true);
			}
			stopwatch.Stop();
			double value = 16.0 / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
			line("OK", $"Destination writable. 16 MB write test: {stopwatch.ElapsedMilliseconds} ms = {value:0} MB/s (write-through, indicative only).");
			return value;
		}
		catch (UnauthorizedAccessException)
		{
			line("FAIL", "Destination is NOT writable: access denied.");
		}
		catch (IOException ex2)
		{
			line("FAIL", "Destination write test failed: " + ex2.Message);
		}
		catch (Exception ex3)
		{
			line("FAIL", "Destination write test failed: " + ex3.Message);
		}
		finally
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
		}
		return 0.0;
	}

	private static (int Count, string Example, bool Complete) FindOversize(IEnumerable<string> sources, long limit)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num = 0;
		string text = "";
		EnumerationOptions enumerationOptions = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = FileAttributes.ReparsePoint
		};
		try
		{
			foreach (string source in sources)
			{
				if (File.Exists(source))
				{
					FileInfo fileInfo = new FileInfo(source);
					if (fileInfo.Length > limit)
					{
						num++;
						if (text.Length == 0)
						{
							text = fileInfo.Name;
						}
					}
				}
				else
				{
					if (!Directory.Exists(source))
					{
						continue;
					}
					foreach (FileInfo item in new DirectoryInfo(source).EnumerateFiles("*", enumerationOptions))
					{
						if (item.Length > limit)
						{
							num++;
							if (text.Length == 0)
							{
								text = item.Name;
							}
						}
						if (stopwatch.ElapsedMilliseconds > 3000)
						{
							return (Count: num, Example: text, Complete: false);
						}
					}
				}
			}
		}
		catch
		{
			return (Count: num, Example: text, Complete: false);
		}
		return (Count: num, Example: text, Complete: true);
	}

	private static string? RootOf(string path)
	{
		try
		{
			return Path.GetPathRoot(Path.GetFullPath(path));
		}
		catch
		{
			return null;
		}
	}

	private static string FormatBytes(long b)
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
		return $"{num:0.##} {array[num2]}";
	}
}
