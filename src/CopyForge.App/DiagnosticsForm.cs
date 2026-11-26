using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopyForge.Engine;

namespace CopyForge.App;

internal sealed class DiagnosticsForm : Form
{
	private readonly Func<DiagnosticsInput> _capture;

	private readonly HudPanel _panel = new HudPanel
	{
		Title = "DIAGNOSTICS",
		TagText = "REPORT"
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

	private readonly HudButton _bSave = new HudButton
	{
		Text = "Save..."
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

	private string _report = "";

	private bool _busy;

	public DiagnosticsForm(Func<DiagnosticsInput> capture)
	{
		_capture = capture;
		Text = "CopyForge - Diagnostics";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(1040, 740);
		MinimumSize = SizeFromClientSize(new Size(760, 480));
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		NativeUi.DarkScroll(_rtb);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_rtb, _bRefresh, _bCopy, _bSave, _bClose, _stat);
		_panel.Resize += delegate
		{
			DoLayout();
		};
		DoLayout();
		_bRefresh.Click += async delegate
		{
			await RefreshAsync();
		};
		_bCopy.Click += delegate
		{
			if (_report.Length != 0)
			{
				try
				{
					Clipboard.SetText(_report.Replace("\n", Environment.NewLine));
					_stat.Text = "COPIED TO CLIPBOARD";
					_stat.Color = Theme.Ok;
				}
				catch (Exception ex)
				{
					_stat.Text = "COPY FAILED: " + ex.Message;
					_stat.Color = Theme.Bad;
				}
				_stat.Invalidate();
			}
		};
		_bSave.Click += delegate
		{
			Save();
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
				_ = RefreshAsync();
			}
		};
		base.Shown += async delegate
		{
			await RefreshAsync();
		};
	}

	private void DoLayout()
	{
		int width = _panel.Width;
		int height = _panel.Height;
		if (width >= 200 && height >= 150)
		{
			_rtb.SetBounds(12, 32, width - 24, height - 32 - 62);
			int y = height - 12 - 36;
			_bRefresh.SetBounds(12, y, 120, 36);
			_bCopy.SetBounds(140, y, 120, 36);
			_bSave.SetBounds(268, y, 120, 36);
			_bClose.SetBounds(width - 12 - 120, y, 120, 36);
			_stat.SetBounds(404, y, Math.Max(10, width - 404 - 144), 36);
		}
	}

	private async Task RefreshAsync()
	{
		if (_busy)
		{
			return;
		}
		_busy = true;
		_stat.Text = "GENERATING...";
		_stat.Color = Theme.Accent;
		_stat.Invalidate();
		_bRefresh.Enabled = false;
		try
		{
			DiagnosticsInput input = _capture();
			DiagnosticsResult diagnosticsResult = await Task.Run(() => DiagnosticsReport.Build(input));
			_report = diagnosticsResult.Text;
			Render(diagnosticsResult.Text);
			_stat.Color = ((diagnosticsResult.Fails > 0) ? Theme.Bad : ((diagnosticsResult.Warns > 0) ? Theme.Warn : Theme.Ok));
			_stat.Text = ((diagnosticsResult.Fails > 0) ? $"{diagnosticsResult.Fails} FAIL, {diagnosticsResult.Warns} WARN - ATTENTION NEEDED" : ((diagnosticsResult.Warns > 0) ? $"{diagnosticsResult.Warns} WARNING(S)" : "ALL CHECKS PASSED"));
		}
		catch (Exception ex)
		{
			_stat.Color = Theme.Bad;
			_stat.Text = "REPORT FAILED: " + ex.Message;
			CrashLog.Log("Diagnostics", ex);
		}
		finally
		{
			_bRefresh.Enabled = true;
			_busy = false;
			_stat.Invalidate();
		}
	}

	private void Render(string text)
	{
		_rtb.SuspendLayout();
		_rtb.Clear();
		_rtb.Text = text;
		_rtb.SelectAll();
		_rtb.SelectionColor = Theme.Text;
		_rtb.SelectionFont = Theme.Mono(9.5f);
		int num = 0;
		string[] array = text.Split('\n');
		foreach (string text2 in array)
		{
			Color? color = null;
			FontStyle fontStyle = FontStyle.Regular;
			if (text2.StartsWith("[ OK ]"))
			{
				color = Theme.Ok;
			}
			else if (text2.StartsWith("[WARN]"))
			{
				color = Theme.Warn;
			}
			else if (text2.StartsWith("[FAIL]"))
			{
				color = Theme.Bad;
			}
			else if (text2.StartsWith("[INFO]"))
			{
				color = Theme.A(Theme.Text, 200);
			}
			else if (text2.StartsWith("== "))
			{
				color = Theme.Accent;
				fontStyle = FontStyle.Bold;
			}
			else if (text2.StartsWith("COPYFORGE DIAGNOSTIC"))
			{
				color = Color.White;
				fontStyle = FontStyle.Bold;
			}
			else if (text2.StartsWith("Result:"))
			{
				color = (text2.Contains("ATTENTION") ? Theme.Bad : (text2.Contains("WARNINGS") ? Theme.Warn : Theme.Ok));
			}
			if (color.HasValue && text2.Length > 0)
			{
				_rtb.Select(num, text2.Length);
				_rtb.SelectionColor = color.Value;
				if (fontStyle != 0)
				{
					_rtb.SelectionFont = Theme.Mono(9.5f, fontStyle);
				}
			}
			num += text2.Length + 1;
		}
		_rtb.Select(0, 0);
		_rtb.ResumeLayout();
		_rtb.ScrollToCaret();
	}

	private void Save()
	{
		if (_report.Length == 0)
		{
			return;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Save diagnostic report",
			Filter = "Text file (*.txt)|*.txt",
			FileName = $"CopyForge-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
		};
		if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			try
			{
				File.WriteAllText(saveFileDialog.FileName, _report.Replace("\n", Environment.NewLine));
				_stat.Text = "SAVED: " + saveFileDialog.FileName;
				_stat.Color = Theme.Ok;
			}
			catch (Exception ex)
			{
				_stat.Text = "SAVE FAILED: " + ex.Message;
				_stat.Color = Theme.Bad;
			}
			_stat.Invalidate();
		}
	}
}
