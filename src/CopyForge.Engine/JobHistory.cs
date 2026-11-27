using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CopyForge.Engine;

/// <summary>One finished (or failed-to-start) job, as kept in the history file.</summary>
public sealed class HistoryEntry
{
	public DateTime Started { get; set; }

	public string Source { get; set; } = "";

	public string Mode { get; set; } = "";

	public List<string> Sources { get; set; } = new List<string>();

	public string Destination { get; set; } = "";

	public List<string> ExtraDestinations { get; set; } = new List<string>();

	public bool Verify { get; set; }

	/// <summary>Full options of the job (for re-checks and the robocopy view); null in older entries.</summary>
	public JobOptions? Options { get; set; }

	public string VerifyAlgo { get; set; } = "";

	public bool Finished { get; set; }

	public bool Cancelled { get; set; }

	public long FilesProcessed { get; set; }

	public long FilesSkipped { get; set; }

	public long FilesDeleted { get; set; }

	public long FilesFailed { get; set; }

	public long Bytes { get; set; }

	public double ElapsedSeconds { get; set; }

	public string Problem { get; set; } = "";

	public List<string> Errors { get; set; } = new List<string>();

	public string Outcome
	{
		get
		{
			if (!string.IsNullOrEmpty(Problem))
			{
				return "PROBLEM";
			}
			if (Cancelled)
			{
				return "CANCELLED";
			}
			if (FilesFailed > 0)
			{
				return "ERRORS";
			}
			return Finished ? "OK" : "?";
		}
	}
}

/// <summary>Append-only history of jobs: one JSON object per line.</summary>
public static class JobHistory
{
	private const long RotateAtBytes = 4194304L;

	private const int KeepLines = 2000;

	private static readonly object Gate = new object();

	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "history.jsonl");

	public static HistoryEntry FromRecord(JobRecord rec, string source)
	{
		JobOptions o = rec.Options;
		HistoryEntry e = new HistoryEntry
		{
			Started = rec.Started,
			Source = source,
			Mode = o.Mode.ToString(),
			Sources = o.Sources.ToList(),
			Destination = o.Destination,
			ExtraDestinations = (o.ExtraDestinations ?? new List<string>()).ToList(),
			Verify = o.Verify,
			VerifyAlgo = (o.Verify ? VerifyAlgos.Name(o.VerifyAlgo) : ""),
			Problem = rec.Problem ?? "",
			Options = o
		};
		JobResult r = rec.Result;
		if (r != null)
		{
			e.Finished = true;
			e.Cancelled = r.Cancelled;
			e.FilesProcessed = r.FilesProcessed;
			e.FilesSkipped = r.FilesSkipped;
			e.FilesDeleted = r.FilesDeleted;
			e.FilesFailed = r.FilesFailed;
			e.Bytes = r.BytesTransferred;
			e.ElapsedSeconds = r.Elapsed.TotalSeconds;
			e.Errors = r.Errors.Take(20).ToList();
		}
		return e;
	}

	/// <summary>Adds a job to the history. Never throws: history must not break a copy.</summary>
	public static void Append(JobRecord rec, string source)
	{
		try
		{
			string line = JsonSerializer.Serialize(FromRecord(rec, source));
			lock (Gate)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
				using (FileStream fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
				using (StreamWriter w = new StreamWriter(fs))
				{
					w.WriteLine(line);
				}
				if (new FileInfo(FilePath).Length > RotateAtBytes)
				{
					string[] all = File.ReadAllLines(FilePath);
					File.WriteAllLines(FilePath, all.Skip(Math.Max(0, all.Length - KeepLines)));
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>The newest entries first. <paramref name="search"/> matches any text in the entry.</summary>
	public static List<HistoryEntry> Read(int max = 500, string search = "")
	{
		List<HistoryEntry> list = new List<HistoryEntry>();
		try
		{
			if (!File.Exists(FilePath))
			{
				return list;
			}
			string[] lines;
			lock (Gate)
			{
				using FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using StreamReader sr = new StreamReader(fs);
				lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
			}
			for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
			{
				try
				{
					HistoryEntry e = JsonSerializer.Deserialize<HistoryEntry>(lines[i].Trim());
					if (e != null && Matches(e, search))
					{
						list.Add(e);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list;
	}

	public static bool Matches(HistoryEntry e, string search)
	{
		if (string.IsNullOrWhiteSpace(search))
		{
			return true;
		}
		string hay = string.Join(" | ", e.Mode, e.Outcome, e.Destination, string.Join(" ", e.ExtraDestinations), string.Join(" ", e.Sources), e.Problem, string.Join(" ", e.Errors), e.VerifyAlgo, e.Started.ToString("yyyy-MM-dd HH:mm"));
		return hay.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	public static void Clear()
	{
		try
		{
			lock (Gate)
			{
				if (File.Exists(FilePath))
				{
					File.Delete(FilePath);
				}
			}
		}
		catch
		{
		}
	}
}
