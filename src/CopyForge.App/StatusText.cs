using System;
using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class StatusText : HudControl
{
	public bool ShowLed { get; set; } = true;


	public bool Pulse { get; set; }

	public Color Color { get; set; } = Theme.Dim;


	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		float num = 0f;
		if (ShowLed)
		{
			using SolidBrush brush = new SolidBrush((!Pulse || Environment.TickCount64 / 400 % 2 == 0) ? Color : Theme.A(Color, 60));
			graphics.FillEllipse(brush, 0f, (float)base.Height / 2f - 4f, 8f, 8f);
			num = 16f;
		}
		using SolidBrush brush2 = new SolidBrush(Color);
		graphics.DrawString(Text, Theme.FBody, brush2, new RectangleF(num, 0f, (float)base.Width - num, base.Height), Theme.SfLeft);
	}
}
