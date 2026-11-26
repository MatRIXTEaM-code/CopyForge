using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CopyForge.Engine;

namespace CopyForge.App;

/// <summary>Saved jobs (sources, destination, mode, options) that can be started from the tray icon.</summary>
internal sealed class Preset
{
	public string Name { get; set; } = "";

	public JobOptions Options { get; set; } = new JobOptions();
}

internal static class Presets
{
	public const int Max = 30;

	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "presets.json");

	public static List<Preset> Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(FilePath)) ?? new List<Preset>();
			}
		}
		catch
		{
		}
		return new List<Preset>();
	}

	public static void Save(List<Preset> list)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			string tmp = FilePath + ".tmp";
			File.WriteAllText(tmp, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
			File.Move(tmp, FilePath, overwrite: true);
		}
		catch
		{
		}
	}

	/// <summary>Adds or replaces (same name) a preset. Delete jobs and retry lists are not kept.</summary>
	public static bool Add(string name, JobOptions o, out string why)
	{
		why = "";
		if (o.Mode == JobMode.Delete)
		{
			why = "Delete jobs cannot be saved as presets.";
			return false;
		}
		if (o.Sources.Count == 0 || string.IsNullOrWhiteSpace(o.Destination))
		{
			why = "Choose the source and destination in the main window first.";
			return false;
		}
		List<Preset> list = Load();
		JobOptions c = o.Clone();
		c.ExplicitPairs = null;
		c.ModifiedSinceUtc = null;
		c.DryRun = false;
		list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		if (list.Count >= Max)
		{
			why = "At most " + Max + " presets.";
			return false;
		}
		list.Add(new Preset { Name = name, Options = c });
		Save(list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList());
		return true;
	}

	public static void Remove(string name)
	{
		List<Preset> list = Load();
		list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		Save(list);
	}
}
