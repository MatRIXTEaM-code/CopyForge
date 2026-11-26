using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopyForge.Engine;

internal sealed class ShadowSet : IDisposable
{
	private static readonly Regex IdRx = new Regex("^\\{[0-9A-Fa-f\\-]{36}\\}$", RegexOptions.Compiled);

	private static readonly Regex DevRx = new Regex("^\\\\\\\\\\?\\\\GLOBALROOT\\\\Device\\\\HarddiskVolumeShadowCopy\\d+$", RegexOptions.Compiled);

	private static readonly Regex VolRx = new Regex("^[A-Za-z]:\\\\$", RegexOptions.Compiled);

	private readonly object _gate = new object();

	private readonly Dictionary<string, (string id, string device)?> _vols = new Dictionary<string, (string, string)?>(StringComparer.OrdinalIgnoreCase);

	private readonly Action<string> _info;

	public ShadowSet(Action<string> info)
	{
		_info = info;
	}

	public static bool Available(out string why)
	{
		why = "";
		if (!OperatingSystem.IsWindows())
		{
			why = "Volume Shadow Copy is Windows only.";
			return false;
		}
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			if (!new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator))
			{
				why = "Volume Shadow Copy needs an elevated (administrator) process.";
				return false;
			}
		}
		catch (Exception ex)
		{
			why = ex.Message;
			return false;
		}
		return true;
	}

	public string? Map(string path)
	{
		try
		{
			string fullPath = Path.GetFullPath(path);
			string text = Path.GetPathRoot(fullPath) ?? "";
			if (!VolRx.IsMatch(text))
			{
				return null;
			}
			(string, string)? value;
			lock (_gate)
			{
				if (!_vols.TryGetValue(text, out value))
				{
					if (!Available(out string why))
					{
						_info("Shadow copy not available: " + why);
						value = null;
					}
					else
					{
						value = Create(text);
					}
					_vols[text] = value;
				}
			}
			return (!value.HasValue) ? null : (value.Value.Item2 + "\\" + fullPath.Substring(text.Length));
		}
		catch
		{
			return null;
		}
	}

	private (string id, string device)? Create(string root)
	{
		_info("Creating a shadow copy of " + root + " to read locked files...");
		string[] array = RunPs("$ErrorActionPreference='Stop';$r=Invoke-CimMethod -ClassName Win32_ShadowCopy -MethodName Create -Arguments @{Volume='" + root + "';Context='ClientAccessible'};if($r.ReturnValue -ne 0){exit 2};$s=Get-CimInstance Win32_ShadowCopy | Where-Object {$_.ID -eq $r.ShadowID};Write-Output ($s.ID+'|'+$s.DeviceObject)", 90)?.Trim().Split('|');
		if (array != null && array.Length == 2 && IdRx.IsMatch(array[0]) && DevRx.IsMatch(array[1]))
		{
			_info("Shadow copy ready.");
			return (array[0], array[1]);
		}
		_info("Could not create a shadow copy; the locked file will be reported as failed.");
		return null;
	}

	private static string? RunPs(string script, int timeoutSec)
	{
		using Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }
		});
		if (process == null)
		{
			return null;
		}
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		if (!process.WaitForExit(timeoutSec * 1000))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			return null;
		}
		return (process.ExitCode == 0) ? task.Result : null;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			foreach ((string, string)? value in _vols.Values)
			{
				if (value.HasValue && IdRx.IsMatch(value.Value.Item1))
				{
					try
					{
						RunPs("Get-CimInstance Win32_ShadowCopy | Where-Object {$_.ID -eq '" + value.Value.Item1 + "'} | Remove-CimInstance", 60);
					}
					catch
					{
					}
				}
			}
			_vols.Clear();
		}
	}
}
