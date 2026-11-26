using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HeaderBar : HudControl
{
	public string Status { get; set; } = "ENGINE IDLE";


	public Color StatusColor { get; set; } = Theme.Dim;


	public bool Blink { get; set; }

	public float Reveal { get; set; } = 1f;


	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(Theme.Bg);
		float num = Math.Clamp(Reveal, 0f, 1f);
		int alpha = (int)(255f * num);
		RectangleF rectangleF = new RectangleF(2f, 5f, 30f, 30f);
		using (GraphicsPath graphicsPath = Theme.Chamfer(rectangleF, 8f))
		{
			Theme.Glow(graphics, graphicsPath, Theme.Accent, 1.2f);
			using SolidBrush brush = new SolidBrush(Theme.A(Theme.Accent, (int)(40f * num)));
			graphics.FillPath(brush, graphicsPath);
			using Pen pen = new Pen(Theme.A(Theme.Accent, alpha), 1.8f);
			graphics.DrawPath(pen, graphicsPath);
		}
		using (SolidBrush brush2 = new SolidBrush(Theme.A(Color.White, alpha)))
		{
			graphics.DrawString("CF", Theme.FBtn, brush2, rectangleF, Theme.SfCenter);
		}
		int value = (int)Math.Ceiling((float)"COPYFORGE".Length * num);
		Theme.DrawSpaced(graphics, "COPYFORGE".Substring(0, Math.Clamp(value, 0, 9)), Theme.FLogo, Theme.A(Color.White, alpha), 44f, 2f, 5f);
		using (SolidBrush brush3 = new SolidBrush(Theme.A(Theme.Dim, alpha)))
		{
			graphics.DrawString("HIGH-SPEED TRANSFER ENGINE   //   V" + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3), Theme.FSmall, brush3, 46f, 24f);
		}
		DateTime now = DateTime.Now;
		using (SolidBrush brush4 = new SolidBrush(Theme.A(Theme.Text, alpha)))
		{
			graphics.DrawString(now.ToString("HH:mm:ss"), Theme.FValue, brush4, new RectangleF(base.Width - 160, 0f, 160f, 22f), Theme.SfRight);
		}
		using (SolidBrush brush5 = new SolidBrush(Theme.A(Theme.Dim, alpha)))
		{
			graphics.DrawString(now.ToString("ddd dd MMM yyyy").ToUpperInvariant(), Theme.FSmall, brush5, new RectangleF(base.Width - 160, 22f, 160f, 14f), Theme.SfRight);
		}
		bool num2 = !Blink || Environment.TickCount64 / 400 % 2 == 0;
		float num3 = base.Width - 360;
		using (SolidBrush brush6 = new SolidBrush(num2 ? StatusColor : Theme.A(StatusColor, 50)))
		{
			graphics.FillEllipse(brush6, num3 + 188f, 15f, 9f, 9f);
		}
		using (SolidBrush brush7 = new SolidBrush(StatusColor))
		{
			graphics.DrawString(Status, Theme.FLabel, brush7, new RectangleF(num3, 6f, 184f, 26f), Theme.SfRight);
		}
		float num4 = base.Height - 2;
		using (Pen pen2 = new Pen(Theme.Line, 1f))
		{
			graphics.DrawLine(pen2, 0f, num4, (float)base.Width * num, num4);
		}
		using (Pen pen3 = new Pen(Theme.Accent, 2f))
		{
			graphics.DrawLine(pen3, 0f, num4, 60f * num, num4);
		}
		float num5 = (float)(Environment.TickCount64 % 5000) / 5000f * (float)base.Width;
		using LinearGradientBrush brush8 = new LinearGradientBrush(new RectangleF(num5 - 90f, num4 - 1f, 90f, 3f), Theme.A(Theme.Accent, 0), Theme.A(Theme.Accent, 200), 0f);
		graphics.FillRectangle(brush8, num5 - 90f, num4 - 1f, 90f, 3f);
	}
}
