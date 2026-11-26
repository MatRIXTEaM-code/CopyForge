using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudCheck : HudControl
{
	private bool _checked;

	private bool _hover;

	public bool Checked
	{
		get
		{
			return _checked;
		}
		set
		{
			if (_checked != value)
			{
				_checked = value;
				Invalidate();
				this.CheckedChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? CheckedChanged;

	public HudCheck()
	{
		Cursor = Cursors.Hand;
		base.Size = new Size(200, 22);
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
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (base.Enabled && e.Button == MouseButtons.Left)
		{
			Checked = !Checked;
		}
		base.OnMouseDown(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (base.Enabled && e.KeyCode == Keys.Space)
		{
			Checked = !Checked;
			e.Handled = true;
		}
		base.OnKeyDown(e);
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

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		Color color = (base.Enabled ? Theme.Accent : Theme.Dim);
		RectangleF r = new RectangleF(1f, (float)(base.Height - 15) / 2f, 15f, 15f);
		using (GraphicsPath graphicsPath = Theme.Chamfer(r, 4f))
		{
			using (SolidBrush brush = new SolidBrush(Theme.A(color, _checked ? 50 : 10)))
			{
				graphics.FillPath(brush, graphicsPath);
			}
			if (_checked && base.Enabled)
			{
				Theme.Glow(graphics, graphicsPath, color, 1f);
			}
			using Pen pen = new Pen(Theme.A(color, (_hover || Focused) ? 255 : 170), 1.3f);
			graphics.DrawPath(pen, graphicsPath);
		}
		if (_checked)
		{
			using SolidBrush brush2 = new SolidBrush(color);
			graphics.FillRectangle(brush2, r.X + 4f, r.Y + 4f, 7f, 7f);
		}
		using SolidBrush brush3 = new SolidBrush((!base.Enabled) ? Theme.Dim : (_hover ? Color.White : Theme.Text));
		graphics.DrawString(Text.ToUpperInvariant(), Theme.FLabel, brush3, new RectangleF(24f, 0f, base.Width - 24, base.Height), Theme.SfLeft);
	}
}
