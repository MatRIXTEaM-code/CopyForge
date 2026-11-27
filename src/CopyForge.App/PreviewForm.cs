using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CopyForge.Engine;

namespace CopyForge.App;

/// <summary>Confirmation window that lists every file a sync / move / delete is about to touch.</summary>
internal sealed class PreviewForm : Form
{
	private readonly HudPanel _panel;

	private readonly RichTextBox _rtb = new RichTextBox
	{
		ReadOnly = true,
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Bg,
		ForeColor = Theme.Text,
		Font = Theme.Mono(9.5f),
		WordWrap = false,
		ScrollBars = RichTextBoxScrollBars.Both,
		DetectUrls = false
	};

	private readonly HudButton _bYes;

	private readonly HudButton _bNo = new HudButton
	{
		Text = "Cancel",
		Accent = Theme.Warn
	};

	private readonly Label _head = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Text,
		Font = Theme.FSmall
	};

	private PreviewForm(string title, string header, PlanPreview p, string yes, bool danger, bool deleteMode)
	{
		_panel = new HudPanel
		{
			Title = title.ToUpperInvariant()
		};
		_bYes = new HudButton
		{
			Text = yes,
			Accent = (danger ? Theme.Bad : Theme.Ok)
		};
		Text = "CopyForge - " + title;
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(900, 640);
		MinimumSize = SizeFromClientSize(new Size(640, 400));
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		NativeUi.DarkScroll(_rtb);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_head, _rtb, _bYes, _bNo);
		_head.Text = header;
		StringBuilder sb = new StringBuilder();
		if (p.DeleteList.Count > 0)
		{
			sb.Append(deleteMode ? "WILL BE DELETED\n" : "WILL BE DELETED FROM THE DESTINATION\n");
			foreach (string d in p.DeleteList)
			{
				sb.Append("  - ").Append(d).Append('\n');
			}
			sb.Append('\n');
		}
		int delEnd = sb.Length;
		if (p.CopyList.Count > 0)
		{
			sb.Append("WILL BE COPIED / MOVED\n");
			foreach (string c in p.CopyList)
			{
				sb.Append("  + ").Append(c).Append('\n');
			}
		}
		if (p.Truncated || p.CopyList.Count < p.CopyCount || p.DeleteList.Count < p.DeleteCount)
		{
			sb.Append("\n(list shortened - the counts above are complete)\n");
		}
		_rtb.Text = sb.ToString();
		if (delEnd > 0)
		{
			_rtb.Select(0, delEnd);
			_rtb.SelectionColor = Theme.Bad;
		}
		_rtb.Select(0, 0);
		_panel.Resize += delegate
		{
			DoLayout();
		};
		DoLayout();
		_bYes.Click += delegate
		{
			base.DialogResult = DialogResult.Yes;
			Close();
		};
		_bNo.Click += delegate
		{
			base.DialogResult = DialogResult.No;
			Close();
		};
		base.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				base.DialogResult = DialogResult.No;
				Close();
			}
		};
	}

	private void DoLayout()
	{
		int w = _panel.ClientSize.Width;
		int h = _panel.ClientSize.Height;
		_head.SetBounds(12, 32, w - 24, 54);
		_rtb.SetBounds(12, 92, w - 24, h - 92 - 56);
		_bYes.SetBounds(w - 12 - 120 - 130, h - 48, 120, 36);
		_bNo.SetBounds(w - 12 - 120, h - 48, 120, 36);
	}

	public static bool Ask(IWin32Window owner, string title, string header, PlanPreview p, string yes, bool danger, bool deleteMode)
	{
		using PreviewForm f = new PreviewForm(title, header, p, yes, danger, deleteMode);
		return f.ShowDialog(owner) == DialogResult.Yes;
	}
}
