using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CopyForge.Engine;

internal static partial class Native
{
	private struct STORAGE_PROPERTY_QUERY
	{
		public int PropertyId;

		public int QueryType;

		public byte Extra;
	}

	private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
	{
		public uint Version;

		public uint Size;

		public byte IncursSeekPenalty;
	}

	private delegate uint CopyProgressRoutine(long total, long transferred, long streamSize, long streamTransferred, uint streamNumber, uint reason, nint hSrc, nint hDst, nint data);

	private struct FILE_BASIC_INFO
	{
		public long CreationTime;

		public long LastAccessTime;

		public long LastWriteTime;

		public long ChangeTime;

		public uint FileAttributes;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WIN32_FIND_STREAM_DATA
	{
		public long StreamSize;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
		public string StreamName;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHFILEOPSTRUCT
	{
		public nint hwnd;

		public uint wFunc;

		public string pFrom;

		public string? pTo;

		public ushort fFlags;

		public int fAnyOperationsAborted;

		public nint hNameMappings;

		public string? lpszProgressTitle;
	}

	public static readonly bool IsWindows = OperatingSystem.IsWindows();

	private static readonly ConcurrentDictionary<string, VolInfo> VolCache = new ConcurrentDictionary<string, VolInfo>(StringComparer.OrdinalIgnoreCase);

	private const FileAttributes CopyableAttrs = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.Temporary | FileAttributes.NotContentIndexed;

	public static string Long(string p)
	{
		if (!IsWindows || p.Length < 240 || p.StartsWith("\\\\?\\", StringComparison.Ordinal))
		{
			return p;
		}
		if (p.StartsWith("\\\\", StringComparison.Ordinal))
		{
			return "\\\\?\\UNC\\" + p.Substring(2);
		}
		return "\\\\?\\" + p;
	}

	private static IOException Win32Error(int err)
	{
		return new IOException(new Win32Exception(err).Message, -2147024896 | err);
	}

	public static VolInfo Probe(string path)
	{
		string key = "";
		try
		{
			key = Path.GetPathRoot(path) ?? "";
		}
		catch
		{
		}
		return VolCache.GetOrAdd(key, ProbeRoot);
	}

	private static VolInfo ProbeRoot(string root)
	{
		bool flag = root.StartsWith("\\\\", StringComparison.Ordinal) && !root.StartsWith("\\\\?\\", StringComparison.Ordinal);
		bool removable = false;
		string fs = "";
		try
		{
			if (root.Length > 0 && !flag)
			{
				DriveInfo driveInfo = new DriveInfo(root);
				if (driveInfo.DriveType == DriveType.Network)
				{
					flag = true;
				}
				if (driveInfo.DriveType == DriveType.Removable)
				{
					removable = true;
				}
				if (driveInfo.IsReady)
				{
					fs = driveInfo.DriveFormat;
				}
			}
		}
		catch
		{
		}
		bool solidKnown = false;
		bool solid = true;
		if (IsWindows && !flag && root.Length >= 2 && root[1] == ':')
		{
			try
			{
				solidKnown = TryIsSolidState(root[0], out solid);
			}
			catch
			{
				solidKnown = false;
			}
		}
		return new VolInfo(flag, removable, solid, solidKnown, fs);
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFileW(string name, uint access, uint share, nint sec, uint disp, uint flags, nint tmpl);

	private static SafeFileHandle OpenRaw(string path, uint access, uint share, uint disp, uint flags)
	{
		nint num = CreateFileW(path, access, share, IntPtr.Zero, disp, flags, IntPtr.Zero);
		if (num == new IntPtr(-1))
		{
			throw Win32Error(Marshal.GetLastWin32Error());
		}
		return new SafeFileHandle(num, ownsHandle: true);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref STORAGE_PROPERTY_QUERY inBuf, int inSize, out DEVICE_SEEK_PENALTY_DESCRIPTOR outBuf, int outSize, out int returned, nint overlapped);

	private static bool TryIsSolidState(char drive, out bool solid)
	{
		solid = true;
		using SafeFileHandle h = OpenRaw($"\\\\.\\{char.ToUpperInvariant(drive)}:", 0u, 3u, 3u, 0u);
		STORAGE_PROPERTY_QUERY sTORAGE_PROPERTY_QUERY = default(STORAGE_PROPERTY_QUERY);
		sTORAGE_PROPERTY_QUERY.PropertyId = 7;
		sTORAGE_PROPERTY_QUERY.QueryType = 0;
		STORAGE_PROPERTY_QUERY inBuf = sTORAGE_PROPERTY_QUERY;
		if (!DeviceIoControl(h, 2954240u, ref inBuf, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), out var outBuf, Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), out var _, IntPtr.Zero))
		{
			return false;
		}
		solid = outBuf.IncursSeekPenalty == 0;
		return true;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CopyFileExW", SetLastError = true)]
	private static extern bool CopyFileEx(string src, string dst, CopyProgressRoutine? cb, nint data, nint cancel, uint flags);

	public static bool CopyFile(string src, string dst, bool noBuffering, Func<long, bool> progress, bool failIfExists = false)
	{
		Func<long, bool> progress2 = progress;
		long last = 0L;
		CopyProgressRoutine copyProgressRoutine = delegate(long total, long transferred, long ss, long st, uint sn, uint reason, nint hs, nint hd, nint d)
		{
			long arg = transferred - last;
			last = transferred;
			return (!progress2(arg)) ? 1u : 0u;
		};
		bool num = CopyFileEx(Long(src), Long(dst), copyProgressRoutine, IntPtr.Zero, IntPtr.Zero, (noBuffering ? 4096u : 0u) | (failIfExists ? 1u : 0u));
		int lastWin32Error = Marshal.GetLastWin32Error();
		GC.KeepAlive(copyProgressRoutine);
		if (num)
		{
			return true;
		}
		bool flag = failIfExists;
		if (flag)
		{
			bool flag2 = ((lastWin32Error == 80 || lastWin32Error == 183) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return false;
		}
		if (lastWin32Error == 1235)
		{
			throw new OperationCanceledException();
		}
		throw Win32Error(lastWin32Error);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref FILE_BASIC_INFO info, uint size);

	public static bool SetBasicInfo(SafeFileHandle h, DateTime creationUtc, DateTime writeUtc, FileAttributes attrs)
	{
		if (!IsWindows)
		{
			return false;
		}
		try
		{
			FileAttributes fileAttributes = attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.Temporary | FileAttributes.NotContentIndexed);
			FILE_BASIC_INFO fILE_BASIC_INFO = default(FILE_BASIC_INFO);
			fILE_BASIC_INFO.CreationTime = ((creationUtc.Year > 1700) ? creationUtc.ToFileTimeUtc() : 0);
			fILE_BASIC_INFO.LastWriteTime = writeUtc.ToFileTimeUtc();
			fILE_BASIC_INFO.FileAttributes = (uint)((fileAttributes == FileAttributes.None) ? FileAttributes.Normal : fileAttributes);
			FILE_BASIC_INFO info = fILE_BASIC_INFO;
			return SetFileInformationByHandle(h, 0, ref info, (uint)Marshal.SizeOf<FILE_BASIC_INFO>());
		}
		catch
		{
			return false;
		}
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetFileSecurityW", SetLastError = true)]
	private static extern bool GetFileSecurity(string file, uint info, byte[]? sd, uint len, out uint needed);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetFileSecurityW", SetLastError = true)]
	private static extern bool SetFileSecurity(string file, uint info, byte[] sd);

	public static void CopySecurity(string src, string dst)
	{
		uint[] obj = new uint[3] { 7u, 6u, 4u };
		Exception ex = null;
		uint[] array = obj;
		foreach (uint info in array)
		{
			try
			{
				GetFileSecurity(Long(src), info, null, 0u, out var needed);
				if (needed == 0)
				{
					throw Win32Error(Marshal.GetLastWin32Error());
				}
				byte[] sd = new byte[needed];
				if (!GetFileSecurity(Long(src), info, sd, needed, out var _))
				{
					throw Win32Error(Marshal.GetLastWin32Error());
				}
				if (!SetFileSecurity(Long(dst), info, sd))
				{
					throw Win32Error(Marshal.GetLastWin32Error());
				}
				return;
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
		}
		throw ex ?? new IOException("Could not copy permissions.");
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindFirstStreamW", SetLastError = true)]
	private static extern nint FindFirstStream(string file, int level, out WIN32_FIND_STREAM_DATA data, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindNextStreamW", SetLastError = true)]
	private static extern bool FindNextStream(nint h, out WIN32_FIND_STREAM_DATA data);

	[DllImport("kernel32.dll")]
	private static extern bool FindClose(nint h);

	public static int CopyStreams(string src, string dst, bool zoneOnly = false)
	{
		List<string> list = new List<string>();
		WIN32_FIND_STREAM_DATA data;
		nint num = FindFirstStream(Long(src), 0, out data, 0u);
		if (num == new IntPtr(-1))
		{
			return 0;
		}
		try
		{
			do
			{
				if (!(data.StreamName == "::$DATA") && (!zoneOnly || data.StreamName.StartsWith(":Zone.Identifier", StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(data.StreamName);
				}
			}
			while (FindNextStream(num, out data));
		}
		finally
		{
			FindClose(num);
		}
		int num2 = 0;
		foreach (string item in list)
		{
			string text;
			if (!item.EndsWith(":$DATA", StringComparison.Ordinal))
			{
				text = item;
			}
			else
			{
				string text2 = item;
				text = text2.Substring(0, text2.Length - 6);
			}
			string text3 = text;
			using SafeFileHandle handle = OpenRaw(Long(src) + text3, 2147483648u, 7u, 3u, 0u);
			using SafeFileHandle handle2 = OpenRaw(Long(dst) + text3, 1073741824u, 0u, 2u, 0u);
			using FileStream fileStream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
			using FileStream destination = new FileStream(handle2, FileAccess.Write, 4096, isAsync: false);
			fileStream.CopyTo(destination);
			num2++;
		}
		return num2;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
	private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

	public static void Recycle(string path)
	{
		SHFILEOPSTRUCT sHFILEOPSTRUCT = default(SHFILEOPSTRUCT);
		sHFILEOPSTRUCT.wFunc = 3u;
		sHFILEOPSTRUCT.pFrom = path + "\0";
		sHFILEOPSTRUCT.fFlags = 1108;
		SHFILEOPSTRUCT op = sHFILEOPSTRUCT;
		int num = SHFileOperation(ref op);
		if (num != 0 || op.fAnyOperationsAborted != 0)
		{
			throw new IOException($"Could not move to the Recycle Bin (code 0x{num:X}).");
		}
	}
}
