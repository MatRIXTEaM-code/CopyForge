using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudLabel : HudControl
{
	public Color Color { get; set; } = Theme.Dim;


	public HudLabel()
	{
		base.Size = new Size(200, 14);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Theme.Quality(e.Graphics);
		e.Graphics.Clear(BackColor);
		using SolidBrush brush = new SolidBrush(Color);
		e.Graphics.DrawString(Text.ToUpperInvariant(), Theme.FLabel, brush, new RectangleF(0f, 0f, base.Width, base.Height), Theme.SfLeft);
	}
}
