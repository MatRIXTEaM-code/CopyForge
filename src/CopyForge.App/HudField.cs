using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudField : HudControl
{
	private readonly TextBox _tb = new TextBox
	{
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Field,
		ForeColor = Theme.Accent,
		Font = Theme.FBody,
		AcceptsReturn = true
	};

	private readonly Label _hint = new Label
	{
		AutoSize = false,
		BackColor = Theme.Field,
		ForeColor = Theme.A(Theme.Dim, 220),
		Font = Theme.FSmall,
		Cursor = Cursors.IBeam
	};

	private bool _locked;

	public string Hint
	{
		get
		{
			return _hint.Text;
		}
		set
		{
			_hint.Text = value;
			UpdateHint();
		}
	}

	public TextBox Inner => _tb;

	public string[] Lines => _tb.Lines;

	public bool Multiline
	{
		get
		{
			return _tb.Multiline;
		}
		set
		{
			_tb.Multiline = value;
			_tb.ScrollBars = (value ? ScrollBars.Vertical : ScrollBars.None);
			_tb.WordWrap = !value;
			DoLayoutInner();
		}
	}

	public bool Locked
	{
		get
		{
			return _locked;
		}
		set
		{
			_locked = value;
			_tb.ReadOnly = value;
			_tb.ForeColor = (value ? Theme.Dim : Theme.Accent);
			_tb.TabStop = !value;
			Invalidate();
		}
	}

	public override string Text
	{
		get
		{
			return _tb.Text;
		}
		[param: AllowNull]
		set
		{
			_tb.Text = value ?? "";
		}
	}

	public HudField()
	{
		base.Size = new Size(200, 28);
		NativeUi.DarkScroll(_tb);
		base.Controls.Add(_tb);
		base.Controls.Add(_hint);
		_hint.BringToFront();
		_hint.Click += delegate
		{
			_tb.Focus();
		};
		_tb.Enter += delegate
		{
			Invalidate();
		};
		_tb.Leave += delegate
		{
			Invalidate();
		};
		_tb.TextChanged += delegate
		{
			UpdateHint();
			Invalidate();
		};
		DoLayoutInner();
	}

	public void AppendText(string s)
	{
		_tb.AppendText(s);
	}

	public void Clear()
	{
		_tb.Clear();
	}

	private void UpdateHint()
	{
		_hint.Visible = _tb.Text.Length == 0 && _hint.Text.Length > 0;
	}

	private void DoLayoutInner()
	{
		if (_tb.Multiline)
		{
			_tb.SetBounds(10, 5, Math.Max(10, base.Width - 16), Math.Max(10, base.Height - 10));
		}
		else
		{
			_tb.SetBounds(10, Math.Max(0, (base.Height - _tb.PreferredHeight) / 2), Math.Max(10, base.Width - 16), _tb.PreferredHeight);
		}
		int num = 16;
		_hint.SetBounds(12, _tb.Multiline ? 6 : Math.Max(0, (base.Height - num) / 2), Math.Max(10, base.Width - 20), num);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		DoLayoutInner();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		Theme.Quality(graphics);
		graphics.Clear(Theme.Field);
		bool focused = _tb.Focused;
		using (Pen pen = new Pen(focused ? Theme.Accent : Theme.Line, 1f))
		{
			graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
		}
		using (SolidBrush brush = new SolidBrush(focused ? Theme.Accent : Theme.LineHi))
		{
			graphics.FillRectangle(brush, 0, 0, 3, base.Height);
		}
		if (focused)
		{
			using (Pen pen2 = new Pen(Theme.A(Theme.Accent, 35), 4f))
			{
				graphics.DrawRectangle(pen2, 1, 1, base.Width - 3, base.Height - 3);
			}
		}
	}
}
