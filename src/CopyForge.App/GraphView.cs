using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class GraphView : HudControl
{
	private const int MaxSamples = 120;

	private readonly List<double> _s = new List<double>();

	private long _lastSample = Environment.TickCount64;

	public bool Running { get; set; }

	public Func<double, string> Fmt { get; set; } = Theme.Rate;


	public double MinPeak { get; set; } = 1048576.0;


	public void Add(double v)
	{
		_s.Add(Math.Max(v, 0.0));
		if (_s.Count > 120)
		{
			_s.RemoveAt(0);
		}
		_lastSample = Environment.TickCount64;
		Invalidate();
	}

	public void Reset()
	{
		_s.Clear();
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		RectangleF rectangleF = new RectangleF(64f, 8f, Math.Max(10, base.Width - 64 - 14), Math.Max(10, base.Height - 8 - 20));
		double num = ((_s.Count > 0) ? _s.Max() : 0.0);
		double num2 = Math.Max(num * 1.15, MinPeak);
		using (Pen pen = new Pen(Theme.A(Theme.Line, 110), 1f)
		{
			DashStyle = DashStyle.Dash
		})
		{
			using SolidBrush brush = new SolidBrush(Theme.Dim);
			for (int i = 0; i <= 4; i++)
			{
				float num3 = rectangleF.Top + (float)i * rectangleF.Height / 4f;
				graphics.DrawLine(pen, rectangleF.Left, num3, rectangleF.Right, num3);
				graphics.DrawString(Fmt(num2 * (double)(4 - i) / 4.0), Theme.FSmall, brush, new RectangleF(0f, num3 - 8f, 60f, 16f), Theme.SfRight);
			}
			for (int j = 1; j < 12; j++)
			{
				float num4 = rectangleF.Left + (float)j * rectangleF.Width / 12f;
				graphics.DrawLine(pen, num4, rectangleF.Top, num4, rectangleF.Bottom);
			}
		}
		if (_s.Count >= 2)
		{
			float num5 = rectangleF.Width / 119f;
			double num6 = (Running ? Math.Min(1.0, (double)(Environment.TickCount64 - _lastSample) / 250.0) : 0.0);
			int count = _s.Count;
			List<PointF> list = new List<PointF>(count + 1);
			for (int k = 0; k < count; k++)
			{
				float x = rectangleF.Right - (float)(count - 1 - k) * num5 - (float)num6 * num5;
				float y = rectangleF.Bottom - (float)(_s[k] / num2) * rectangleF.Height;
				list.Add(new PointF(x, y));
			}
			float y2 = list[list.Count - 1].Y;
			list.Add(new PointF(rectangleF.Right, y2));
			GraphicsState gstate = graphics.Save();
			graphics.SetClip(rectangleF);
			List<PointF> list2 = new List<PointF>(list)
			{
				new PointF(rectangleF.Right, rectangleF.Bottom),
				new PointF(list[0].X, rectangleF.Bottom)
			};
			using (LinearGradientBrush brush2 = new LinearGradientBrush(rectangleF, Theme.A(Theme.Accent, 95), Theme.A(Theme.Accent, 0), 90f))
			{
				graphics.FillPolygon(brush2, list2.ToArray());
			}
			PointF[] points = list.ToArray();
			using (Pen pen2 = new Pen(Theme.A(Theme.Accent, 38), 7f)
			{
				LineJoin = LineJoin.Round
			})
			{
				graphics.DrawLines(pen2, points);
			}
			using (Pen pen3 = new Pen(Theme.Accent, 1.8f)
			{
				LineJoin = LineJoin.Round
			})
			{
				graphics.DrawLines(pen3, points);
			}
			using (SolidBrush brush3 = new SolidBrush(Color.White))
			{
				graphics.FillEllipse(brush3, rectangleF.Right - 3.5f, y2 - 3.5f, 7f, 7f);
			}
			using (Pen pen4 = new Pen(Theme.A(Theme.Accent, 120), 1f))
			{
				graphics.DrawEllipse(pen4, rectangleF.Right - 8f, y2 - 8f, 16f, 16f);
			}
			graphics.Restore(gstate);
			using SolidBrush brush4 = new SolidBrush(Theme.Accent);
			Func<double, string> fmt = Fmt;
			List<double> s = _s;
			graphics.DrawString("NOW  " + fmt(s[s.Count - 1]), Theme.FSmall, brush4, rectangleF.Left + 8f, rectangleF.Top + 4f);
			using SolidBrush brush5 = new SolidBrush(Theme.Dim);
			graphics.DrawString("PEAK " + Fmt(num), Theme.FSmall, brush5, new RectangleF(rectangleF.Left, rectangleF.Top + 4f, rectangleF.Width - 8f, 16f), Theme.SfRight);
		}
		else
		{
			using SolidBrush brush6 = new SolidBrush(Theme.A(Theme.Dim, 180));
			graphics.DrawString("AWAITING TRANSFER DATA", Theme.FLabel, brush6, rectangleF, Theme.SfCenter);
		}
		if (Running)
		{
			float num7 = rectangleF.Left + (float)(Environment.TickCount64 % 2800) / 2800f * rectangleF.Width;
			float num8 = Math.Min(70f, num7 - rectangleF.Left);
			if (num8 > 2f)
			{
				using LinearGradientBrush brush7 = new LinearGradientBrush(new RectangleF(num7 - num8, rectangleF.Top, num8, rectangleF.Height), Theme.A(Theme.Accent, 0), Theme.A(Theme.Accent, 34), 0f);
				graphics.FillRectangle(brush7, num7 - num8, rectangleF.Top, num8, rectangleF.Height);
			}
			using Pen pen5 = new Pen(Theme.A(Theme.Accent, 110), 1f);
			graphics.DrawLine(pen5, num7, rectangleF.Top, num7, rectangleF.Bottom);
		}
		using Pen pen6 = new Pen(Theme.A(Theme.LineHi, 160), 1f);
		graphics.DrawLine(pen6, rectangleF.Left, rectangleF.Bottom, rectangleF.Right, rectangleF.Bottom);
		using SolidBrush brush8 = new SolidBrush(Theme.Dim);
		graphics.DrawString("-30 s", Theme.FSmall, brush8, new RectangleF(rectangleF.Left, rectangleF.Bottom + 2f, 60f, 16f), Theme.SfLeft);
		graphics.DrawString("NOW", Theme.FSmall, brush8, new RectangleF(rectangleF.Right - 60f, rectangleF.Bottom + 2f, 60f, 16f), Theme.SfRight);
	}
}
