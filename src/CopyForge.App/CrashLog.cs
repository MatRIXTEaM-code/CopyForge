using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CopyForge.App;

internal static class CrashLog
{
	private static readonly object Gate = new object();

	private static readonly List<string> Entries = new List<string>();

	public static string FilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyForge", "crash.log");


	public static IReadOnlyList<string> Recent()
	{
		lock (Gate)
		{
			return Entries.ToList();
		}
	}

	public static void Log(string where, Exception? ex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").Append(where)
			.Append(": ")
			.Append(ex?.GetType().Name ?? "unknown")
			.Append(": ")
			.Append(ex?.Message ?? "")
			.Append('\n')
			.Append(ex?.StackTrace ?? "");
		string text = stringBuilder.ToString();
		lock (Gate)
		{
			Entries.Add(text);
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				try
				{
					FileInfo fi = new FileInfo(FilePath);
					if (fi.Exists && fi.Length > 1048576L)
					{
						string old = FilePath + ".old";
						if (File.Exists(old))
						{
							File.Delete(old);
						}
						File.Move(FilePath, old);
					}
				}
				catch
				{
				}
				File.AppendAllText(FilePath, text.Replace("\n", Environment.NewLine) + Environment.NewLine + Environment.NewLine);
			}
			catch
			{
			}
		}
	}
}
