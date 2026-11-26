using System;
using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class SegBar : HudControl
{
	public double Value { get; set; }

	public bool Running { get; set; }

	public Color BarColor { get; set; } = Theme.Accent;


	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		float num = ((float)base.Width - 189f) / 64f;
		int num2 = (int)Math.Floor(Math.Clamp(Value, 0.0, 1.0) * 64.0);
		int num3 = (int)(Running ? (Environment.TickCount64 / 45 % 64) : (-1));
		bool flag = Environment.TickCount64 / 250 % 2 == 0;
		for (int i = 0; i < 64; i++)
		{
			RectangleF rect = new RectangleF((float)i * (num + 3f), 0f, num, base.Height);
			Color color = ((i < num2) ? Theme.A(BarColor, (Running && Math.Abs(i - num3) < 2 && i <= num2) ? 255 : 200) : ((i != num2 || !(Value > 0.0) || !(Value < 1.0)) ? Theme.A(Theme.Line, 150) : (flag ? BarColor : Theme.A(BarColor, 90))));
			using SolidBrush brush = new SolidBrush(color);
			graphics.FillRectangle(brush, rect);
		}
	}
}
