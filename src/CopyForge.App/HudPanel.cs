using System;
using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudPanel : HudControl
{
	private float _rev = 1f;

	private bool _kidsShown = true;

	private string _tag = "";

	public string Title { get; set; } = "";


	public string TagText
	{
		get
		{
			return _tag;
		}
		set
		{
			_tag = value;
			Invalidate();
		}
	}

	public Color AccentColor { get; set; } = Theme.Accent;


	public float Reveal
	{
		get
		{
			return _rev;
		}
		set
		{
			value = Math.Clamp(value, 0f, 1f);
			if (Math.Abs(value - _rev) < 0.001f)
			{
				return;
			}
			_rev = value;
			bool flag = _rev >= 0.85f;
			if (flag != _kidsShown)
			{
				_kidsShown = flag;
				foreach (Control control in base.Controls)
				{
					control.Visible = flag;
				}
			}
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(Theme.Panel);
		float num = base.Width - 1;
		float num2 = base.Height - 1;
		float rev = _rev;
		using (Pen pen = new Pen(Theme.Line, 1f))
		{
			graphics.DrawLine(pen, 0f, 0f, num * rev, 0f);
			graphics.DrawLine(pen, num, num2, num - num * rev, num2);
			graphics.DrawLine(pen, 0f, num2, 0f, num2 - num2 * rev);
			graphics.DrawLine(pen, num, 0f, num, num2 * rev);
		}
		if (rev > 0.3f)
		{
			int alpha = (int)(255f * Math.Clamp((rev - 0.3f) / 0.7f, 0f, 1f));
			if (Title.Length > 0 || _tag.Length > 0)
			{
				using (Pen pen2 = new Pen(Theme.A(Theme.Line, alpha), 1f))
				{
					graphics.DrawLine(pen2, 10f, 24f, num - 10f, 24f);
				}
				using (Pen pen3 = new Pen(Theme.A(AccentColor, alpha), 2f))
				{
					graphics.DrawLine(pen3, 10, 24, 52, 24);
				}
				using SolidBrush brush = new SolidBrush(Theme.A(AccentColor, alpha));
				graphics.DrawString(Title, Theme.FLabel, brush, new RectangleF(14f, 3f, num * 0.6f, 20f), Theme.SfLeft);
				using SolidBrush brush2 = new SolidBrush(Theme.A(Theme.Dim, alpha));
				graphics.DrawString(_tag, Theme.FLabel, brush2, new RectangleF(num * 0.5f, 3f, num * 0.5f - 14f, 20f), Theme.SfRight);
			}
		}
		float num3 = 12f * rev;
		if (num3 > 1f)
		{
			using (Pen pen4 = new Pen(AccentColor, 1.8f))
			{
				graphics.DrawLine(pen4, 0f, 0f, num3, 0f);
				graphics.DrawLine(pen4, 0f, 0f, 0f, num3);
				graphics.DrawLine(pen4, num, 0f, num - num3, 0f);
				graphics.DrawLine(pen4, num, 0f, num, num3);
				graphics.DrawLine(pen4, 0f, num2, num3, num2);
				graphics.DrawLine(pen4, 0f, num2, 0f, num2 - num3);
				graphics.DrawLine(pen4, num, num2, num - num3, num2);
				graphics.DrawLine(pen4, num, num2, num, num2 - num3);
			}
		}
	}
}
