using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudButton : HudControl
{
	private bool _hover;

	private bool _down;

	private Color _accent = Theme.Accent;

	public Color Accent
	{
		get
		{
			return _accent;
		}
		set
		{
			_accent = value;
			Invalidate();
		}
	}

	public HudButton()
	{
		Cursor = Cursors.Hand;
		base.Size = new Size(100, 30);
		base.TabStop = true;
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hover = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hover = false;
		_down = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			_down = true;
			Focus();
			Invalidate();
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_down = false;
		Invalidate();
		base.OnMouseUp(e);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
		base.OnGotFocus(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
		base.OnLostFocus(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Return))
		{
			OnClick(EventArgs.Empty);
			e.Handled = true;
		}
		base.OnKeyDown(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		using GraphicsPath graphicsPath = Theme.Chamfer(new RectangleF(1f, 1f, base.Width - 3, base.Height - 3), Math.Min(10f, (float)base.Height / 3f));
		Color color = (base.Enabled ? _accent : Theme.Dim);
		int alpha = ((!base.Enabled) ? 8 : (_down ? 95 : (_hover ? 50 : 20)));
		using (SolidBrush brush = new SolidBrush(Theme.A(color, alpha)))
		{
			graphics.FillPath(brush, graphicsPath);
		}
		if (base.Enabled && (_hover || Focused))
		{
			Theme.Glow(graphics, graphicsPath, color, 1.2f);
		}
		using (Pen pen = new Pen(Theme.A(color, base.Enabled ? 220 : 90), 1.4f))
		{
			graphics.DrawPath(pen, graphicsPath);
		}
		using SolidBrush brush2 = new SolidBrush((!base.Enabled) ? Theme.Dim : (_hover ? Color.White : color));
		graphics.DrawString(Text.ToUpperInvariant(), Theme.FBtn, brush2, new RectangleF(0f, 0f, base.Width, base.Height), Theme.SfCenter);
	}
}
