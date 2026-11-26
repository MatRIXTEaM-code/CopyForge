namespace CopyForge.Engine;

/// <summary>Checksum used by "verify after copy".</summary>
public enum VerifyAlgo
{
	/// <summary>xxHash3, 64-bit (default, fastest).</summary>
	XxHash3,
	/// <summary>xxHash3, 128-bit.</summary>
	XxHash128,
	/// <summary>SHA-256 (cryptographic, slowest).</summary>
	Sha256
}

public static class VerifyAlgos
{
	public static string Name(VerifyAlgo a)
	{
		return a switch
		{
			VerifyAlgo.XxHash128 => "xxHash128", 
			VerifyAlgo.Sha256 => "SHA-256", 
			_ => "xxHash3", 
		};
	}

	public static bool TryParse(string text, out VerifyAlgo algo)
	{
		switch ((text ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", ""))
		{
		case "xxh3":
		case "xxhash3":
		case "xxhash":
		case "xxhash64":
		case "xxh64":
		case "fast":
			algo = VerifyAlgo.XxHash3;
			return true;
		case "xxh128":
		case "xxhash128":
			algo = VerifyAlgo.XxHash128;
			return true;
		case "sha256":
		case "sha":
			algo = VerifyAlgo.Sha256;
			return true;
		default:
			algo = VerifyAlgo.XxHash3;
			return false;
		}
	}
}
