using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class LogView : HudControl
{
	private readonly record struct Entry(DateTime Time, string Text, Color Color);

	private readonly List<Entry> _items = new List<Entry>();

	private int _off;

	public LogView()
	{
		Cursor = Cursors.IBeam;
	}

	public void Add(string text, Color? color = null)
	{
		Color color2 = color ?? (text.StartsWith("ERROR", StringComparison.Ordinal) ? Theme.Bad : Theme.Text);
		_items.Add(new Entry(DateTime.Now, text, color2));
		if (_items.Count > 5000)
		{
			_items.RemoveAt(0);
		}
		if (_off > 0)
		{
			_off++;
		}
		Invalidate();
	}

	public void Clear()
	{
		_items.Clear();
		_off = 0;
		Invalidate();
	}

	public List<string> Snapshot(int max)
	{
		int count = Math.Max(0, _items.Count - max);
		return (from i in _items.Skip(count)
			select $"{i.Time:HH:mm:ss}  {i.Text}").ToList();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		int num = base.Height / 15;
		_off = Math.Clamp(_off + Math.Sign(e.Delta) * 3, 0, Math.Max(0, _items.Count - num));
		Invalidate();
		base.OnMouseWheel(e);
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
		int num = Math.Max(1, (base.Height - 8) / 15);
		int num2 = _items.Count - 1 - _off;
		using SolidBrush brush = new SolidBrush(Theme.Dim);
		for (int i = 0; i < num && num2 - i >= 0; i++)
		{
			Entry entry = _items[num2 - i];
			float y = base.Height - 6 - (i + 1) * 15;
			graphics.DrawString(entry.Time.ToString("HH:mm:ss"), Theme.FSmall, brush, 8f, y);
			using SolidBrush brush2 = new SolidBrush(entry.Color);
			graphics.DrawString(entry.Text, Theme.FSmall, brush2, new RectangleF(70f, y, base.Width - 78, 15f), Theme.SfLeft);
		}
		if (_off > 0)
		{
			using (SolidBrush brush3 = new SolidBrush(Theme.Warn))
			{
				graphics.DrawString($"▲ {_off} newer", Theme.FSmall, brush3, new RectangleF(0f, 4f, base.Width - 8, 14f), Theme.SfRight);
				return;
			}
		}
	}
}
