using System;

namespace CopyForge.Engine;

/// <summary>Public entry point for "safely remove" so the app can retry an eject after the user closes whatever held the drive.</summary>
public static class DriveEject
{
	public static bool Eject(string path, out string message, int waitSeconds = 10)
	{
		return Native.EjectVolume(path, out message, waitSeconds);
	}

	public static string Root(string path)
	{
		return System.IO.Path.GetPathRoot(path) ?? path;
	}
}
