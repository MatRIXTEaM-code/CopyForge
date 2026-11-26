using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class StatTile : HudControl
{
	private string _value = "—";

	private Color _vc = Theme.Text;

	public string Caption { get; set; } = "";


	public string Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (_value != value)
			{
				_value = value;
				Invalidate();
			}
		}
	}

	public Color ValueColor
	{
		get
		{
			return _vc;
		}
		set
		{
			if (_vc != value)
			{
				_vc = value;
				Invalidate();
			}
		}
	}

	public StatTile()
	{
		base.Size = new Size(150, 52);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(Theme.Field);
		using (Pen pen = new Pen(Theme.Line))
		{
			graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
		}
		using (SolidBrush brush = new SolidBrush(_vc))
		{
			graphics.FillRectangle(brush, 0, 0, 3, base.Height);
		}
		using SolidBrush brush2 = new SolidBrush(Theme.Dim);
		graphics.DrawString(Caption.ToUpperInvariant(), Theme.FLabel, brush2, new RectangleF(12f, 4f, base.Width - 16, 16f), Theme.SfLeft);
		using SolidBrush brush3 = new SolidBrush(_vc);
		graphics.DrawString(_value, Theme.FValue, brush3, new RectangleF(11f, 20f, base.Width - 14, base.Height - 22), Theme.SfLeft);
	}
}
