using System;
using System.IO;
using System.Text.Json;

namespace CopyForge.App;

internal sealed class AppSettings
{
	public string Destination { get; set; } = "";


	public string Include { get; set; } = "";


	public string Exclude { get; set; } = "";


	public int OverwriteIndex { get; set; }

	public bool Verify { get; set; }

	public int VerifyAlgoIndex { get; set; }

	public bool BlockClone { get; set; } = true;

	public bool EjectAfter { get; set; }

	public bool DirectWriteRemovable { get; set; } = true;

	public int Threads { get; set; }

	public int BufferMB { get; set; } = 4;


	public int IoIndex { get; set; }

	public int MethodIndex { get; set; }

	public int Chunks { get; set; }

	public int Retries { get; set; } = 3;


	public int WaitSeconds { get; set; } = 120;

	public int LimitMBps { get; set; }

	public bool WriteLog { get; set; }

	public bool VerifySkipped { get; set; }

	public bool ShutdownAfter { get; set; }

	public bool KeepVersions { get; set; }

	public bool CheckOnPlug { get; set; }

	public bool LocalSendOn { get; set; }

	public string LocalSendFolder { get; set; } = "";

	public string LocalSendAlias { get; set; } = "";

	public string LocalSendPin { get; set; } = "";

	public int ParityPct { get; set; }

	public int ResumeMB { get; set; } = 256;

	public string OnFinish { get; set; } = "";

	public bool AlwaysOnTop { get; set; }


	public bool Flush { get; set; } = true;


	public bool DirTimes { get; set; } = true;


	public bool Extras { get; set; }

	public bool Split { get; set; }

	public bool Recycle { get; set; }

	public bool Manifest { get; set; }

	public bool SignManifest { get; set; }

	public bool Vss { get; set; }

	public bool Delta { get; set; }

	public bool AutoTune { get; set; }

	public static string KeyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "manifest-key.pem");

	public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "copylog.csv");

	public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopyForge", "settings.json");

	public static AppSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
			}
		}
		catch
		{
		}
		return new AppSettings();
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			string tmp = FilePath + ".tmp";
			File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			File.Move(tmp, FilePath, overwrite: true);
		}
		catch
		{
		}
	}
}
