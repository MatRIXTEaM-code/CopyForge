using System;
using System.Linq;
using Microsoft.Win32;

namespace CopyForge.App;

internal static class ShellMenu
{
	private static readonly string[] Keys = new string[2] { "Software\\Classes\\*\\shell\\CopyForge", "Software\\Classes\\Directory\\shell\\CopyForge" };

	private static readonly string[] SendKeys = new string[2] { "Software\\Classes\\*\\shell\\CopyForgeSend", "Software\\Classes\\Directory\\shell\\CopyForgeSend" };

	private static readonly string[] FavKeys = new string[2] { "Software\\Classes\\*\\shell\\CopyForgeSendFav", "Software\\Classes\\Directory\\shell\\CopyForgeSendFav" };

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
		foreach (string subkey2 in SendKeys)
		{
			using RegistryKey registryKey3 = Registry.CurrentUser.CreateSubKey(subkey2);
			registryKey3.SetValue("", "Send with LocalSend (CopyForge)");
			registryKey3.SetValue("Icon", "\"" + text + "\"");
			using RegistryKey registryKey4 = registryKey3.CreateSubKey("command");
			registryKey4.SetValue("", "\"" + text + "\" --localsend \"%1\"");
		}
		foreach (string subkey3 in FavKeys)
		{
			using RegistryKey registryKey5 = Registry.CurrentUser.CreateSubKey(subkey3);
			registryKey5.SetValue("", "Send to favourite LocalSend device (CopyForge)");
			registryKey5.SetValue("Icon", "\"" + text + "\"");
			using RegistryKey registryKey6 = registryKey5.CreateSubKey("command");
			registryKey6.SetValue("", "\"" + text + "\" --sendfav \"%1\"");
		}
	}

	public static void Remove()
	{
		foreach (string subkey in Keys.Concat(SendKeys).Concat(FavKeys))
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
