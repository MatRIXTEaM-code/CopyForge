using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine;

public sealed record HashFileResult(int Checked, int Ok, List<string> Bad, List<string> Missing, int Lines);

/// <summary>Standard "sha256sum" files: "HEX *relative/path" per line (works with sha256sum -c).</summary>
public static class HashFile
{
	public static async Task<int> MakeAsync(string folder, string outFile, int threads, Action<string>? log, CancellationToken ct)
	{
		string root = Path.GetFullPath(folder);
		string outFull = Path.GetFullPath(outFile);
		List<string> files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where((string f) => !string.Equals(Path.GetFullPath(f), outFull, StringComparison.OrdinalIgnoreCase) && !f.Contains(Path.DirectorySeparatorChar + ".cf_") ).OrderBy((string f) => f, StringComparer.Ordinal).ToList();
		ConcurrentDictionary<string, string> sums = new ConcurrentDictionary<string, string>();
		int done = 0;
		await Parallel.ForEachAsync(files, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(1, (threads > 0) ? threads : Environment.ProcessorCount),
			CancellationToken = ct
		}, async delegate(string f, CancellationToken t)
		{
			sums[f] = await Sha(f, t).ConfigureAwait(false);
			int n = Interlocked.Increment(ref done);
			if (n % 200 == 0)
			{
				log?.Invoke($"  hashed {n:N0}/{files.Count:N0}");
			}
		}).ConfigureAwait(false);
		StringBuilder sb = new StringBuilder();
		foreach (string f2 in files)
		{
			string rel = Path.GetRelativePath(root, f2).Replace('\\', '/');
			sb.Append(sums[f2]).Append(" *").Append(rel).Append('\n');
		}
		File.WriteAllText(outFull, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return files.Count;
	}

	/// <summary>Checks every listed file under <paramref name="root"/>; accepts "HEX *path", "HEX  path" and "#" comments.</summary>
	public static async Task<HashFileResult> VerifyAsync(string hashFile, string root, int threads, Action<string>? log, CancellationToken ct)
	{
		List<(string hash, string rel)> items = new List<(string, string)>();
		int lines = 0;
		foreach (string raw in File.ReadAllLines(hashFile))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
			{
				continue;
			}
			lines++;
			int sp = line.IndexOf(' ');
			if (sp != 64)
			{
				continue;
			}
			string h = line.Substring(0, 64).ToLowerInvariant();
			string rel2 = line.Substring(65).TrimStart('*', ' ');
			if (rel2.Length > 0)
			{
				items.Add((h, rel2.Replace('/', Path.DirectorySeparatorChar)));
			}
		}
		ConcurrentBag<string> bad = new ConcurrentBag<string>();
		ConcurrentBag<string> missing = new ConcurrentBag<string>();
		int ok = 0;
		string rootFull = Path.GetFullPath(root);
		await Parallel.ForEachAsync(items, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(1, (threads > 0) ? threads : Environment.ProcessorCount),
			CancellationToken = ct
		}, async delegate((string hash, string rel) it, CancellationToken t)
		{
			string path = Path.GetFullPath(Path.Combine(rootFull, it.rel));
			if (!path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
			{
				bad.Add(it.rel + " (path leaves the folder)");
			}
			else if (!File.Exists(path))
			{
				missing.Add(it.rel);
			}
			else if (string.Equals(await Sha(path, t).ConfigureAwait(false), it.hash, StringComparison.Ordinal))
			{
				Interlocked.Increment(ref ok);
			}
			else
			{
				bad.Add(it.rel);
			}
		}).ConfigureAwait(false);
		return new HashFileResult(items.Count, ok, bad.OrderBy((string x) => x, StringComparer.Ordinal).ToList(), missing.OrderBy((string x) => x, StringComparer.Ordinal).ToList(), lines);
	}

	private static async Task<string> Sha(string path, CancellationToken ct)
	{
		using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1048576, FileOptions.SequentialScan | FileOptions.Asynchronous);
		byte[] h = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
		return Convert.ToHexString(h).ToLowerInvariant();
	}
}
