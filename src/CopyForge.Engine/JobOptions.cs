using System;
using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed class JobOptions
{
	public List<string> Sources { get; set; } = new List<string>();


	public string Destination { get; set; } = "";

	/// <summary>More destinations to copy to after <see cref="Destination"/> (one after another).</summary>
	public List<string> ExtraDestinations { get; set; } = new List<string>();


	public JobMode Mode { get; set; }

	public OverwriteMode Overwrite { get; set; } = OverwriteMode.IfDifferent;


	public bool Verify { get; set; }

	public bool BlockClone { get; set; } = true;

	/// <summary>Safely eject a removable destination when the job finishes without errors.</summary>
	public bool EjectAfter { get; set; }

	/// <summary>Write small files straight to their final name on removable drives (no temp name + rename). Faster on USB sticks; a cancelled or failed file is deleted.</summary>
	public bool DirectWriteRemovable { get; set; } = true;

	public VerifyAlgo VerifyAlgo { get; set; }

	public string Include { get; set; } = "";


	public string Exclude { get; set; } = "";


	public long MinSizeBytes { get; set; }

	public long MaxSizeBytes { get; set; }

	public DateTime? ModifiedSinceUtc { get; set; }

	public int Threads { get; set; }

	public int BufferSizeMB { get; set; } = 4;


	public IoMode Io { get; set; }

	public CopyMethod Method { get; set; }

	public int ChunkWorkers { get; set; }

	public bool FlushToDisk { get; set; } = true;


	public int Retries { get; set; } = 3;


	public int RetryWaitSeconds { get; set; } = 2;


	public int ReconnectWaitSeconds { get; set; } = 120;


	public bool CopyNtfsExtras { get; set; }

	public bool SplitOversize { get; set; }

	public bool PreserveDirTimes { get; set; } = true;


	public bool RecycleBin { get; set; }

	public bool UseUsn { get; set; }

	public bool AutoTune { get; set; }

	public bool DeltaUpdate { get; set; }

	public int DeltaMinMB { get; set; } = 32;


	/// <summary>Speed limit in MB per second (0 = unlimited). Forces the CopyForge pipeline.</summary>
	public int LimitMBps { get; set; }

	/// <summary>Write a per-file CSV log here (empty = no log).</summary>
	public string LogPath { get; set; } = "";

	/// <summary>Stop Windows going to sleep while the job runs.</summary>
	public bool KeepAwake { get; set; } = true;

	public bool DryRun { get; set; }

	public bool UseVss { get; set; }

	public bool WriteManifest { get; set; }

	public string ManifestSignKey { get; set; } = "";


	public List<FilePair>? ExplicitPairs { get; set; }

	internal long? Fat32MaxOverride { get; set; }

	internal string? FileSystemOverride { get; set; }

	public JobOptions Clone()
	{
		return (JobOptions)MemberwiseClone();
	}
}
