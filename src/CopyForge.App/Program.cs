using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CopyForge.App;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		using SingleInstance singleInstance = new SingleInstance();
		if (!singleInstance.IsFirst && args.Length != 0 && SingleInstance.Forward(args))
		{
			return;
		}
		ApplicationConfiguration.Initialize();
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += delegate(object _, ThreadExceptionEventArgs e)
		{
			CrashLog.Log("UI thread", e.Exception);
			MessageBox.Show("An unexpected error occurred:\n\n" + e.Exception.Message + "\n\nDetails were saved. Open Diagnostics (Ctrl+D) to view them.", "CopyForge", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e)
		{
			CrashLog.Log("Unhandled", e.ExceptionObject as Exception);
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs e)
		{
			CrashLog.Log("Background task", e.Exception);
			e.SetObserved();
		};
		MainForm form = new MainForm(args);
		if (singleInstance.IsFirst)
		{
			singleInstance.Listen(delegate(string[] paths)
			{
				string[] paths2 = paths;
				try
				{
					if (form.IsHandleCreated)
					{
						form.BeginInvoke(delegate
						{
							form.AddPaths(paths2);
						});
					}
				}
				catch
				{
				}
			});
		}
		Application.Run(form);
	}
}
