using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CopyForge.Engine;

internal static partial class Native
{
	private const uint FSCTL_DUPLICATE_EXTENTS_TO_FILE = 622404u;

	private const long CloneChunk = 1073741824L;

	[StructLayout(LayoutKind.Sequential)]
	private struct DUPLICATE_EXTENTS_DATA
	{
		public nint FileHandle;

		public long SourceFileOffset;

		public long TargetFileOffset;

		public long ByteCount;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref DUPLICATE_EXTENTS_DATA inBuf, int inSize, nint outBuf, int outSize, out int returned, nint overlapped);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceW", SetLastError = true)]
	private static extern bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

	/// <summary>Cluster size of the volume that holds <paramref name="path"/>, or 0 if unknown.</summary>
	public static long ClusterSize(string path)
	{
		if (!IsWindows)
		{
			return 0L;
		}
		try
		{
			string root = Path.GetPathRoot(path);
			if (string.IsNullOrEmpty(root))
			{
				return 0L;
			}
			if (GetDiskFreeSpace(root, out var spc, out var bps, out var _, out var _))
			{
				return (long)spc * bps;
			}
		}
		catch
		{
		}
		return 0L;
	}

	/// <summary>
	/// Copies <paramref name="src"/> to <paramref name="dst"/> by ReFS block cloning (a metadata operation).
	/// Whole clusters are cloned; the last partial cluster is copied normally. On any failure the
	/// partly written destination is removed and false is returned so the caller can copy normally.
	/// </summary>
	public static bool TryCloneFile(string src, string dst, long size, out string why)
	{
		why = "";
		if (!IsWindows || size <= 0)
		{
			why = "not applicable";
			return false;
		}
		long cluster = ClusterSize(dst);
		if (cluster <= 0 || cluster > 1048576)
		{
			why = "cluster size unknown";
			return false;
		}
		SafeFileHandle s = null;
		SafeFileHandle d = null;
		try
		{
			s = File.OpenHandle(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.None);
			d = File.OpenHandle(dst, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.None);
			RandomAccess.SetLength(d, size);
			long whole = size / cluster * cluster;
			for (long off = 0L; off < whole; off += CloneChunk)
			{
				long n = Math.Min(CloneChunk, whole - off);
				DUPLICATE_EXTENTS_DATA data = new DUPLICATE_EXTENTS_DATA
				{
					FileHandle = s.DangerousGetHandle(),
					SourceFileOffset = off,
					TargetFileOffset = off,
					ByteCount = n
				};
				if (!DeviceIoControl(d, FSCTL_DUPLICATE_EXTENTS_TO_FILE, ref data, Marshal.SizeOf<DUPLICATE_EXTENTS_DATA>(), IntPtr.Zero, 0, out var _, IntPtr.Zero))
				{
					int err = Marshal.GetLastWin32Error();
					why = new Win32Exception(err).Message.TrimEnd('.', ' ') + $" (error {err})";
					throw new IOException(why);
				}
			}
			if (whole < size)
			{
				byte[] tail = new byte[size - whole];
				int got = 0;
				while (got < tail.Length)
				{
					int r = RandomAccess.Read(s, tail.AsSpan(got), whole + got);
					if (r == 0)
					{
						break;
					}
					got += r;
				}
				if (got != tail.Length)
				{
					why = "source changed size while cloning";
					throw new IOException(why);
				}
				RandomAccess.Write(d, tail, whole);
			}
			RandomAccess.FlushToDisk(d);
			return true;
		}
		catch (Exception ex)
		{
			if (why.Length == 0)
			{
				why = ex.Message.TrimEnd('.', ' ');
			}
			d?.Dispose();
			d = null;
			try
			{
				File.Delete(dst);
			}
			catch
			{
			}
			return false;
		}
		finally
		{
			s?.Dispose();
			d?.Dispose();
		}
	}
}
