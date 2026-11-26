using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed class ManifestDoc
{
	public int Version { get; set; } = 1;


	public string Tool { get; set; } = "CopyForge";


	public string CreatedUtc { get; set; } = "";


	public string? PublicKey { get; set; }

	public List<ManifestEntry> Entries { get; set; } = new List<ManifestEntry>();

}
