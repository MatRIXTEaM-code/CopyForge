using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudStepper : HudControl
{
	private long _value;

	private readonly Timer _repeat = new Timer
	{
		Interval = 380
	};

	private int _dir;

	private int _reps;

	private int _hot;

	private const int BtnW = 24;

	private const int ValW = 56;

	public string Label { get; set; } = "";


	public string Suffix { get; set; } = "";


	public string ZeroText { get; set; } = "";


	public long Min { get; set; }

	public long Max { get; set; } = 100L;


	public long Step { get; set; } = 1L;


	public long Value
	{
		get
		{
			return _value;
		}
		set
		{
			value = Math.Clamp(value, Min, Max);
			if (value != _value)
			{
				_value = value;
				Invalidate();
				this.ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	private Rectangle Minus => new Rectangle(base.Width - 48 - 56, 1, 24, base.Height - 2);

	private Rectangle Plus => new Rectangle(base.Width - 24, 1, 24, base.Height - 2);

	private Rectangle ValueRect => new Rectangle(base.Width - 24 - 56, 1, 56, base.Height - 2);

	public event EventHandler? ValueChanged;

	public HudStepper()
	{
		base.Size = new Size(158, 26);
		_repeat.Tick += delegate
		{
			_repeat.Interval = 60;
			_reps++;
			Value += _dir * Step * ((_reps <= 12) ? 1 : 10);
		};
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_repeat.Dispose();
		}
		base.Dispose(disposing);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		int num = (Minus.Contains(e.Location) ? (-1) : (Plus.Contains(e.Location) ? 1 : 0));
		if (num != _hot)
		{
			_hot = num;
			Invalidate();
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hot = 0;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (base.Enabled && e.Button == MouseButtons.Left)
		{
			_dir = (Minus.Contains(e.Location) ? (-1) : (Plus.Contains(e.Location) ? 1 : 0));
			if (_dir != 0)
			{
				Value += _dir * Step;
				_reps = 0;
				_repeat.Interval = 380;
				_repeat.Start();
			}
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_repeat.Stop();
		base.OnMouseUp(e);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		if (base.Enabled)
		{
			Value += Math.Sign(e.Delta) * Step;
		}
		base.OnMouseWheel(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		Color ac = (base.Enabled ? Theme.Accent : Theme.Dim);
		if (Label.Length > 0)
		{
			using SolidBrush brush = new SolidBrush(Theme.Dim);
			graphics.DrawString(Label.ToUpperInvariant(), Theme.FLabel, brush, new RectangleF(0f, 0f, base.Width - 48 - 56, base.Height), Theme.SfLeft);
		}
		DrawBtn(graphics, Minus, "–", _hot == -1, ac);
		DrawBtn(graphics, Plus, "+", _hot == 1, ac);
		Rectangle valueRect = ValueRect;
		using (SolidBrush brush2 = new SolidBrush(Theme.Field))
		{
			graphics.FillRectangle(brush2, valueRect);
		}
		using (Pen pen = new Pen(Theme.Line))
		{
			graphics.DrawLine(pen, valueRect.Left, valueRect.Top, valueRect.Right, valueRect.Top);
			graphics.DrawLine(pen, valueRect.Left, valueRect.Bottom - 1, valueRect.Right, valueRect.Bottom - 1);
		}
		using SolidBrush brush3 = new SolidBrush(base.Enabled ? Theme.Accent : Theme.Dim);
		graphics.DrawString((_value == 0L && ZeroText.Length > 0) ? ZeroText : (_value.ToString("N0") + Suffix), Theme.FBody, brush3, valueRect, Theme.SfCenter);
	}

	private static void DrawBtn(Graphics g, Rectangle r, string glyph, bool hot, Color ac)
	{
		using GraphicsPath path = Theme.Chamfer(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), 4f);
		using (SolidBrush brush = new SolidBrush(Theme.A(ac, hot ? 60 : 16)))
		{
			g.FillPath(brush, path);
		}
		using (Pen pen = new Pen(Theme.A(ac, hot ? 255 : 150), 1.2f))
		{
			g.DrawPath(pen, path);
		}
		using SolidBrush brush2 = new SolidBrush(hot ? Color.White : ac);
		g.DrawString(glyph, Theme.FBtn, brush2, r, Theme.SfCenter);
	}
}
