using System;
using Microsoft.Win32;

namespace CopyForge.App;

internal static class ShellMenu
{
	private static readonly string[] Keys = new string[2] { "Software\\Classes\\*\\shell\\CopyForge", "Software\\Classes\\Directory\\shell\\CopyForge" };

	public static bool IsInstalled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(Keys[0] + "\\command");
			return registryKey != null;
		}
		catch
		{
			return false;
		}
	}

	public static void Install()
	{
		string text = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the program path.");
		string[] keys = Keys;
		foreach (string subkey in keys)
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(subkey);
			registryKey.SetValue("", "Add to CopyForge");
			registryKey.SetValue("Icon", "\"" + text + "\"");
			using RegistryKey registryKey2 = registryKey.CreateSubKey("command");
			registryKey2.SetValue("", "\"" + text + "\" \"%1\"");
		}
	}

	public static void Remove()
	{
		string[] keys = Keys;
		foreach (string subkey in keys)
		{
			try
			{
				Registry.CurrentUser.DeleteSubKeyTree(subkey, throwOnMissingSubKey: false);
			}
			catch
			{
			}
		}
	}
}
