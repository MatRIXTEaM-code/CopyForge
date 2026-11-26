using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed record AuditResult(int Checked, int Ok, IReadOnlyList<string> Changed, IReadOnlyList<string> Missing, IReadOnlyList<string> Unlisted, string Signature, string? KeyFingerprint, bool ManifestFound)
{
	public bool Clean
	{
		get
		{
			if (ManifestFound && Changed.Count == 0 && Missing.Count == 0)
			{
				return Signature != "INVALID";
			}
			return false;
		}
	}
}
