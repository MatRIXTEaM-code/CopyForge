using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine;

public static class Manifest
{
	public const string FileName = "cfmanifest.json";

	public const string SigName = "cfmanifest.sig";

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static string Rel(string root, string path)
	{
		return Path.GetRelativePath(root, path).Replace('\\', '/');
	}

	public static async Task<string> HashFileAsync(string path, CancellationToken ct)
	{
		using SHA256 sha = SHA256.Create();
		FileStream fs = new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.Open,
			Access = FileAccess.Read,
			Share = (FileShare.ReadWrite | FileShare.Delete),
			Options = (FileOptions.SequentialScan | FileOptions.Asynchronous),
			BufferSize = 1048576
		});
		string result;
		try
		{
			result = Convert.ToHexString(await sha.ComputeHashAsync(fs, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
		}
		finally
		{
			if (fs != null)
			{
				await fs.DisposeAsync();
			}
		}
		return result;
	}

	private static bool IsInsideRel(string p)
	{
		if (string.IsNullOrEmpty(p) || p.Contains('\0') || p.Contains(':') || p.StartsWith('/') || p.StartsWith('\\'))
		{
			return false;
		}
		return !p.Split('/', '\\').Any((string seg) => seg == "..");
	}

	public static ManifestDoc? Load(string root)
	{
		string path = Path.Combine(root, "cfmanifest.json");
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			ManifestDoc manifestDoc = JsonSerializer.Deserialize<ManifestDoc>(File.ReadAllBytes(path), Json);
			if (manifestDoc?.Entries != null)
			{
				// A tampered manifest must not make audit/update touch files outside the folder it describes.
				manifestDoc.Entries = manifestDoc.Entries.Where((ManifestEntry e) => IsInsideRel(e.P)).ToList();
			}
			return manifestDoc;
		}
		catch
		{
			return null;
		}
	}

	public static string? FindRoot(string picked)
	{
		try
		{
			for (DirectoryInfo directoryInfo = new DirectoryInfo(picked); directoryInfo != null; directoryInfo = directoryInfo.Parent)
			{
				if (File.Exists(Path.Combine(directoryInfo.FullName, "cfmanifest.json")))
				{
					return directoryInfo.FullName;
				}
			}
			foreach (string item in Directory.EnumerateDirectories(picked))
			{
				if (File.Exists(Path.Combine(item, "cfmanifest.json")))
				{
					return item;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public static string Fingerprint(string publicKeyB64)
	{
		return Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyB64))).Substring(0, 16).ToLowerInvariant();
	}

	public static ECDsa LoadOrCreateKey(string pemPath)
	{
		ECDsa eCDsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		if (File.Exists(pemPath))
		{
			eCDsa.ImportFromPem(File.ReadAllText(pemPath));
			return eCDsa;
		}
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(pemPath));
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(pemPath, eCDsa.ExportPkcs8PrivateKeyPem());
		PrivateFile.Restrict(pemPath);
		return eCDsa;
	}

	public static async Task UpdateAsync(string root, IEnumerable<(string? src, string dst)> files, string? signKeyPem, int threads, Action<string>? info, CancellationToken ct)
	{
		string root2 = root;
		Action<string> info2 = info;
		Dictionary<string, ManifestEntry> obj = Load(root2)?.Entries.ToDictionary<ManifestEntry, string>((ManifestEntry e) => e.P, StringComparer.Ordinal) ?? new Dictionary<string, ManifestEntry>();
		ConcurrentDictionary<string, ManifestEntry> list = new ConcurrentDictionary<string, ManifestEntry>(StringComparer.Ordinal);
		int hashed = 0;
		foreach (ManifestEntry value2 in obj.Values)
		{
			string fileName = Path.Combine(root2, value2.P.Replace('/', Path.DirectorySeparatorChar));
			try
			{
				FileInfo fileInfo = new FileInfo(fileName);
				if (fileInfo.Exists && fileInfo.Length == value2.S && fileInfo.LastWriteTimeUtc.Ticks == value2.M)
				{
					list[value2.P] = value2;
				}
			}
			catch
			{
			}
		}
		await Parallel.ForEachAsync(from g in files.GroupBy<(string, string), string>(((string src, string dst) x) => x.dst, StringComparer.OrdinalIgnoreCase)
			select g.First(), new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(1, threads),
			CancellationToken = ct
		}, async delegate((string src, string dst) pair, CancellationToken t)
		{
			string f = pair.dst;
			try
			{
				FileInfo fi = new FileInfo(f);
				if (fi.Exists)
				{
					string rel = Rel(root2, f);
					if (!(rel == "cfmanifest.json") && !(rel == "cfmanifest.sig") && (!list.TryGetValue(rel, out ManifestEntry value) || value.S != fi.Length || value.M != fi.LastWriteTimeUtc.Ticks))
					{
						string path2;
						if (pair.src != null && File.Exists(pair.src) && new FileInfo(pair.src).Length == fi.Length)
						{
							(path2, _) = pair;
						}
						else
						{
							path2 = f;
						}
						string h = await HashFileAsync(path2, t).ConfigureAwait(continueOnCapturedContext: false);
						list[rel] = new ManifestEntry
						{
							P = rel,
							S = fi.Length,
							M = fi.LastWriteTimeUtc.Ticks,
							H = h
						};
						Interlocked.Increment(ref hashed);
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				info2?.Invoke("Manifest: could not hash " + f + ": " + ex2.Message);
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		ManifestDoc manifestDoc = new ManifestDoc
		{
			CreatedUtc = DateTime.UtcNow.ToString("o")
		};
		manifestDoc.Entries = list.Values.OrderBy<ManifestEntry, string>((ManifestEntry e) => e.P, StringComparer.Ordinal).ToList();
		ECDsa eCDsa = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(signKeyPem))
			{
				eCDsa = LoadOrCreateKey(signKeyPem);
				manifestDoc.PublicKey = Convert.ToBase64String(eCDsa.ExportSubjectPublicKeyInfo());
			}
			byte[] data = JsonSerializer.SerializeToUtf8Bytes(manifestDoc, Json);
			AtomicWrite(Path.Combine(root2, "cfmanifest.json"), data);
			string path = Path.Combine(root2, "cfmanifest.sig");
			if (eCDsa != null)
			{
				AtomicWrite(path, Encoding.ASCII.GetBytes(Convert.ToBase64String(eCDsa.SignData(data, HashAlgorithmName.SHA256))));
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		finally
		{
			eCDsa?.Dispose();
		}
		info2?.Invoke($"Manifest: {manifestDoc.Entries.Count:N0} files recorded ({hashed:N0} hashed now)" + ((manifestDoc.PublicKey != null) ? (", signed (key " + Fingerprint(manifestDoc.PublicKey) + ").") : ", unsigned."));
	}

	private static void AtomicWrite(string path, byte[] data)
	{
		string text = path + ".tmp";
		File.WriteAllBytes(text, data);
		File.Move(text, path, overwrite: true);
	}

	public static async Task<AuditResult> AuditAsync(string root, int threads, bool listUnlisted, string? pinnedFingerprint, IProgress<(int done, int total)>? progress, CancellationToken ct)
	{
		string root2 = root;
		IProgress<(int done, int total)> progress2 = progress;
		ManifestDoc doc = Load(root2);
		if (doc == null)
		{
			return new AuditResult(0, 0, new string[0], new string[0], new string[0], "none", null, ManifestFound: false);
		}
		string sig = "unsigned";
		string fp = null;
		string path = Path.Combine(root2, "cfmanifest.sig");
		if (doc.PublicKey != null || File.Exists(path))
		{
			try
			{
				byte[] data = File.ReadAllBytes(Path.Combine(root2, "cfmanifest.json"));
				using ECDsa eCDsa = ECDsa.Create();
				eCDsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(doc.PublicKey ?? ""), out var _);
				byte[] signature = Convert.FromBase64String(File.ReadAllText(path).Trim());
				fp = Fingerprint(doc.PublicKey);
				sig = (eCDsa.VerifyData(data, signature, HashAlgorithmName.SHA256) ? "valid" : "INVALID");
				if (sig == "valid" && !string.IsNullOrWhiteSpace(pinnedFingerprint) && !fp.Equals(pinnedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					sig = "INVALID";
				}
			}
			catch
			{
				sig = "INVALID";
			}
		}
		else if (!string.IsNullOrWhiteSpace(pinnedFingerprint))
		{
			sig = "INVALID";
		}
		ConcurrentBag<string> changed = new ConcurrentBag<string>();
		ConcurrentBag<string> missing = new ConcurrentBag<string>();
		int ok = 0;
		int done = 0;
		int total = doc.Entries.Count;
		await Parallel.ForEachAsync(doc.Entries, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(1, threads),
			CancellationToken = ct
		}, async delegate(ManifestEntry e, CancellationToken t)
		{
			string path2 = Path.Combine(root2, e.P.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(path2))
			{
				missing.Add(e.P);
			}
			else
			{
				try
				{
					if (await HashFileAsync(path2, t).ConfigureAwait(continueOnCapturedContext: false) == e.H)
					{
						Interlocked.Increment(ref ok);
					}
					else
					{
						changed.Add(e.P);
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
					changed.Add(e.P + "  (unreadable)");
				}
			}
			progress2?.Report((Interlocked.Increment(ref done), total));
		}).ConfigureAwait(continueOnCapturedContext: false);
		List<string> list = new List<string>();
		if (listUnlisted)
		{
			HashSet<string> hashSet = new HashSet<string>(doc.Entries.Select((ManifestEntry e) => e.P), StringComparer.Ordinal);
			foreach (string item in Directory.EnumerateFiles(root2, "*", SearchOption.AllDirectories))
			{
				string text = Rel(root2, item);
				if (!(text == "cfmanifest.json") && !(text == "cfmanifest.sig") && !text.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase) && !hashSet.Contains(text))
				{
					list.Add(text);
				}
			}
		}
		return new AuditResult(total, ok, changed.OrderBy((string x) => x).ToList(), missing.OrderBy((string x) => x).ToList(), list.OrderBy((string x) => x).ToList(), sig, fp, ManifestFound: true);
	}
}
