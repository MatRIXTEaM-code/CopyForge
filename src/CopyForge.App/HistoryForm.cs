using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

	private readonly HudButton _bRecheck = new HudButton
	{
		Text = "Re-check newest"
	};

	private readonly HudButton _bRobo = new HudButton
	{
		Text = "Copy robocopy cmd"
	};

	private readonly HudButton _bHash = new HudButton
	{
		Text = "Check .sha256..."
	};

	private readonly HudButton _bPreset = new HudButton
	{
		Text = "Save as preset"
	};

	private bool _rechecking;

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
		base.ClientSize = new Size(1300, 700);
		MinimumSize = SizeFromClientSize(new Size(760, 440));
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		NativeUi.DarkScroll(_rtb);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_search, _rtb, _bRefresh, _bCopy, _bRecheck, _bRobo, _bHash, _bPreset, _bClear, _bClose, _stat);
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
		_bRecheck.Click += async delegate
		{
			if (_rechecking)
			{
				return;
			}
			HistoryEntry e = JobHistory.Read(1, _search.Text).FirstOrDefault();
			if (e == null)
			{
				SetStat("NOTHING TO RE-CHECK", Theme.Warn);
				return;
			}
			if (e.Destination.Length == 0 || e.Mode.Equals("delete", StringComparison.OrdinalIgnoreCase) || e.Mode.Equals("move", StringComparison.OrdinalIgnoreCase))
			{
				SetStat("A " + e.Mode.ToUpperInvariant() + " JOB CANNOT BE RE-CHECKED", Theme.Warn);
				return;
			}
			List<string> present = e.Sources.Where((string x) => System.IO.File.Exists(x) || System.IO.Directory.Exists(x)).ToList();
			if (present.Count == 0)
			{
				SetStat("THE SOURCES NO LONGER EXIST", Theme.Warn);
				return;
			}
			_rechecking = true;
			SetStat("RE-CHECKING...", Theme.Accent);
			try
			{
				long diff = 0L;
				long total = 0L;
				foreach (string dest in new string[1] { e.Destination }.Concat(e.ExtraDestinations))
				{
					JobOptions o = new JobOptions();
					o.Mode = JobMode.Copy;
					o.DryRun = true;
					o.Sources = present;
					o.Destination = dest;
					JobResult jr = await Task.Run(() => new CopyJob(o).RunAsync(System.Threading.CancellationToken.None));
					if (jr.Preview != null)
					{
						diff += jr.Preview.CopyCount + jr.Preview.DeleteCount;
						total += jr.Preview.CopyCount + jr.Preview.SkipCount;
					}
					diff += jr.FilesFailed;
				}
				SetStat((diff == 0L) ? $"RE-CHECK: IDENTICAL ({total:N0} FILES)" : $"RE-CHECK: {diff:N0} FILE(S) MISSING OR DIFFERENT", (diff == 0L) ? Theme.Ok : Theme.Bad);
			}
			catch (Exception ex)
			{
				SetStat("RE-CHECK FAILED: " + ex.Message, Theme.Bad);
			}
			finally
			{
				_rechecking = false;
			}
		};
		_bRobo.Click += delegate
		{
			HistoryEntry e = JobHistory.Read(1, _search.Text).FirstOrDefault();
			if (e == null || e.Destination.Length == 0)
			{
				SetStat("NO JOB TO CONVERT", Theme.Warn);
				return;
			}
			JobOptions o = e.Options ?? new JobOptions();
			o.Mode = Enum.TryParse(e.Mode, ignoreCase: true, out JobMode m) ? m : JobMode.Copy;
			o.Sources = e.Sources.ToList();
			o.Destination = e.Destination;
			(List<string> cmds, List<string> notes) = RoboCommand.Build(o);
			string txt = string.Join(Environment.NewLine, cmds.Concat(notes.Select((string n) => "REM " + n)));
			try
			{
				Clipboard.SetText(txt);
				SetStat("ROBOCOPY COMMAND COPIED (" + cmds.Count + ")", Theme.Ok);
			}
			catch (Exception ex)
			{
				SetStat("COPY FAILED: " + ex.Message, Theme.Bad);
			}
		};
		_bPreset.Click += delegate
		{
			HistoryEntry e = JobHistory.Read(1, _search.Text).FirstOrDefault();
			if (e == null || e.Options == null)
			{
				SetStat("NEWEST JOB HAS NO SAVED OPTIONS (OLDER ENTRY)", Theme.Warn);
				return;
			}
			static string Leaf(string p)
			{
				string t = p.TrimEnd('\\', '/');
				string n = System.IO.Path.GetFileName(t);
				return n.Length > 0 ? n : t;
			}
			string name = e.Mode + " " + Leaf(e.Sources.FirstOrDefault() ?? "") + " -> " + Leaf(e.Destination);
			if (Presets.Add(name, e.Options, out string why))
			{
				SetStat("PRESET SAVED: " + name, Theme.Ok);
			}
			else
			{
				SetStat(why.ToUpperInvariant(), Theme.Warn);
			}
		};
		_bHash.Click += async delegate
		{
			using OpenFileDialog dlg = new OpenFileDialog
			{
				Title = "Checksum file (sha256sum format)",
				Filter = "Checksum files|*.sha256;*.sha256sum;*.txt|All files|*.*"
			};
			if (dlg.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			SetStat("CHECKING HASHES...", Theme.Accent);
			try
			{
				HashFileResult r = await HashFile.VerifyAsync(dlg.FileName, System.IO.Path.GetDirectoryName(dlg.FileName) ?? ".", 0, null, System.Threading.CancellationToken.None);
				bool good = r.Bad.Count == 0 && r.Missing.Count == 0;
				SetStat(good ? $"HASHES OK ({r.Ok:N0} FILES)" : $"{r.Bad.Count:N0} BAD, {r.Missing.Count:N0} MISSING OF {r.Checked:N0}", good ? Theme.Ok : Theme.Bad);
			}
			catch (Exception ex)
			{
				SetStat("HASH CHECK FAILED: " + ex.Message, Theme.Bad);
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
			_bRecheck.SetBounds(396, y, 150, 36);
			_bRobo.SetBounds(554, y, 170, 36);
			_bHash.SetBounds(732, y, 150, 36);
			_bPreset.SetBounds(890, y, 140, 36);
			_bClose.SetBounds(width - 12 - 120, y, 120, 36);
			_stat.SetBounds(1038, y, Math.Max(10, width - 1038 - 144), 36);
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
