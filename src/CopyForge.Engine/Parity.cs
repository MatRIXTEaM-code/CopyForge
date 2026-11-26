using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CopyForge.Engine;

/// <summary>Result of a parity pass over a folder.</summary>
public sealed record ParityResult(int Files, int Written, int Skipped, int Damaged, int Repaired, int Unrepairable, int NoParity, List<string> Problems);

/// <summary>
/// Repair data for files (CopyForge's own Reed-Solomon format, not PAR2). Each file gets a small sidecar in
/// .cf_parity next to the folder root. Blocks are spread over groups, so a damaged stretch of the disk is shared
/// out between many groups and each group only has to rebuild a few blocks.
/// </summary>
public static class Parity
{
	public const string DirName = ".cf_parity";

	private const int MaxBlock = 64 * 1024;

	private const int MaxData = 64;

	private static readonly byte[] Exp = new byte[512];

	private static readonly byte[] Log = new byte[256];

	private static readonly byte[][] Mul = new byte[256][];

	static Parity()
	{
		int x = 1;
		for (int i = 0; i < 255; i++)
		{
			Exp[i] = (byte)x;
			Log[x] = (byte)i;
			x <<= 1;
			if ((x & 0x100) != 0)
			{
				x ^= 0x11D;
			}
		}
		for (int i = 255; i < 512; i++)
		{
			Exp[i] = Exp[i - 255];
		}
		for (int a = 0; a < 256; a++)
		{
			Mul[a] = new byte[256];
			for (int b = 0; b < 256; b++)
			{
				Mul[a][b] = ((a == 0 || b == 0) ? (byte)0 : Exp[Log[a] + Log[b]]);
			}
		}
	}

	private static byte Inv(byte a)
	{
		return Exp[255 - Log[a]];
	}

	/// <summary>Cauchy matrix entry: any square piece of it can be inverted, so any m missing blocks can be rebuilt.</summary>
	private static byte Coef(int row, int col)
	{
		return Inv((byte)(row ^ (64 + col)));
	}

	private static string SidecarPath(string root, string file)
	{
		string rel = Path.GetRelativePath(root, file);
		return Path.Combine(root, DirName, rel + ".cfpar");
	}

