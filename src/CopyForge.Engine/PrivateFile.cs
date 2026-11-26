using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CopyForge.Engine;

/// <summary>Locks a secret file (private key, shared secret) down to the current user. Best effort; never throws.</summary>
public static class PrivateFile
{
	public static void Restrict(string path)
	{
		if (!OperatingSystem.IsWindows())
		{
			try
			{
				File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			catch
			{
			}
			return;
		}
		try
		{
			using WindowsIdentity me = WindowsIdentity.GetCurrent();
			FileInfo fi = new FileInfo(path);
			FileSecurity sec = new FileSecurity();
			sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
			sec.AddAccessRule(new FileSystemAccessRule(me.User, FileSystemRights.FullControl, AccessControlType.Allow));
			fi.SetAccessControl(sec);
		}
		catch
		{
		}
	}
}
