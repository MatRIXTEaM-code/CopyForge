using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopyForge.Engine.Net;

namespace CopyForge.App;

/// <summary>LocalSend: receive settings, and sending the main window's sources to a device found on the network.</summary>
internal sealed class LocalSendForm : Form
{
	private readonly AppSettings _cfg;

	private readonly Func<List<string>> _sources;

	private List<LsPeer> _peers = new List<LsPeer>();

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly HudPanel _panel = new HudPanel
	{
		Title = "LOCALSEND",
		TagText = "PHONES & PCS"
	};

	private readonly HudCheck _chkRecv = new HudCheck
	{
		Text = "Receive files from LocalSend devices (this PC shows up on the network)"
	};

	private readonly HudField _fFolder = new HudField
	{
		Hint = "C:\\Users\\you\\Downloads\\LocalSend"
	};

	private readonly HudButton _bBrowse = new HudButton
	{
		Text = "Browse..."
	};

	private readonly HudField _fAlias = new HudField
	{
		Hint = "name other devices see"
	};

	private readonly HudField _fPin = new HudField
	{
		Hint = "PIN senders must enter (empty = none)"
	};

	private readonly Label _lblSend = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Dim,
		Font = Theme.FSmall,
		Text = "SEND THE FILES AND FOLDERS LISTED IN THE MAIN WINDOW TO A DEVICE"
	};

	private readonly ListBox _list = new ListBox
	{
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Field,
		ForeColor = Theme.Text,
		Font = Theme.FBody,
		IntegralHeight = false
	};

	private readonly HudButton _bFind = new HudButton
	{
		Text = "Find devices"
	};

	private readonly HudButton _bSend = new HudButton
	{
		Text = "Send",
		Accent = Theme.Accent
	};

	private readonly HudField _fPeerPin = new HudField
	{
		Hint = "device's PIN, if any"
	};

	private readonly Label _lblStatus = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Dim,
		Font = Theme.FSmall
	};

	private readonly HudButton _bClose = new HudButton
	{
		Text = "Close"
	};

	private bool _loading = true;

	public LocalSendForm(AppSettings cfg, Func<List<string>> sources)
	{
		_cfg = cfg;
		_sources = sources;
		Text = "CopyForge - LocalSend";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(640, 470);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		base.ShowInTaskbar = false;
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_chkRecv, _fFolder, _bBrowse, _fAlias, _fPin, _lblSend, _list, _bFind, _bSend, _fPeerPin, _lblStatus, _bClose);
		_chkRecv.SetBounds(20, 44, 580, 22);
		_fFolder.SetBounds(20, 78, 450, 28);
		_bBrowse.SetBounds(480, 76, 120, 32);
		_fAlias.SetBounds(20, 118, 285, 28);
		_fPin.SetBounds(315, 118, 285, 28);
		_lblSend.SetBounds(20, 166, 580, 14);
		_list.SetBounds(20, 186, 440, 150);
		_bFind.SetBounds(470, 186, 130, 34);
		_fPeerPin.SetBounds(470, 230, 130, 28);
		_bSend.SetBounds(470, 270, 130, 34);
		_lblStatus.SetBounds(20, 346, 580, 70);
		_bClose.SetBounds(470, 424, 130, 34);
		_lblStatus.Text = "Receiving asks you before every transfer unless you set a PIN. Allow CopyForge through the Windows firewall for PRIVATE networks only. If the LocalSend app is open on this PC, close it first (both use port 53317).";
		_chkRecv.Checked = _cfg.LocalSendOn;
		_fFolder.Text = _cfg.LocalSendFolder;
		_fAlias.Text = _cfg.LocalSendAlias;
		_fPin.Text = _cfg.LocalSendPin;
		_bSend.Enabled = false;
		_loading = false;
		_chkRecv.CheckedChanged += delegate
		{
			_cfg.LocalSendOn = _chkRecv.Checked;
		};
		_fFolder.Inner.TextChanged += delegate
		{
			if (!_loading)
			{
				_cfg.LocalSendFolder = _fFolder.Text.Trim();
			}
		};
		_fAlias.Inner.TextChanged += delegate
		{
			if (!_loading)
			{
				_cfg.LocalSendAlias = _fAlias.Text.Trim();
			}
		};
		_fPin.Inner.TextChanged += delegate
		{
			if (!_loading)
			{
				_cfg.LocalSendPin = _fPin.Text.Trim();
			}
		};
		_bBrowse.Click += delegate
		{
			using FolderBrowserDialog dlg = new FolderBrowserDialog
			{
				Description = "Folder to save received files into",
				UseDescriptionForTitle = true
			};
			if (dlg.ShowDialog(this) == DialogResult.OK)
			{
				_fFolder.Text = dlg.SelectedPath;
			}
		};
		_list.SelectedIndexChanged += delegate
		{
			_bSend.Enabled = _list.SelectedIndex >= 0;
		};
		_bFind.Click += async delegate
		{
			await FindAsync();
		};
		_bSend.Click += async delegate
		{
			await SendAsync();
		};
		_bClose.Click += delegate
		{
			Close();
		};
		base.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Close();
			}
		};
		base.FormClosing += delegate
		{
			_cts.Cancel();
		};
	}

	private async Task FindAsync()
	{
		_bFind.Enabled = false;
		_lblStatus.Text = "Looking for devices (3 seconds)...";
		try
		{
			_peers = await LocalSendClient.DiscoverAsync(TimeSpan.FromSeconds(3), _cts.Token);
			_list.Items.Clear();
			foreach (LsPeer p in _peers)
			{
				_list.Items.Add($"{p.Alias}   ({p.Ip})");
			}
			_lblStatus.Text = (_peers.Count == 0) ? "No devices found. Open LocalSend on the other device (same network) and try again; UDP 53317 must be allowed in the firewall." : (_peers.Count + " device(s) found. Pick one and press SEND.");
		}
		catch (Exception ex)
		{
			_lblStatus.Text = "Search failed: " + ex.Message;
		}
		finally
		{
			_bFind.Enabled = true;
		}
	}

	private async Task SendAsync()
	{
		int i = _list.SelectedIndex;
		if (i < 0 || i >= _peers.Count)
		{
			return;
		}
		List<string> sources = _sources();
		if (sources.Count == 0)
		{
			HudDialog.Notify(this, "LOCALSEND", "The source list in the main window is empty.\nAdd the files or folders to send there first.");
			return;
		}
		LsPeer peer = _peers[i];
		string alias = (_cfg.LocalSendAlias.Length > 0) ? _cfg.LocalSendAlias : ("CopyForge " + Environment.MachineName);
		_bSend.Enabled = false;
		_bFind.Enabled = false;
		try
		{
			LsSendResult r = await Task.Run(() => LocalSendClient.SendAsync(peer, alias, _fPeerPin.Text.Trim(), sources, delegate(string m)
			{
				BeginInvoke(delegate
				{
					_lblStatus.Text = m;
				});
			}, delegate(long done, long total)
			{
				BeginInvoke(delegate
				{
					_lblStatus.Text = $"Sending to {peer.Alias}: {Theme.Bytes(done)} of {Theme.Bytes(total)}";
				});
			}, _cts.Token));
			_lblStatus.Text = r.Message;
			HudDialog.Notify(this, r.Ok ? "SENT" : "NOT SENT", r.Message, !r.Ok);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_lblStatus.Text = "Failed: " + ex.Message;
		}
		finally
		{
			_bFind.Enabled = true;
			_bSend.Enabled = _list.SelectedIndex >= 0;
		}
	}
}