	private static bool Skip(string root, string path)
	{
		string rel = Path.GetRelativePath(root, path);
		string first = rel.Split(Path.DirectorySeparatorChar)[0];
		if (first.Equals(DirName, StringComparison.OrdinalIgnoreCase) || first.Equals(".cf_versions", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string n = Path.GetFileName(path);
		return n.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".cftmp", StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> Files(string root)
	{
		EnumerationOptions opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
		foreach (string f in Directory.EnumerateFiles(root, "*", opt))
		{
			if (!Skip(root, f))
			{
				yield return f;
			}
		}
	}

	private sealed class Layout
	{
		public long Size;
		public int Bs = MaxBlock;
		public int N;
		public int G;
		public int K;
		public int M;
	}

	private static Layout MakeLayout(long size, int percent)
	{
		int bs = (int)Math.Min(MaxBlock, Math.Max(1L, size));
		Layout l = new Layout { Size = size, Bs = bs, N = (int)((size + bs - 1) / bs) };
		if (l.N == 0)
		{
			return l;
		}
		l.G = Math.Max(1, (l.N + MaxData - 1) / MaxData);
		l.K = (l.N + l.G - 1) / l.G;
		l.M = Math.Max(1, (int)Math.Ceiling(l.K * Math.Clamp(percent, 1, 50) / 100.0));
		return l;
	}

	private static int BlockLen(Layout l, int i)
	{
		long off = (long)i * l.Bs;
		return (int)Math.Min(l.Bs, l.Size - off);
	}

	private static uint Crc(byte[] b, int len)
	{
		return Crc32.HashToUInt32(new ReadOnlySpan<byte>(b, 0, len));
	}

	private static void ReadBlock(FileStream fs, Layout l, int i, byte[] buf)
	{
		Array.Clear(buf);
		int len = BlockLen(l, i);
		fs.Position = (long)i * l.Bs;
		int got = 0;
		while (got < len)
		{
			int r = fs.Read(buf, got, len - got);
			if (r <= 0)
			{
				break;
			}
			got += r;
		}
	}

	/// <summary>Writes repair data for every file under root. Files whose sidecar is already current are skipped.</summary>
	public static ParityResult Protect(string root, int percent, Action<string>? log, CancellationToken ct)
	{
		ParityResult res = new ParityResult(0, 0, 0, 0, 0, 0, 0, new List<string>());
		int files = 0, written = 0, skipped = 0;
		foreach (string f in Files(root))
		{
			ct.ThrowIfCancellationRequested();
			files++;
			try
			{
				FileInfo fi = new FileInfo(f);
				string sc = SidecarPath(root, f);
				if (File.Exists(sc) && SidecarMatches(sc, fi))
				{
					skipped++;
					continue;
				}
				WriteSidecar(f, sc, fi, percent);
				written++;
				log?.Invoke("parity " + Path.GetRelativePath(root, f));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				res.Problems.Add(f + ": " + ex.Message);
			}
		}
		return res with { Files = files, Written = written, Skipped = skipped };
	}

	private static bool SidecarMatches(string sc, FileInfo fi)
	{
		try
		{
			using FileStream s = File.OpenRead(sc);
			using BinaryReader r = new BinaryReader(s);
			if (Encoding.ASCII.GetString(r.ReadBytes(6)) != "CFPAR1")
			{
				return false;
			}
			long size = r.ReadInt64();
			long ticks = r.ReadInt64();
			return size == fi.Length && ticks == fi.LastWriteTimeUtc.Ticks;
		}
		catch
		{
			return false;
		}
	}

	private static void WriteSidecar(string file, string sidecar, FileInfo fi, int percent)
	{
		Layout l = MakeLayout(fi.Length, percent);
		Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
		string tmp = sidecar + ".tmp";
		uint[] dataCrc = new uint[l.N];
		byte[][][] par = new byte[l.G][][];
		uint[][] parCrc = new uint[l.G][];
		for (int g = 0; g < l.G; g++)
		{
			par[g] = new byte[l.M][];
			parCrc[g] = new uint[l.M];
			for (int r = 0; r < l.M; r++)
			{
				par[g][r] = new byte[l.Bs];
			}
		}
		byte[] buf = new byte[l.Bs];
		using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
		{
			for (int i = 0; i < l.N; i++)
			{
				ReadBlock(fs, l, i, buf);
				dataCrc[i] = Crc(buf, BlockLen(l, i));
				int g = i % l.G;
				int c = i / l.G;
				for (int r = 0; r < l.M; r++)
				{
					byte[] row = Mul[Coef(r, c)];
					byte[] p = par[g][r];
					for (int k = 0; k < l.Bs; k++)
					{
						p[k] ^= row[buf[k]];
					}
				}
			}
			if (fs.Length != fi.Length)
			{
				throw new IOException("file changed while reading");
			}
		}
		for (int g = 0; g < l.G; g++)
		{
			for (int r = 0; r < l.M; r++)
			{
				parCrc[g][r] = Crc(par[g][r], l.Bs);
			}
		}
		using (MemoryStream ms = new MemoryStream())
		{
			using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
			{
				w.Write(Encoding.ASCII.GetBytes("CFPAR1"));
				w.Write(fi.Length);
				w.Write(fi.LastWriteTimeUtc.Ticks);
				w.Write(l.Bs);
				w.Write(l.N);
				w.Write(l.G);
				w.Write(l.K);
				w.Write(l.M);
				for (int i = 0; i < l.N; i++)
				{
					w.Write(dataCrc[i]);
				}
				for (int g = 0; g < l.G; g++)
				{
					for (int r = 0; r < l.M; r++)
					{
						w.Write(parCrc[g][r]);
						w.Write(par[g][r]);
					}
				}
			}
			byte[] body = ms.ToArray();
			byte[] sum = SHA256.HashData(body);
			using FileStream o = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
			o.Write(body, 0, body.Length);
			o.Write(sum, 0, sum.Length);
		}
		File.Move(tmp, sidecar, overwrite: true);
	}

	private sealed class Sidecar
	{
		public Layout L = new Layout();
		public uint[] DataCrc = Array.Empty<uint>();
		public uint[][] ParCrc = Array.Empty<uint[]>();
		public byte[][][] Par = Array.Empty<byte[][]>();
	}

	private static Sidecar? ReadSidecar(string path)
	{
		byte[] all = File.ReadAllBytes(path);
		if (all.Length < 32 + 6)
		{
			return null;
		}
		byte[] body = new byte[all.Length - 32];
		Array.Copy(all, body, body.Length);
		byte[] sum = SHA256.HashData(body);
		for (int i = 0; i < 32; i++)
		{
			if (sum[i] != all[body.Length + i])
			{
				return null;
			}
		}
		using MemoryStream ms = new MemoryStream(body);
		using BinaryReader r = new BinaryReader(ms);
		if (Encoding.ASCII.GetString(r.ReadBytes(6)) != "CFPAR1")
		{
			return null;
		}
		Sidecar s = new Sidecar();
		s.L.Size = r.ReadInt64();
		r.ReadInt64();
		s.L.Bs = r.ReadInt32();
		s.L.N = r.ReadInt32();
		s.L.G = r.ReadInt32();
		s.L.K = r.ReadInt32();
		s.L.M = r.ReadInt32();
		s.DataCrc = new uint[s.L.N];
		for (int i = 0; i < s.L.N; i++)
		{
			s.DataCrc[i] = r.ReadUInt32();
		}
		s.Par = new byte[s.L.G][][];
		s.ParCrc = new uint[s.L.G][];
		for (int g = 0; g < s.L.G; g++)
		{
			s.Par[g] = new byte[s.L.M][];
			s.ParCrc[g] = new uint[s.L.M];
			for (int p = 0; p < s.L.M; p++)
			{
				s.ParCrc[g][p] = r.ReadUInt32();
				s.Par[g][p] = r.ReadBytes(s.L.Bs);
			}
		}
		return s;
	}

	/// <summary>Checks every file against its repair data and, unless checkOnly, rebuilds damaged blocks in place.</summary>
	public static ParityResult Repair(string root, bool checkOnly, Action<string>? log, CancellationToken ct)
	{
		int files = 0, damaged = 0, repaired = 0, bad = 0, nopar = 0;
		List<string> problems = new List<string>();
		foreach (string f in Files(root))
		{
			ct.ThrowIfCancellationRequested();
			files++;
			string sc = SidecarPath(root, f);
			string rel = Path.GetRelativePath(root, f);
			if (!File.Exists(sc))
			{
				nopar++;
				continue;
			}
			try
			{
				Sidecar? s = ReadSidecar(sc);
				if (s == null)
				{
					problems.Add(rel + ": the repair data itself is damaged");
					bad++;
					continue;
				}
				int outcome = CheckOne(f, s, checkOnly, out string msg);
				if (outcome == 1)
				{
					damaged++;
					repaired++;
					log?.Invoke(rel + ": " + msg);
				}
				else if (outcome == 2)
				{
					damaged++;
					bad++;
					problems.Add(rel + ": " + msg);
				}
				else if (outcome == 3)
				{
					damaged++;
					problems.Add(rel + ": " + msg);
					log?.Invoke(rel + ": " + msg);
				}
			}
			catch (Exception ex)
			{
				bad++;
				problems.Add(rel + ": " + ex.Message);
			}
		}
		return new ParityResult(files, 0, 0, damaged, repaired, bad, nopar, problems);
	}

	// 0 = fine, 1 = repaired, 2 = cannot repair, 3 = damaged (check only)
	private static int CheckOne(string file, Sidecar s, bool checkOnly, out string msg)
	{
		Layout l = s.L;
		msg = "";
		FileInfo fi = new FileInfo(file);
		if (fi.Length != l.Size)
		{
			msg = "size differs from when repair data was made (file was changed on purpose, or cut short)";
			return 2;
		}
		bool[] badBlock = new bool[l.N];
		int nBad = 0;
		byte[] buf = new byte[l.Bs];
		using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
		{
			for (int i = 0; i < l.N; i++)
			{
				bool ok;
				try
				{
					ReadBlock(fs, l, i, buf);
					ok = Crc(buf, BlockLen(l, i)) == s.DataCrc[i];
				}
				catch (IOException)
				{
					ok = false;
				}
				if (!ok)
				{
					badBlock[i] = true;
					nBad++;
				}
			}
		}
		if (nBad == 0)
		{
			return 0;
		}
		if (checkOnly)
		{
			msg = nBad + " damaged block(s) of " + l.N;
			return 3;
		}
		using FileStream rw = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		int fixedBlocks = 0;
		for (int g = 0; g < l.G; g++)
		{
			List<int> missing = new List<int>();
			for (int c = 0; c < l.K; c++)
			{
				int i = c * l.G + g;
				if (i < l.N && badBlock[i])
				{
					missing.Add(c);
				}
			}
			if (missing.Count == 0)
			{
				continue;
			}
			List<int> goodRows = new List<int>();
			for (int r = 0; r < l.M; r++)
			{
				if (Crc(s.Par[g][r], l.Bs) == s.ParCrc[g][r])
				{
					goodRows.Add(r);
				}
			}
			int e = missing.Count;
			if (goodRows.Count < e)
			{
				msg = "group " + g + " has " + e + " damaged blocks but only " + goodRows.Count + " usable repair block(s)";
				return 2;
			}
			// rhs[row] = parity row XOR contribution of all good data blocks
			byte[][] rhs = new byte[e][];
			for (int q = 0; q < e; q++)
			{
				rhs[q] = (byte[])s.Par[g][goodRows[q]].Clone();
			}
			for (int c = 0; c < l.K; c++)
			{
				int i = c * l.G + g;
				if (i >= l.N || badBlock[i])
				{
					continue;
				}
				ReadBlock(rw, l, i, buf);
				for (int q = 0; q < e; q++)
				{
					byte[] row = Mul[Coef(goodRows[q], c)];
					for (int k = 0; k < l.Bs; k++)
					{
						rhs[q][k] ^= row[buf[k]];
					}
				}
			}
			byte[][] a = new byte[e][];
			for (int q = 0; q < e; q++)
			{
				a[q] = new byte[e];
				for (int j = 0; j < e; j++)
				{
					a[q][j] = Coef(goodRows[q], missing[j]);
				}
			}
			// Gauss-Jordan over GF(256), applied to whole blocks
			for (int col = 0; col < e; col++)
			{
				int piv = col;
				while (piv < e && a[piv][col] == 0)
				{
					piv++;
				}
				if (piv == e)
				{
					msg = "repair matrix is singular";
					return 2;
				}
				(a[col], a[piv]) = (a[piv], a[col]);
				(rhs[col], rhs[piv]) = (rhs[piv], rhs[col]);
				byte iv = Inv(a[col][col]);
				for (int j = 0; j < e; j++)
				{
					a[col][j] = Mul[iv][a[col][j]];
				}
				for (int k = 0; k < l.Bs; k++)
				{
					rhs[col][k] = Mul[iv][rhs[col][k]];
				}
				for (int q = 0; q < e; q++)
				{
					if (q == col || a[q][col] == 0)
					{
						continue;
					}
					byte f = a[q][col];
					for (int j = 0; j < e; j++)
					{
						a[q][j] ^= Mul[f][a[col][j]];
					}
					byte[] m = Mul[f];
					for (int k = 0; k < l.Bs; k++)
					{
						rhs[q][k] ^= m[rhs[col][k]];
					}
				}
			}
			for (int j = 0; j < e; j++)
			{
				int i = missing[j] * l.G + g;
				int len = BlockLen(l, i);
				if (Crc(rhs[j], len) != s.DataCrc[i])
				{
					msg = "block " + i + " could not be rebuilt correctly";
					return 2;
				}
				rw.Position = (long)i * l.Bs;
				rw.Write(rhs[j], 0, len);
				fixedBlocks++;
			}
		}
		rw.Flush(flushToDisk: true);
		msg = "repaired " + fixedBlocks + " block(s)";
		return 1;
	}
}
