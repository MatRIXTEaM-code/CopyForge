using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudSegmented : HudControl
{
	private string[] _items = Array.Empty<string>();

	private int _sel;

	private int _hot = -1;

	public Color Accent { get; set; } = Theme.Accent;


	public string[] Items
	{
		get
		{
			return _items;
		}
		set
		{
			_items = value;
			Invalidate();
		}
	}

	public int SelectedIndex
	{
		get
		{
			return _sel;
		}
		set
		{
			if (value != _sel)
			{
				_sel = value;
				Invalidate();
				this.SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? SelectedIndexChanged;

	public HudSegmented()
	{
		Cursor = Cursors.Hand;
		base.Size = new Size(300, 30);
	}

	private int Hit(Point p)
	{
		if (_items.Length != 0)
		{
			return Math.Clamp(p.X * _items.Length / Math.Max(1, base.Width), 0, _items.Length - 1);
		}
		return -1;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		int num = Hit(e.Location);
		if (num != _hot)
		{
			_hot = num;
			Invalidate();
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hot = -1;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (base.Enabled && e.Button == MouseButtons.Left)
		{
			int num = Hit(e.Location);
			if (num >= 0)
			{
				SelectedIndex = num;
			}
		}
		base.OnMouseDown(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		if (_items.Length == 0)
		{
			return;
		}
		float num = (float)base.Width / (float)_items.Length;
		for (int i = 0; i < _items.Length; i++)
		{
			RectangleF rectangleF = new RectangleF((float)i * num + 1f, 1f, num - 3f, base.Height - 3);
			using GraphicsPath graphicsPath = Theme.Chamfer(rectangleF, 5f);
			bool flag = i == _sel;
			bool flag2 = i == _hot && base.Enabled;
			Color c = (base.Enabled ? Accent : Theme.Dim);
			if (flag)
			{
				using SolidBrush brush = new SolidBrush(Theme.A(c, base.Enabled ? 60 : 25));
				graphics.FillPath(brush, graphicsPath);
				if (base.Enabled)
				{
					Theme.Glow(graphics, graphicsPath, c, 1.2f);
				}
			}
			else if (flag2)
			{
				using SolidBrush brush2 = new SolidBrush(Theme.A(c, 22));
				graphics.FillPath(brush2, graphicsPath);
			}
			using (Pen pen = new Pen(flag ? Theme.A(c, 230) : Theme.Line, flag ? 1.5f : 1f))
			{
				graphics.DrawPath(pen, graphicsPath);
			}
			using SolidBrush brush3 = new SolidBrush((!flag) ? (flag2 ? Theme.Text : Theme.Dim) : (base.Enabled ? Color.White : Theme.Dim));
			graphics.DrawString(_items[i], Theme.FLabel, brush3, rectangleF, Theme.SfCenter);
		}
	}
}
