using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CopyForge.App;

internal static class NativeUi
{
	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint hwnd, string? subApp, string? subIdList);

	public static void DarkTitleBar(Form f)
	{
		Form f2 = f;
		if (f2.IsHandleCreated)
		{
			Apply();
			return;
		}
		f2.HandleCreated += delegate
		{
			Apply();
		};
		void Apply()
		{
			try
			{
				int value = 1;
				DwmSetWindowAttribute(f2.Handle, 20, ref value, 4);
			}
			catch
			{
			}
		}
	}

	public static void DarkScroll(Control c)
	{
		Control c2 = c;
		if (c2.IsHandleCreated)
		{
			Apply();
			return;
		}
		c2.HandleCreated += delegate
		{
			Apply();
		};
		void Apply()
		{
			try
			{
				SetWindowTheme(c2.Handle, "DarkMode_Explorer", null);
			}
			catch
			{
			}
		}
	}
}
