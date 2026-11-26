using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class Readout : HudControl
{
	public string Caption { get; set; } = "";


	public string Value { get; set; } = "0";


	public string Unit { get; set; } = "";


	public Color ValueColor { get; set; } = Theme.Accent;


	public void Set(string value, string unit)
	{
		if (!(Value == value) || !(Unit == unit))
		{
			Value = value;
			Unit = unit;
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(BackColor);
		using SolidBrush brush = new SolidBrush(Theme.Dim);
		graphics.DrawString(Caption.ToUpperInvariant(), Theme.FLabel, brush, new RectangleF(0f, 0f, base.Width, 14f), Theme.SfLeft);
		SizeF sizeF = graphics.MeasureString(Value, Theme.FBig, PointF.Empty, StringFormat.GenericTypographic);
		using (SolidBrush brush2 = new SolidBrush(Theme.A(ValueColor, 40)))
		{
			graphics.DrawString(Value, Theme.FBig, brush2, 1f, 15f, StringFormat.GenericTypographic);
		}
		using (SolidBrush brush3 = new SolidBrush(ValueColor))
		{
			graphics.DrawString(Value, Theme.FBig, brush3, 0f, 14f, StringFormat.GenericTypographic);
		}
		using SolidBrush brush4 = new SolidBrush(Theme.Text);
		graphics.DrawString(Unit, Theme.FBtn, brush4, sizeF.Width + 8f, 14f + sizeF.Height - 22f, StringFormat.GenericTypographic);
	}
}
