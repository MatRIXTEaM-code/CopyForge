using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CopyForge.Engine.Net;

namespace CopyForge.App;

/// <summary>Incoming LocalSend offer: tick the files you want, untick the rest.</summary>
internal sealed class LsPickForm : Form
{
	private readonly HudPanel _panel = new HudPanel { Title = "INCOMING FILES" };

	private readonly Label _head = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Text,
		Font = Theme.FSmall
	};

	private readonly CheckedListBox _list = new CheckedListBox
	{
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Bg,
		ForeColor = Theme.Text,
		Font = Theme.Mono(9.5f),
		CheckOnClick = true,
		HorizontalScrollbar = true
	};

	private readonly HudButton _bYes = new HudButton { Text = "ACCEPT", Accent = Theme.Ok };

	private readonly HudButton _bNo = new HudButton { Text = "DECLINE", Accent = Theme.Warn };

	private readonly HudButton _bAll = new HudButton { Text = "All / none" };

	private readonly IReadOnlyList<LsOffer> _offers;

	private LsPickForm(string who, string ip, IReadOnlyList<LsOffer> offers, string folder)
	{
		_offers = offers;
		Text = "CopyForge - incoming files";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.ClientSize = new Size(720, 520);
		MinimumSize = SizeFromClientSize(new Size(520, 320));
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_head, _list, _bYes, _bNo, _bAll);
		_head.Text = $"{who} ({ip}) wants to send {offers.Count:N0} file(s). Untick any you do not want. Saved into: {folder}";
		foreach (LsOffer o in offers)
		{
			_list.Items.Add(o.Name + "   (" + Theme.Bytes(o.Size) + ")" + (o.Exists ? "   [already exists]" : ""), isChecked: true);
		}
		_panel.Resize += delegate { DoLayout(); };
		DoLayout();
		_bYes.Click += delegate
		{
			DialogResult = DialogResult.Yes;
			Close();
		};
		_bNo.Click += delegate
		{
			DialogResult = DialogResult.No;
			Close();
		};
		_bAll.Click += delegate
		{
			bool any = _list.CheckedItems.Count < _list.Items.Count;
			for (int i = 0; i < _list.Items.Count; i++)
			{
				_list.SetItemChecked(i, any);
			}
		};
		base.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				DialogResult = DialogResult.No;
				Close();
			}
		};
	}

	private void DoLayout()
	{
		int w = _panel.ClientSize.Width;
		int h = _panel.ClientSize.Height;
		_head.SetBounds(12, 32, w - 24, 54);
		_list.SetBounds(12, 92, w - 24, h - 92 - 56);
		_bAll.SetBounds(12, h - 48, 120, 36);
		_bYes.SetBounds(w - 12 - 120 - 130, h - 48, 120, 36);
		_bNo.SetBounds(w - 12 - 120, h - 48, 120, 36);
	}

	/// <summary>Returns the ticked ids, or null when declined / nothing ticked.</summary>
	public static List<string>? Pick(IWin32Window owner, string who, string ip, IReadOnlyList<LsOffer> offers, string folder)
	{
		using LsPickForm f = new LsPickForm(who, ip, offers, folder);
		if (f.ShowDialog(owner) != DialogResult.Yes)
		{
			return null;
		}
		List<string> ids = new List<string>();
		for (int i = 0; i < f._offers.Count; i++)
		{
			if (f._list.GetItemChecked(i))
			{
				ids.Add(f._offers[i].Id);
			}
		}
		return ids.Count > 0 ? ids : null;
	}
}
