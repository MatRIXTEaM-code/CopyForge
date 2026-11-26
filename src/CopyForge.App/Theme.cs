using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace CopyForge.App;

internal static class Theme
{
	public static readonly Color Bg = Color.FromArgb(4, 8, 12);

	public static readonly Color Panel = Color.FromArgb(8, 17, 23);

	public static readonly Color Field = Color.FromArgb(5, 12, 17);

	public static readonly Color Line = Color.FromArgb(28, 66, 77);

	public static readonly Color LineHi = Color.FromArgb(86, 190, 205);

	public static readonly Color Accent = Color.FromArgb(110, 231, 242);

	public static readonly Color Dim = Color.FromArgb(76, 124, 134);

	public static readonly Color Text = Color.FromArgb(184, 222, 228);

	public static readonly Color Warn = Color.FromArgb(255, 179, 71);

	public static readonly Color Bad = Color.FromArgb(255, 82, 100);

	public static readonly Color Ok = Color.FromArgb(96, 255, 178);

	private static readonly string DisplayFamily = Pick("Bahnschrift", "Segoe UI Semibold", "Segoe UI");

	private static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

	public static readonly Font FLabel = Display(7.5f, FontStyle.Bold);

	public static readonly Font FSmall = Mono(8f);

	public static readonly Font FBody = Mono(9.5f);

	public static readonly Font FBtn = Display(9f, FontStyle.Bold);

	public static readonly Font FValue = Display(15f, FontStyle.Bold);

	public static readonly Font FBig = Display(34f, FontStyle.Bold);

	public static readonly Font FGauge = Display(40f, FontStyle.Bold);

	public static readonly Font FGauge3 = Display(31f, FontStyle.Bold);

	public static readonly Font FPct = Display(14f, FontStyle.Bold);

	public static readonly Font FLogo = Display(17f, FontStyle.Bold);

	public static readonly StringFormat SfCenter = new StringFormat
	{
		Alignment = StringAlignment.Center,
		LineAlignment = StringAlignment.Center,
		FormatFlags = StringFormatFlags.NoWrap,
		Trimming = StringTrimming.EllipsisCharacter
	};

	public static readonly StringFormat SfLeft = new StringFormat
	{
		Alignment = StringAlignment.Near,
		LineAlignment = StringAlignment.Center,
		FormatFlags = StringFormatFlags.NoWrap,
		Trimming = StringTrimming.EllipsisCharacter
	};

	public static readonly StringFormat SfRight = new StringFormat
	{
		Alignment = StringAlignment.Far,
		LineAlignment = StringAlignment.Center,
		FormatFlags = StringFormatFlags.NoWrap,
		Trimming = StringTrimming.EllipsisCharacter
	};

	private static string Pick(params string[] names)
	{
		HashSet<string> hashSet = new HashSet<string>(FontFamily.Families.Select((FontFamily f) => f.Name), StringComparer.OrdinalIgnoreCase);
		foreach (string text in names)
		{
			if (hashSet.Contains(text))
			{
				return text;
			}
		}
		return "Segoe UI";
	}

	public static Font Display(float pt, FontStyle st = FontStyle.Regular)
	{
		return new Font(DisplayFamily, pt, st, GraphicsUnit.Point);
	}

	public static Font Mono(float pt, FontStyle st = FontStyle.Regular)
	{
		return new Font(MonoFamily, pt, st, GraphicsUnit.Point);
	}

	public static Color A(Color c, int alpha)
	{
		return Color.FromArgb(Math.Clamp(alpha, 0, 255), c);
	}

	public static void Quality(Graphics g)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
	}

	public static GraphicsPath Chamfer(RectangleF r, float c)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddPolygon(new PointF(r.Left + c, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top + c));
		return graphicsPath;
	}

	public static void Glow(Graphics g, GraphicsPath p, Color c, float w)
	{
		for (int num = 3; num >= 1; num--)
		{
			using Pen pen = new Pen(A(c, 20), w + (float)(num * 3))
			{
				LineJoin = LineJoin.Round
			};
			g.DrawPath(pen, p);
		}
	}

	public static void DrawSpaced(Graphics g, string s, Font f, Color c, float x, float y, float spacing)
	{
		using SolidBrush brush = new SolidBrush(c);
		for (int i = 0; i < s.Length; i++)
		{
			char c2 = s[i];
			string text = c2.ToString();
			g.DrawString(text, f, brush, x, y, StringFormat.GenericTypographic);
			float num = ((c2 == ' ') ? (f.Size * 0.5f) : g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width);
			x += num + spacing;
		}
	}

	public static string Bytes(long b)
	{
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		double num = Math.Max(b, 0L);
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{num:0} {array[num2]}";
		}
		return $"{num:0.##} {array[num2]}";
	}

	public static void RateParts(double bps, out string value, out string unit)
	{
		string[] array = new string[4] { "B/s", "KB/s", "MB/s", "GB/s" };
		double num = Math.Max(bps, 0.0);
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		value = ((num2 == 0) ? $"{num:0}" : $"{num:0.0}");
		unit = array[num2];
	}

	public static string Rate(double bps)
	{
		RateParts(bps, out string value, out string unit);
		return value + " " + unit;
	}

	public static string FilesPerSec(double fps)
	{
		return $"{fps:0} f/s";
	}
}
