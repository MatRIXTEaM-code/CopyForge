using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CopyForge.Engine;

public sealed record CapacityResult(bool Passed, long BytesWritten, long BytesGood, long BadBlocks, long FirstBadOffset, string Summary);

/// <summary>
/// Detects fake-capacity USB sticks and cards: fills free space with unique, reproducible data, then reads it all back
/// (bypassing the Windows cache where possible) and compares. A fake drive wraps around and returns old or wrong data.
/// Only touches its own temporary folder, and deletes it afterwards.
/// </summary>
public static class CapacityTest
{
	private const int Block = 4 * 1024 * 1024;

	private static void Fill(Span<byte> buf, ulong seed, long globalBlock)
	{
		Span<ulong> w = MemoryMarshal.Cast<byte, ulong>(buf);
		ulong s = seed + (ulong)(globalBlock + 1) * 11400714819323198485UL;
		if (s == 0)
		{
			s = 88172645463325252UL;
		}
		for (int i = 0; i < w.Length; i++)
		{
			s ^= s >> 12;
			s ^= s << 25;
			s ^= s >> 27;
			w[i] = s * 2685821657736338717UL;
		}
	}

	public static CapacityResult Run(string target, long limitBytes, Action<string>? log, CancellationToken ct, long fileBytes = 256L * 1024 * 1024)
	{
		string root = Path.GetPathRoot(Path.GetFullPath(target)) ?? target;
		if (!Directory.Exists(target))
		{
			throw new DirectoryNotFoundException("Folder not found: " + target);
		}
		long free = new DriveInfo(root).AvailableFreeSpace;
		long margin = 16L * 1024 * 1024;
		long want = ((limitBytes > 0) ? Math.Min(limitBytes, free - margin) : (free - margin));
		want = want / Block * Block;
		if (want < Block)
		{
			throw new IOException("Not enough free space to test (need at least 20 MB free).");
		}
		fileBytes = Math.Max(Block, fileBytes / Block * Block);
		string dir = Path.Combine(target, ".cf_capacity_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(dir);
		ulong seed = (ulong)DateTime.UtcNow.Ticks ^ 0xA5A5A5A55A5A5A5AUL;
		List<(string path, long blocks)> files = new List<(string, long)>();
		long written = 0L;
		log?.Invoke($"Capacity test: writing up to {want / 1048576.0:N0} MB of unique data to {dir} ...");
		AlignedBuffer buf = new AlignedBuffer(Block);
		try
		{
			long globalBlock = 0L;
			int lastPct = -1;
			bool full = false;
			for (int fi = 0; written < want && !full; fi++)
			{
				ct.ThrowIfCancellationRequested();
				string path = Path.Combine(dir, $"t{fi:D5}.bin");
				long blocksInFile = 0L;
				try
				{
					using Microsoft.Win32.SafeHandles.SafeFileHandle h = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.WriteThrough, 0L);
					files.Add((path, 0L));
					while (blocksInFile * Block < fileBytes && written < want)
					{
						ct.ThrowIfCancellationRequested();
						Fill(buf.GetSpan(), seed, globalBlock);
						try
						{
							RandomAccess.Write(h, buf.GetSpan(), blocksInFile * Block);
						}
						catch (IOException)
						{
							full = true;
							break;
						}
						blocksInFile++;
						globalBlock++;
						written += Block;
						int pct = (int)(written * 100 / want);
						if (pct / 5 != lastPct / 5)
						{
							lastPct = pct;
							log?.Invoke($"  writing... {pct}%");
						}
					}
					RandomAccess.FlushToDisk(h);
				}
				catch (IOException) when (blocksInFile == 0L)
				{
					full = true;
				}
				files[files.Count - 1] = (path, blocksInFile);
			}
			log?.Invoke($"Written {written / 1048576.0:N0} MB. Reading it back and comparing (this bypasses the cache where Windows allows) ...");
			AlignedBuffer rd = new AlignedBuffer(Block);
			long good = 0L, bad = 0L, firstBad = -1L, g = 0L;
			int lastRead = -1;
			try
			{
				foreach ((string path, long blocks) in files)
				{
					if (blocks == 0L)
					{
						continue;
					}
					Microsoft.Win32.SafeHandles.SafeFileHandle h2;
					try
					{
						h2 = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, (FileOptions)536870912 | FileOptions.SequentialScan, 0L);
					}
					catch (Exception e) when (e is ArgumentException || e is IOException || e is NotSupportedException)
					{
						h2 = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan, 0L);
					}
					using (h2)
					{
						byte[] expect = new byte[Block];
						for (long b = 0; b < blocks; b++, g++)
						{
							ct.ThrowIfCancellationRequested();
							int n = 0;
							while (n < Block)
							{
								int r = RandomAccess.Read(h2, rd.GetSpan().Slice(n), b * Block + n);
								if (r <= 0)
								{
									break;
								}
								n += r;
							}
							Fill(expect, seed, g);
							if (n == Block && rd.GetSpan().SequenceEqual(expect))
							{
								good += Block;
							}
							else
							{
								bad++;
								if (firstBad < 0)
								{
									firstBad = g * Block;
								}
							}
							int pct2 = (int)((g + 1) * Block * 100 / Math.Max(1L, written));
							if (pct2 / 5 != lastRead / 5)
							{
								lastRead = pct2;
								log?.Invoke($"  verifying... {pct2}%");
							}
						}
					}
				}
			}
			finally
			{
				rd.Free();
			}
			if (bad == 0L)
			{
				return new CapacityResult(true, written, good, 0L, -1L, $"PASSED: {written / 1073741824.0:N2} GB written and read back correctly. The capacity looks genuine for the part tested.");
			}
			return new CapacityResult(false, written, good, bad, firstBad, $"FAILED: {bad:N0} block(s) of {written / Block:N0} came back wrong. The first bad data is at {firstBad / 1073741824.0:N2} GB, so the real usable capacity is probably about {firstBad / 1073741824.0:N2} GB. Do not trust this drive with important files.");
		}
		finally
		{
			buf.Free();
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch
			{
			}
		}
	}
}
