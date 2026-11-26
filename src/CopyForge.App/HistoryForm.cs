using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CopyForge.Engine;

namespace CopyForge.App;

internal sealed class HistoryForm : Form
{
	private readonly HudPanel _panel = new HudPanel
	{
		Title = "JOB HISTORY",
		TagText = "LOG"
	};

	private readonly HudField _search = new HudField
	{
		Hint = "Search: folder, mode, OK / ERRORS / CANCELLED, date (2026-11-24) ..."
	};

	private readonly RichTextBox _rtb = new RichTextBox
	{
		ReadOnly = true,
		BorderStyle = BorderStyle.None,
		BackColor = Theme.Field,
		ForeColor = Theme.Text,
		Font = Theme.Mono(9.5f),
		WordWrap = false,
		ScrollBars = RichTextBoxScrollBars.Both,
		DetectUrls = false
	};

	private readonly HudButton _bRefresh = new HudButton
	{
		Text = "Refresh"
	};

	private readonly HudButton _bCopy = new HudButton
	{
		Text = "Copy"
	};

	private readonly HudButton _bClear = new HudButton
	{
		Text = "Clear history",
		Accent = Theme.Bad
	};

	private readonly HudButton _bClose = new HudButton
	{
		Text = "Close",
		Accent = Theme.Warn
	};

	private readonly StatusText _stat = new StatusText
	{
		Color = Theme.Dim,
		Text = ""
	};

	private string _text = "";

	private readonly System.Windows.Forms.Timer _typing = new System.Windows.Forms.Timer
	{
		Interval = 300
	};

	public HistoryForm()
	{
		Text = "CopyForge - History";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(1040, 700);
		MinimumSize = SizeFromClientSize(new Size(760, 440));
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		NativeUi.DarkScroll(_rtb);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_search, _rtb, _bRefresh, _bCopy, _bClear, _bClose, _stat);
		_panel.Resize += delegate
		{
			DoLayout();
		};
		DoLayout();
		_typing.Tick += delegate
		{
			_typing.Stop();
			Reload();
		};
		_search.Inner.TextChanged += delegate
		{
			_typing.Stop();
			_typing.Start();
		};
		_bRefresh.Click += delegate
		{
			Reload();
		};
		_bCopy.Click += delegate
		{
			if (_text.Length != 0)
			{
				try
				{
					Clipboard.SetText(_text.Replace("\n", Environment.NewLine));
					SetStat("COPIED TO CLIPBOARD", Theme.Ok);
				}
				catch (Exception ex)
				{
					SetStat("COPY FAILED: " + ex.Message, Theme.Bad);
				}
			}
		};
		_bClear.Click += delegate
		{
			if (HudDialog.Confirm(this, "CLEAR HISTORY", "Erase the whole job history?\n\n" + JobHistory.FilePath + "\n\nThis cannot be undone.", danger: true, "ERASE"))
			{
				JobHistory.Clear();
				Reload();
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
			else if (e.KeyCode == Keys.F5)
			{
				Reload();
			}
		};
		base.Shown += delegate
		{
			Reload();
			_search.Inner.Focus();
		};
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_typing.Dispose();
		}
		base.Dispose(disposing);
	}

	private void SetStat(string text, Color c)
	{
		_stat.Text = text;
		_stat.Color = c;
		_stat.Invalidate();
	}

	private void DoLayout()
	{
		int width = _panel.Width;
		int height = _panel.Height;
		if (width >= 200 && height >= 150)
		{
			_search.SetBounds(12, 32, width - 24, 28);
			_rtb.SetBounds(12, 68, width - 24, height - 68 - 62);
			int y = height - 12 - 36;
			_bRefresh.SetBounds(12, y, 110, 36);
			_bCopy.SetBounds(130, y, 110, 36);
			_bClear.SetBounds(248, y, 140, 36);
			_bClose.SetBounds(width - 12 - 120, y, 120, 36);
			_stat.SetBounds(404, y, Math.Max(10, width - 404 - 144), 36);
		}
	}

	private static Color OutcomeColor(string outcome)
	{
		return outcome switch
		{
			"OK" => Theme.Ok, 
			"CANCELLED" => Theme.Warn, 
			_ => Theme.Bad, 
		};
	}

	private void Reload()
	{
		string search = _search.Text;
		List<HistoryEntry> list = JobHistory.Read(500, search);
		StringBuilder sb = new StringBuilder();
		List<(int Start, int Length, Color C)> marks = new List<(int, int, Color)>();
		foreach (HistoryEntry e in list)
		{
			int start = sb.Length;
			string dest = e.Destination + ((e.ExtraDestinations.Count > 0) ? (" + " + string.Join(" + ", e.ExtraDestinations)) : "");
			string line = $"{e.Started:yyyy-MM-dd HH:mm}  {e.Outcome,-9} {e.Mode.ToUpperInvariant(),-6} {e.FilesProcessed,7:N0} files  {Fmt(e.Bytes),10}  {e.FilesSkipped:N0} skipped  {e.FilesFailed:N0} failed  {TimeSpan.FromSeconds(e.ElapsedSeconds):hh\\:mm\\:ss}" + (e.Verify ? ("  [verify " + e.VerifyAlgo + "]") : "") + "  [" + e.Source + "]";
			sb.Append(line).Append('\n');
			marks.Add((start, line.Length, OutcomeColor(e.Outcome)));
			string src = (e.Sources.Count == 0) ? "?" : (e.Sources[0] + ((e.Sources.Count > 1) ? $" (+{e.Sources.Count - 1})" : ""));
			sb.Append("    ").Append(src).Append("  ->  ").Append(dest).Append('\n');
			if (!string.IsNullOrEmpty(e.Problem))
			{
				sb.Append("    PROBLEM: ").Append(e.Problem).Append('\n');
			}
			foreach (string err in e.Errors.Take(3))
			{
				sb.Append("    ERROR: ").Append(err).Append('\n');
			}
			sb.Append('\n');
		}
		_text = sb.ToString();
		_rtb.SuspendLayout();
		_rtb.Clear();
		if (list.Count == 0)
		{
			_rtb.Text = (string.IsNullOrWhiteSpace(search) ? "No jobs recorded yet. Finished jobs from the window and from cfcopy appear here." : "No jobs match that search.");
			_rtb.SelectAll();
			_rtb.SelectionColor = Theme.Dim;
		}
		else
		{
			_rtb.Text = _text;
			_rtb.SelectAll();
			_rtb.SelectionColor = Theme.Text;
			_rtb.SelectionFont = Theme.Mono(9.5f);
			foreach (var (start2, length, c) in marks)
			{
				_rtb.Select(start2, length);
				_rtb.SelectionColor = c;
			}
		}
		_rtb.Select(0, 0);
		_rtb.ResumeLayout();
		SetStat((list.Count == 0) ? "NO ENTRIES" : $"{list.Count} JOB(S)  -  NEWEST FIRST", Theme.Dim);
	}

	private static string Fmt(long b)
	{
		string[] units = new string[5] { "B", "KB", "MB", "GB", "TB" };
		double v = b;
		int i = 0;
		while (v >= 1024.0 && i < units.Length - 1)
		{
			v /= 1024.0;
			i++;
		}
		return $"{v:0.##} {units[i]}";
	}
}
