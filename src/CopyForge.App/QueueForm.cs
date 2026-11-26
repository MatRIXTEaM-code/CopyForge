using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class QueueForm : Form
{
	private readonly List<QueuedJob> _queue;

	private readonly Func<Task<QueuedJob?>> _addCurrent;

	private readonly HudPanel _panel = new HudPanel
	{
		Title = "JOB QUEUE",
		TagText = "SEQUENTIAL"
	};

	private readonly ListBox _list = new ListBox
	{
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Field,
		ForeColor = Theme.Text,
		Font = Theme.FBody,
		IntegralHeight = false
	};

	private readonly Label _lblInfo = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Dim,
		Font = Theme.FSmall
	};

	private readonly HudButton _bAdd = new HudButton
	{
		Text = "Add current",
		Accent = Theme.LineHi
	};

	private readonly HudButton _bRemove = new HudButton
	{
		Text = "Remove"
	};

	private readonly HudButton _bClear = new HudButton
	{
		Text = "Clear"
	};

	private readonly HudButton _bRun = new HudButton
	{
		Text = "Run queue",
		Accent = Theme.Accent
	};

	private readonly HudButton _bClose = new HudButton
	{
		Text = "Close"
	};

	public bool RunRequested { get; private set; }

	public QueueForm(List<QueuedJob> queue, Func<Task<QueuedJob?>> addCurrent)
	{
		_queue = queue;
		_addCurrent = addCurrent;
		Text = "CopyForge - Job queue";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(640, 420);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		base.ShowInTaskbar = false;
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_list, _lblInfo, _bAdd, _bRemove, _bClear, _bRun, _bClose);
		_list.SetBounds(20, 44, 580, 226);
		_lblInfo.SetBounds(20, 278, 580, 54);
		_lblInfo.Text = "Set up a job in the main window (folders, destination, mode and options), press ADD CURRENT, then set up the next one. RUN QUEUE does them in order and stops if you abort one. The queue lives while CopyForge is open.";
		_bAdd.SetBounds(20, 344, 130, 34);
		_bRemove.SetBounds(158, 344, 100, 34);
		_bClear.SetBounds(266, 344, 90, 34);
		_bRun.SetBounds(380, 344, 130, 34);
		_bClose.SetBounds(518, 344, 82, 34);
		Fill();
		_bAdd.Click += async delegate
		{
			_bAdd.Enabled = false;
			try
			{
				QueuedJob queuedJob = await _addCurrent();
				if (queuedJob != null)
				{
					_queue.Add(queuedJob);
					Fill();
				}
			}
			finally
			{
				_bAdd.Enabled = true;
			}
		};
		_bRemove.Click += delegate
		{
			int selectedIndex = _list.SelectedIndex;
			if (selectedIndex >= 0)
			{
				_queue.RemoveAt(selectedIndex);
				Fill();
			}
		};
		_bClear.Click += delegate
		{
			_queue.Clear();
			Fill();
		};
		_bRun.Click += delegate
		{
			if (_queue.Count != 0)
			{
				RunRequested = true;
				Close();
			}
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
	}

	private void Fill()
	{
		_list.Items.Clear();
		for (int i = 0; i < _queue.Count; i++)
		{
			_list.Items.Add($"{i + 1,2}. {_queue[i].Title}");
		}
		_bRun.Enabled = _queue.Count > 0;
	}
}
