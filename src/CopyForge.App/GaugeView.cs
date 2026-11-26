using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class GaugeView : HudControl
{
	public double Value { get; set; }

	public bool Running { get; set; }

	public string Phase { get; set; } = "STANDBY";


	public Color ArcColor { get; set; } = Theme.Accent;


	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		float num = (float)base.Width / 2f;
		float num2 = (float)base.Height / 2f;
		float num3 = (float)Math.Min(base.Width, base.Height) / 2f - 6f;
		if (num3 < 70f)
		{
			return;
		}
		float num4 = (float)(Environment.TickCount64 % 8000) / 8000f * 360f;
		Color arcColor = ArcColor;
		using (Pen pen = new Pen(Theme.A(Theme.Line, 220), 1.2f))
		{
			graphics.DrawEllipse(pen, num - num3, num2 - num3, 2f * num3, 2f * num3);
		}
		if (Running)
		{
			using Pen pen2 = new Pen(Theme.A(arcColor, 190), 2.4f);
			for (int i = 0; i < 3; i++)
			{
				graphics.DrawArc(pen2, num - num3, num2 - num3, 2f * num3, 2f * num3, num4 + (float)(i * 120), 46f);
			}
			using Pen pen3 = new Pen(Theme.A(arcColor, 70), 1f)
			{
				DashStyle = DashStyle.Dot
			};
			float num5 = num3 - 5f;
			graphics.DrawArc(pen3, num - num5, num2 - num5, 2f * num5, 2f * num5, (0f - num4) * 1.5f, 300f);
		}
		float num6 = num3 - 12f;
		float num7 = num3 - 20f;
		for (int j = 0; j < 90; j++)
		{
			double num8 = (-90.0 + 360.0 * (double)j / 90.0) * Math.PI / 180.0;
			bool num9 = ((double)j + 0.5) / 90.0 <= Value;
			bool flag = j % 5 == 0;
			float num10 = (flag ? (num7 - 4f) : num7);
			using Pen pen4 = new Pen(num9 ? Theme.A(arcColor, flag ? 255 : 190) : Theme.A(Theme.Line, 200), flag ? 1.6f : 1f);
			graphics.DrawLine(pen4, num + (float)Math.Cos(num8) * num6, num2 + (float)Math.Sin(num8) * num6, num + (float)Math.Cos(num8) * num10, num2 + (float)Math.Sin(num8) * num10);
		}
		float num11 = num3 - 42f;
		using (Pen pen5 = new Pen(Theme.A(Theme.Line, 150), 9f))
		{
			graphics.DrawEllipse(pen5, num - num11, num2 - num11, 2f * num11, 2f * num11);
		}
		float num12 = (float)(360.0 * Math.Clamp(Value, 0.0, 1.0));
		if (num12 > 0.5f)
		{
			float[] array = new float[3] { 24f, 18f, 13f };
			foreach (float width in array)
			{
				using Pen pen6 = new Pen(Theme.A(arcColor, 16), width);
				graphics.DrawArc(pen6, num - num11, num2 - num11, 2f * num11, 2f * num11, -90f, num12);
			}
			using Pen pen7 = new Pen(arcColor, 9f)
			{
				StartCap = LineCap.Flat,
				EndCap = LineCap.Flat
			};
			graphics.DrawArc(pen7, num - num11, num2 - num11, 2f * num11, 2f * num11, -90f, num12);
			double num13 = (double)(-90f + num12) * Math.PI / 180.0;
			using SolidBrush brush = new SolidBrush(Color.White);
			graphics.FillEllipse(brush, num + (float)Math.Cos(num13) * num11 - 3.5f, num2 + (float)Math.Sin(num13) * num11 - 3.5f, 7f, 7f);
		}
		float num14 = num11 - 14f;
		using (Pen pen8 = new Pen(Theme.A(Theme.Line, 120), 1f)
		{
			DashStyle = DashStyle.Dash
		})
		{
			graphics.DrawEllipse(pen8, num - num14, num2 - num14, 2f * num14, 2f * num14);
		}
		string text = Math.Round(Value * 100.0).ToString("0");
		Font font = ((text.Length >= 3) ? Theme.FGauge3 : Theme.FGauge);
		StringFormat genericTypographic = StringFormat.GenericTypographic;
		SizeF sizeF = graphics.MeasureString(text, font, PointF.Empty, genericTypographic);
		SizeF sizeF2 = graphics.MeasureString("%", Theme.FPct, PointF.Empty, genericTypographic);
		float num15 = sizeF.Width + 3f + sizeF2.Width;
		float num16 = num - num15 / 2f;
		float num17 = num2 - sizeF.Height / 2f - 8f;
		using (SolidBrush brush2 = new SolidBrush(Color.White))
		{
			graphics.DrawString(text, font, brush2, num16, num17, genericTypographic);
		}
		using (SolidBrush brush3 = new SolidBrush(arcColor))
		{
			graphics.DrawString("%", Theme.FPct, brush3, num16 + sizeF.Width + 3f, num17 + sizeF.Height - sizeF2.Height - 4f, genericTypographic);
		}
		using SolidBrush brush4 = new SolidBrush(Theme.Dim);
		graphics.DrawString(Phase.ToUpperInvariant(), Theme.FLabel, brush4, new RectangleF(num - num14, num2 + 26f, num14 * 2f, 16f), Theme.SfCenter);
	}
}
