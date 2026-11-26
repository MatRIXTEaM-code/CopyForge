using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CopyForge.Engine.Net;

public sealed class LsHistoryEntry
{
	public DateTime When { get; set; }

	public string Direction { get; set; } = "";

	public string Device { get; set; } = "";

	public string Address { get; set; } = "";

	public int Files { get; set; }

	public long Bytes { get; set; }

	public bool Ok { get; set; }

	public string Message { get; set; } = "";
}

/// <summary>A small log of LocalSend transfers (sent and accepted), one JSON line each.</summary>
public static class LsHistory
{
	private static readonly object Gate = new object();

	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "localsend-history.jsonl");

	public static void Add(LsHistoryEntry e)
	{
		try
		{
			e.When = DateTime.Now;
			lock (Gate)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
				File.AppendAllText(FilePath, JsonSerializer.Serialize(e) + "\n");
				string[] all = File.ReadAllLines(FilePath);
				if (all.Length > 1000)
				{
					File.WriteAllLines(FilePath, all.Skip(all.Length - 500));
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>Newest first; <paramref name="device"/> filters by (part of) the device name.</summary>
	public static List<LsHistoryEntry> Read(int max, string device = "")
	{
		List<LsHistoryEntry> list = new List<LsHistoryEntry>();
		try
		{
			if (!File.Exists(FilePath))
			{
				return list;
			}
			string[] lines;
			lock (Gate)
			{
				lines = File.ReadAllLines(FilePath);
			}
			for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
			{
				try
				{
					LsHistoryEntry e = JsonSerializer.Deserialize<LsHistoryEntry>(lines[i]);
					if (e != null && (device.Length == 0 || e.Device.Contains(device, StringComparison.OrdinalIgnoreCase)))
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

	public static string Line(LsHistoryEntry e)
	{
		return $"{e.When:yyyy-MM-dd HH:mm}  {e.Direction,-8} {e.Device}  {e.Files:N0} file(s) {e.Bytes / 1048576.0:0.#} MB  " + (e.Ok ? "ok" : ("FAILED: " + e.Message));
	}
}
