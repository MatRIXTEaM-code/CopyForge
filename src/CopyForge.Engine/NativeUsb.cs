using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CopyForge.Engine;

/// <summary>What Windows reports about the bus a drive is on (USB or not, and the bus version if it says).</summary>
internal readonly record struct BusInfo(bool Known, int BusType, int Major, int Minor)
{
	public bool IsUsb => Known && BusType == 7;

	public bool IsUsb2OrOlder => IsUsb && Major >= 1 && Major < 3;

	public string Describe()
	{
		if (!IsUsb)
		{
			return "";
		}
		if (Major <= 0)
		{
			return "USB (version not reported by Windows)";
		}
		return (Minor > 0 && Major >= 3) ? $"USB {Major}.{Minor}" : $"USB {Major}.0";
	}
}

internal static partial class Native
{
	private const uint FSCTL_LOCK_VOLUME = 589848u;

	private const uint FSCTL_DISMOUNT_VOLUME = 589856u;

	private const uint IOCTL_STORAGE_MEDIA_REMOVAL = 2967556u;

	private const uint IOCTL_STORAGE_EJECT_MEDIA = 2967560u;

	private static readonly ConcurrentDictionary<string, BusInfo> BusCache = new ConcurrentDictionary<string, BusInfo>(StringComparer.OrdinalIgnoreCase);

	[DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
	private static extern bool DeviceIoControlRaw(SafeFileHandle h, uint code, byte[]? inBuf, int inSize, byte[]? outBuf, int outSize, out int returned, nint overlapped);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FlushFileBuffers(SafeFileHandle h);

	/// <summary>Asks Windows which bus the drive of <paramref name="path"/> is on. Never throws; unknown is Known = false.</summary>
	public static BusInfo ProbeBus(string path)
	{
		if (!IsWindows)
		{
			return default(BusInfo);
		}
		string root = "";
		try
		{
			root = Path.GetPathRoot(path) ?? "";
		}
		catch
		{
		}
		if (root.Length < 2 || root[1] != ':')
		{
			return default(BusInfo);
		}
		return BusCache.GetOrAdd(root, delegate(string r)
		{
			try
			{
				using SafeFileHandle h = OpenRaw($"\\\\.\\{char.ToUpperInvariant(r[0])}:", 0u, 3u, 3u, 0u);
				byte[] query = new byte[16];
				query[0] = 1;
				byte[] output = new byte[64];
				if (!DeviceIoControlRaw(h, 2954240u, query, query.Length, output, output.Length, out var returned, IntPtr.Zero) || returned < 30)
				{
					return default(BusInfo);
				}
				return new BusInfo(Known: true, output[24], BitConverter.ToUInt16(output, 26), BitConverter.ToUInt16(output, 28));
			}
			catch
			{
				return default(BusInfo);
			}
		});
	}

	/// <summary>
	/// "Safely remove" a removable drive: flush, lock, dismount, then eject. Only for DriveType.Removable.
	/// Returns true when it is safe to unplug; <paramref name="message"/> says what happened.
	/// </summary>
	public static bool EjectVolume(string path, out string message, int waitSeconds = 3, System.Threading.CancellationToken ct = default)
	{
		message = "";
		if (!IsWindows)
		{
			message = "eject is only available on Windows";
			return false;
		}
		string root = Path.GetPathRoot(path) ?? "";
		if (root.Length < 2 || root[1] != ':')
		{
			message = "not a drive letter";
			return false;
		}
		try
		{
			if (new DriveInfo(root).DriveType != DriveType.Removable)
			{
				message = root + " is not a removable drive, so it was not ejected";
				return false;
			}
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
		string vol = $"\\\\.\\{char.ToUpperInvariant(root[0])}:";
		try
		{
			using SafeFileHandle h = OpenRaw(vol, 3221225472u, 3u, 3u, 0u);
			FlushFileBuffers(h);
			bool locked = false;
			int tries = Math.Max(1, waitSeconds * 2);
			for (int i = 0; i < tries && !locked && !ct.IsCancellationRequested; i++)
			{
				locked = DeviceIoControlRaw(h, FSCTL_LOCK_VOLUME, null, 0, null, 0, out var _, IntPtr.Zero);
				if (!locked)
				{
					Thread.Sleep(500);
				}
			}
			if (!locked && ct.IsCancellationRequested)
			{
				message = "Eject cancelled. Nothing was dismounted.";
				return false;
			}
			if (!locked)
			{
				message = root + $" is still in use after {waitSeconds} s (an Explorer window, an open file, antivirus or the search indexer). Nothing was dismounted. Close what is using it and eject from the tray icon, or look for D: in Resource Monitor > CPU > Associated Handles.".Replace("D:", root.TrimEnd('\\'));
				return false;
			}
			if (!DeviceIoControlRaw(h, FSCTL_DISMOUNT_VOLUME, null, 0, null, 0, out var _, IntPtr.Zero))
			{
				message = "could not dismount " + root + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message.TrimEnd('.', ' ');
				return false;
			}
			byte[] allow = new byte[1];
			DeviceIoControlRaw(h, IOCTL_STORAGE_MEDIA_REMOVAL, allow, allow.Length, null, 0, out var _, IntPtr.Zero);
			if (DeviceIoControlRaw(h, IOCTL_STORAGE_EJECT_MEDIA, null, 0, null, 0, out var _, IntPtr.Zero))
			{
				message = root + " ejected - safe to unplug.";
			}
			else
			{
				message = root + " flushed and dismounted - safe to unplug (Windows did not offer an eject command for this device).";
			}
			return true;
		}
		catch (Exception ex2)
		{
			message = "could not eject " + root + ": " + ex2.Message.TrimEnd('.', ' ');
			return false;
		}
	}
}
