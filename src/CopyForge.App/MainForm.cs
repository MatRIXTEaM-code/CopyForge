using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopyForge.Engine;

namespace CopyForge.App;

public sealed class MainForm : Form
{
	private const int LeftW = 350;

	private const int Gap = 10;

	private readonly HeaderBar _header = new HeaderBar();

	private readonly HudPanel _pSrc = new HudPanel
	{
		Title = "SOURCE",
		TagText = "INPUT"
	};

	private readonly HudPanel _pDst = new HudPanel
	{
		Title = "DESTINATION",
		TagText = "OUTPUT"
	};

	private readonly HudPanel _pMode = new HudPanel
	{
		Title = "OPERATION",
		TagText = "MODE"
	};

	private readonly HudPanel _pEng = new HudPanel
	{
		Title = "ENGINE",
		TagText = "I/O"
	};

	private readonly HudPanel _pFilt = new HudPanel
	{
		Title = "FILTERS",
		TagText = "SELECT"
	};

	private readonly HudField _fSrc = new HudField
	{
		Multiline = true,
		Hint = "DROP FILES / FOLDERS HERE  -  ONE PATH PER LINE"
	};

	private readonly HudField _fDst = new HudField
	{
		Hint = "DESTINATION FOLDER   (SEVERAL: SEPARATE WITH ;)"
	};

	private readonly HudField _fInc = new HudField
	{
		Hint = "*.xls;*.docx;reports/*.csv   (EMPTY = EVERYTHING)"
	};

	private readonly HudField _fExc = new HudField
	{
		Hint = "*.tmp;Thumbs.db;node_modules/"
	};

	private readonly HudButton _bFolder = new HudButton
	{
		Text = "+ Folder"
	};

	private readonly HudButton _bFiles = new HudButton
	{
		Text = "+ Files"
	};

	private readonly HudButton _bClear = new HudButton
	{
		Text = "Clear"
	};

	private readonly HudButton _bBrowse = new HudButton
	{
		Text = "Browse"
	};

	private readonly HudSegmented _segMode = new HudSegmented
	{
		Items = new string[4] { "COPY", "MOVE", "SYNC", "DELETE" }
	};

	private readonly HudSegmented _segOver = new HudSegmented
	{
		Items = new string[4] { "DIFFERENT", "NEWER", "ALWAYS", "NEVER" }
	};

	private readonly HudLabel _lblOver = new HudLabel
	{
		Text = "Overwrite policy"
	};

	private readonly HudCheck _chkVerify = new HudCheck
	{
		Text = "Verify after copy  (xxHash3)"
	};

	private readonly HudStepper _stThreads = new HudStepper
	{
		Label = "Threads",
		Min = 0L,
		Max = 64L,
		Value = 0L,
		ZeroText = "AUTO"
	};

	private readonly HudSegmented _segIo = new HudSegmented
	{
		Items = new string[3] { "I/O  AUTO", "CACHED", "DIRECT" }
	};

	private readonly HudCheck _chkFlush = new HudCheck
	{
		Text = "Flush to disk",
		Checked = true
	};

	private readonly HudCheck _chkDirTimes = new HudCheck
	{
		Text = "Folder times",
		Checked = true
	};

	private readonly HudCheck _chkExtras = new HudCheck
	{
		Text = "NTFS ACL+streams"
	};

	private readonly HudCheck _chkSplit = new HudCheck
	{
		Text = "Split >4GB files"
	};

	private readonly HudCheck _chkRecycle = new HudCheck
	{
		Text = "Delete to Recycle Bin"
	};

	private readonly HudStepper _stBuffer = new HudStepper
	{
		Label = "Buffer",
		Min = 1L,
		Max = 64L,
		Value = 4L,
		Suffix = " MB"
	};

	private readonly HudLabel _lblInc = new HudLabel
	{
		Text = "Include"
	};

	private readonly HudLabel _lblExc = new HudLabel
	{
		Text = "Exclude  (trailing / = folders)"
	};

	private readonly HudStepper _stMin = new HudStepper
	{
		Label = "Min MB",
		Min = 0L,
		Max = 10000000L,
		Step = 10L
	};

	private readonly HudStepper _stMax = new HudStepper
	{
		Label = "Max MB",
		Min = 0L,
		Max = 10000000L,
		Step = 100L
	};

	private readonly HudCheck _chkSince = new HudCheck
	{
		Text = "Modified in last"
	};

	private readonly HudStepper _stDays = new HudStepper
	{
		Min = 1L,
		Max = 36500L,
		Value = 7L,
		Suffix = " d"
	};

	private readonly HudPanel _pHero = new HudPanel
	{
		Title = "TRANSFER",
		TagText = "STANDBY"
	};

	private readonly HudPanel _pGraph = new HudPanel
	{
		Title = "THROUGHPUT",
		TagText = "LIVE"
	};

	private readonly HudPanel _pLog = new HudPanel
	{
		Title = "EVENT LOG",
		TagText = "STREAM"
	};

	private readonly HudPanel _pCmd = new HudPanel
	{
		Title = "COMMAND",
		TagText = "EXECUTE"
	};

	private readonly GaugeView _gauge = new GaugeView();

	private readonly Readout _readout = new Readout
	{
		Caption = "Throughput",
		Value = "0.0",
		Unit = "MB/s"
	};

	private readonly StatTile _tFiles = new StatTile
	{
		Caption = "Files"
	};

	private readonly StatTile _tData = new StatTile
	{
		Caption = "Data"
	};

	private readonly StatTile _tSkipped = new StatTile
	{
		Caption = "Skipped"
	};

	private readonly StatTile _tFailed = new StatTile
	{
		Caption = "Failed"
	};

	private readonly StatTile _tElapsed = new StatTile
	{
		Caption = "Elapsed"
	};

	private readonly StatTile _tEta = new StatTile
	{
		Caption = "ETA"
	};

	private readonly SegBar _segBar = new SegBar
	{
		Height = 14
	};

	private readonly StatusText _ticker = new StatusText
	{
		ShowLed = false,
		Color = Theme.Dim
	};

	private readonly GraphView _graph = new GraphView();

	private readonly LogView _log = new LogView();

	private readonly HudButton _bStart = new HudButton
	{
		Text = "Execute"
	};

	private readonly HudButton _bCancel = new HudButton
	{
		Text = "Abort",
		Accent = Theme.Warn,
		Enabled = false
	};

	private readonly StatusText _status = new StatusText
	{
		Color = Theme.Dim,
		Text = "READY"
	};

	private readonly HudButton _bQueue = new HudButton
	{
		Text = "Queue",
		Accent = Theme.LineHi
	};

	private readonly List<QueuedJob> _queue = new List<QueuedJob>();

	private bool _queueRunning;

	private readonly HudButton _bHistory = new HudButton
	{
		Text = "History",
		Accent = Theme.LineHi
	};

	private readonly HudButton _bDiag = new HudButton
	{
		Text = "Diagnostics",
		Accent = Theme.LineHi
	};

	private readonly HudButton _bSettings = new HudButton
	{
		Text = "Settings",
		Accent = Theme.LineHi
	};

	private readonly HudButton _bPause = new HudButton
	{
		Text = "Pause",
		Accent = Theme.Warn,
		Visible = false
	};

	private readonly HudButton _bEject = new HudButton
	{
		Text = "Eject",
		Accent = Theme.Warn,
		Visible = false
	};

	private string? _ejectPath;

	private readonly HudButton _bRetry = new HudButton
	{
		Text = "Retry failed",
		Accent = Theme.Warn,
		Visible = false
	};

	private readonly ToolTip _tips = new ToolTip
	{
		InitialDelay = 450,
		AutoPopDelay = 14000,
		ReshowDelay = 200
	};

	private readonly AppSettings _cfg = AppSettings.Load();

	private List<FilePair>? _failedPairs;

	private JobOptions? _failedOpts;

	private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer
	{
		Interval = 33
	};

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly List<(HudPanel Panel, int DelayMs)> _boot = new List<(HudPanel, int)>();

	private readonly List<(int AtMs, string Text, Color? Color)> _bootLog = new List<(int, string, Color?)>();

	private int _bootLogIdx;

	private bool _bootDone;

	private long _lastHeaderMs;

	private long _lastPollMs;

	private long _lastFiles;

	private double _gaugeTarget;

	private CopyJob? _job;

	private CancellationTokenSource? _cts;

	private Bitmap? _bgCache;

	private JobRecord? _lastRec;

	private long _lastPollMsAnim;

	private JobMode CurrentMode => _segMode.SelectedIndex switch
	{
		1 => JobMode.Move, 
		2 => JobMode.Sync, 
		3 => JobMode.Delete, 
		_ => JobMode.Copy, 
	};

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

	public MainForm(string[] args)
	{
		Text = "CopyForge";
		BackColor = Theme.Bg;
		ForeColor = Theme.Text;
		DoubleBuffered = true;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.ClientSize = new Size(1260, 900);
		MinimumSize = SizeFromClientSize(new Size(1120, 870));
		BuildLeft();
		BuildRight();
		WireEvents();
		LoadSettings();
		ApplyMode();
		foreach (string text in args)
		{
			if (File.Exists(text) || Directory.Exists(text))
			{
				_fSrc.AppendText(text + Environment.NewLine);
			}
		}
		_boot.AddRange(new(HudPanel, int)[9]
		{
			(_pHero, 250),
			(_pSrc, 150),
			(_pDst, 250),
			(_pMode, 350),
			(_pEng, 450),
			(_pFilt, 550),
			(_pGraph, 450),
			(_pLog, 600),
			(_pCmd, 700)
		});
		foreach (var item in _boot)
		{
			item.Panel.Reveal = 0f;
		}
		_header.Reveal = 0f;
		_bootLog.AddRange(new(int, string, Color?)[5]
		{
			(100, "CopyForge engine v1.0 initialising...", null),
			(450, "I/O: direct + cached pipelines, native copy path armed", null),
			(800, "Integrity: verifier ready (xxHash3 / xxHash128 / SHA-256)", null),
			(1100, "Filters: wildcard, size and date rules loaded", null),
			(1450, "ENGINE ONLINE  -  awaiting job", Theme.Ok)
		});
		_anim.Tick += delegate
		{
			OnAnimTick();
		};
		_anim.Start();
		LayoutAll();
	}

	private void BuildLeft()
	{
		base.Controls.Add(_header);
		_header.Status = "ENGINE IDLE";
		_pSrc.Controls.AddRange(_fSrc, _bFolder, _bFiles, _bClear);
		_fSrc.SetBounds(12, 32, 326, 66);
		_fSrc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_bFolder.SetBounds(12, 106, 104, 26);
		_bFiles.SetBounds(124, 106, 104, 26);
		_bClear.SetBounds(236, 106, 80, 26);
		_pDst.Controls.AddRange(_fDst, _bBrowse);
		_fDst.SetBounds(12, 34, 230, 28);
		_bBrowse.SetBounds(250, 34, 88, 28);
		_bBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		_fDst.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_pMode.Controls.AddRange(_segMode, _lblOver, _segOver);
		_segMode.SetBounds(12, 34, 326, 30);
		_lblOver.SetBounds(12, 74, 200, 14);
		_segOver.SetBounds(12, 92, 326, 28);
		HudSegmented segMode = _segMode;
		AnchorStyles anchor = (_segOver.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
		segMode.Anchor = anchor;
		_pEng.Controls.AddRange(_chkVerify, _stThreads, _stBuffer, _segIo, _chkFlush, _chkDirTimes, _chkExtras, _chkSplit, _chkRecycle);
		_chkVerify.SetBounds(12, 34, 326, 22);
		_stThreads.SetBounds(12, 62, 158, 26);
		_stBuffer.SetBounds(180, 62, 158, 26);
		_segIo.SetBounds(12, 94, 326, 26);
		_chkFlush.SetBounds(12, 130, 164, 22);
		_chkDirTimes.SetBounds(186, 130, 152, 22);
		_chkExtras.SetBounds(12, 156, 164, 22);
		_chkSplit.SetBounds(186, 156, 152, 22);
		_chkRecycle.SetBounds(12, 182, 326, 22);
		_segIo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_tips.SetToolTip(_chkVerify, "Re-read every file after copying and compare checksums. On Windows this bypasses the file cache, so it checks what the drive really stored.");
		_tips.SetToolTip(_stThreads, "Files copied at the same time. AUTO picks from the drive types (USB sticks and hard disks get fewer, SSDs and network shares more).");
		_tips.SetToolTip(_stBuffer, "Block size for big files.");
		_tips.SetToolTip(_segIo, "AUTO: bypass the Windows file cache for big local files (faster, keeps your RAM free), cached I/O otherwise. DIRECT / CACHED force one way.");
		_tips.SetToolTip(_chkFlush, "Force each file onto the drive before it counts as finished. Safer for USB sticks; slightly slower.");
		_tips.SetToolTip(_chkDirTimes, "Give the copied folders the original modified/created times.");
		_tips.SetToolTip(_chkExtras, "Copy NTFS permissions and alternate data streams (e.g. 'downloaded from the internet' marks). NTFS/ReFS destinations only.");
		_tips.SetToolTip(_chkSplit, "FAT32 destination only: write files over 4 GB as name.ext.001, .002 ... instead of refusing them. Join with:  copy /b name.ext.001+name.ext.002 name.ext");
		_tips.SetToolTip(_chkRecycle, "Delete mode: send files to the Recycle Bin instead of deleting them permanently (slower; not available on network drives).");
		_pFilt.Controls.AddRange(_lblInc, _fInc, _lblExc, _fExc, _stMin, _stMax, _chkSince, _stDays);
		_lblInc.SetBounds(12, 34, 300, 14);
		_fInc.SetBounds(12, 50, 326, 26);
		_lblExc.SetBounds(12, 84, 300, 14);
		_fExc.SetBounds(12, 100, 326, 26);
		_stMin.SetBounds(12, 138, 158, 26);
		_stMax.SetBounds(180, 138, 158, 26);
		_chkSince.SetBounds(12, 172, 170, 22);
		_stDays.SetBounds(218, 170, 120, 26);
		HudField fInc = _fInc;
		anchor = (_fExc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
		fInc.Anchor = anchor;
		base.Controls.AddRange(_pSrc, _pDst, _pMode, _pEng, _pFilt);
	}

	private void BuildRight()
	{
		_pHero.Controls.AddRange(_gauge, _readout, _tFiles, _tData, _tSkipped, _tFailed, _tElapsed, _tEta, _segBar, _ticker);
		_pGraph.Controls.Add(_graph);
		_pLog.Controls.Add(_log);
		_pCmd.Controls.AddRange(_bStart, _bCancel, _bPause, _bRetry, _bEject, _status, _bHistory, _bQueue, _bSettings, _bDiag);
		_bStart.Accent = Theme.Accent;
		base.Controls.AddRange(_pHero, _pGraph, _pLog, _pCmd);
		ApplyStatTileColors();
	}

	private void ApplyStatTileColors()
	{
		_tFailed.ValueColor = Theme.Dim;
	}

	private void LayoutAll()
	{
		int width = base.ClientSize.Width;
		int height = base.ClientSize.Height;
		_header.SetBounds(14, 8, width - 28, 40);
		int num = 58;
		int[] array = new int[5] { 142, 78, 130, 208, 204 };
		HudPanel[] array2 = new HudPanel[5] { _pSrc, _pDst, _pMode, _pEng, _pFilt };
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].SetBounds(14, num, 350, array[i]);
			num += array[i] + 10;
		}
		int num2 = 376;
		int num3 = width - num2 - 14;
		if (num3 >= 300)
		{
			int num4 = 58;
			_pHero.SetBounds(num2, num4, num3, 296);
			num4 += 306;
			_pGraph.SetBounds(num2, num4, num3, 150);
			num4 += 160;
			int num5 = Math.Max(90, height - 14 - 84 - 10 - num4);
			_pLog.SetBounds(num2, num4, num3, num5);
			num4 += num5 + 10;
			_pCmd.SetBounds(num2, num4, num3, 84);
			_gauge.SetBounds(16, 36, 200, 200);
			int num6 = 240;
			int num7 = num3 - num6 - 16;
			_readout.SetBounds(num6, 34, num7, 70);
			int num8 = (num7 - 16) / 3;
			StatTile[] array3 = new StatTile[6] { _tFiles, _tData, _tSkipped, _tFailed, _tElapsed, _tEta };
			for (int j = 0; j < array3.Length; j++)
			{
				array3[j].SetBounds(num6 + j % 3 * (num8 + 8), 112 + j / 3 * 60, num8, 52);
			}
			_segBar.SetBounds(16, 248, num3 - 32, 14);
			_ticker.SetBounds(16, 268, num3 - 32, 18);
			_graph.SetBounds(10, 30, num3 - 20, 112);
			_log.SetBounds(10, 30, num3 - 20, num5 - 40);
			_bStart.SetBounds(14, 32, 170, 40);
			_bCancel.SetBounds(192, 32, 110, 40);
			_bPause.SetBounds(310, 32, 120, 40);
			_bRetry.SetBounds(310, 32, 120, 40);
			_bEject.SetBounds(310, 32, 120, 40);
			int rightEdge = num3 - 14;
			int wBig = 120, wSmall = 90, gap = 8;
			// Right-hand button group; shrinks further on very narrow windows so it never overlaps Pause/Retry (ends at x=430).
			int need = wBig + 3 * (wSmall + gap);
			int avail = rightEdge - 438;
			if (avail < need)
			{
				int scale = Math.Max(60, (avail - 3 * gap) / 4);
				wBig = scale; wSmall = scale;
			}
			int x = rightEdge - wBig;
			_bDiag.SetBounds(x, 32, wBig, 40);
			x -= gap + wSmall; _bSettings.SetBounds(x, 32, wSmall, 40);
			x -= gap + wSmall; _bQueue.SetBounds(x, 32, wSmall, 40);
			x -= gap + wSmall; _bHistory.SetBounds(x, 32, wSmall, 40);
			// Status text lives in the header strip of the command panel (next to the COMMAND tab), so it can never collide with the buttons.
			int statusW = num3 - 14 - 110 - 130;
			_status.Visible = statusW >= 80;
			_status.SetBounds(130, 5, Math.Max(10, statusW), 22);
			_bgCache?.Dispose();
			_bgCache = null;
			Invalidate();
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (_pHero != null && base.WindowState != FormWindowState.Minimized)
		{
			LayoutAll();
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			int value = 1;
			DwmSetWindowAttribute(base.Handle, 20, ref value, 4);
		}
		catch
		{
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (base.ClientSize.Width <= 0 || base.ClientSize.Height <= 0)
		{
			return;
		}
		if (_bgCache == null || _bgCache.Size != base.ClientSize)
		{
			_bgCache?.Dispose();
			_bgCache = new Bitmap(base.ClientSize.Width, base.ClientSize.Height);
			using Graphics graphics = Graphics.FromImage(_bgCache);
			graphics.Clear(Theme.Bg);
			using (SolidBrush brush = new SolidBrush(Theme.A(Theme.Line, 90)))
			{
				for (int i = 6; i < _bgCache.Height; i += 22)
				{
					for (int j = 6; j < _bgCache.Width; j += 22)
					{
						graphics.FillRectangle(brush, j, i, 1, 1);
					}
				}
			}
			using GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddEllipse((float)(-_bgCache.Width) * 0.25f, (float)(-_bgCache.Height) * 0.25f, (float)_bgCache.Width * 1.5f, (float)_bgCache.Height * 1.5f);
			PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath);
			pathGradientBrush.CenterColor = Color.FromArgb(0, 0, 0, 0);
			pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(150, 0, 0, 0) };
			using PathGradientBrush brush2 = pathGradientBrush;
			graphics.FillRectangle(brush2, 0, 0, _bgCache.Width, _bgCache.Height);
		}
		e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
	}

	private NotifyIcon? _tray;

	/// <summary>Windows balloon / toast when a job ends while CopyForge is minimised or in the background.</summary>
	private void NotifyFinished(string title, string text, bool bad)
	{
		try
		{
			if (base.WindowState != FormWindowState.Minimized && Form.ActiveForm == this)
			{
				return;
			}
			_tray ??= new NotifyIcon
			{
				Icon = (base.Icon ?? SystemIcons.Application),
				Text = "CopyForge"
			};
			_tray.Visible = true;
			_tray.BalloonTipTitle = title;
			_tray.BalloonTipText = text;
			_tray.BalloonTipIcon = (bad ? ToolTipIcon.Warning : ToolTipIcon.Info);
			_tray.ShowBalloonTip(8000);
		}
		catch
		{
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_anim.Dispose();
			_tray?.Dispose();
			_bgCache?.Dispose();
			_cts?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void WireEvents()
	{
		_bFolder.Click += delegate
		{
			AddSourceFolder();
		};
		_bFiles.Click += delegate
		{
			AddSourceFiles();
		};
		_bClear.Click += delegate
		{
			_fSrc.Clear();
		};
		_bBrowse.Click += delegate
		{
			BrowseDest();
		};
		_bDiag.Click += delegate
		{
			OpenDiagnostics();
		};
		_bHistory.Click += delegate
		{
			using HistoryForm historyForm = new HistoryForm();
			historyForm.ShowDialog(this);
		};
		_bQueue.Click += async delegate
		{
			await OpenQueueAsync();
		};
		_bSettings.Click += delegate
		{
			using SettingsForm settingsForm = new SettingsForm(_cfg);
			settingsForm.ShowDialog(this);
			_cfg.Save();
			UpdateVerifyLabel();
		};
		_bPause.Click += delegate
		{
			CopyJob job = _job;
			if (job != null)
			{
				if (job.IsPaused)
				{
					job.Resume();
					_bPause.Text = "Pause";
					SetStatus("TRANSFER ACTIVE", Theme.Accent, pulse: true);
				}
				else
				{
					job.Pause();
					_bPause.Text = "Resume";
					SetStatus("PAUSED", Theme.Warn, pulse: false);
				}
			}
		};
		_bEject.Click += async delegate
		{
			string path = _ejectPath;
			if (path == null)
			{
				_bEject.Visible = false;
				return;
			}
			_bEject.Enabled = false;
			string msg = "";
			bool ok = await Task.Run(() => DriveEject.Eject(path, out msg, 10));
			_log.Add(msg, ok ? Theme.Ok : Theme.Warn);
			_bEject.Enabled = true;
			if (ok)
			{
				_ejectPath = null;
				_bEject.Visible = false;
				SetStatus("EJECTED", Theme.Ok, pulse: false);
			}
		};
		_bRetry.Click += async delegate
		{
			List<FilePair> failedPairs = _failedPairs;
			if (failedPairs != null && failedPairs.Count > 0 && _failedOpts != null)
			{
				JobOptions jobOptions = _failedOpts.Clone();
				jobOptions.Sources = new List<string>();
				jobOptions.ExplicitPairs = _failedPairs.ToList();
				_bRetry.Visible = false;
				await StartAsync(jobOptions);
			}
		};
		_segMode.SelectedIndexChanged += delegate
		{
			ApplyMode();
		};
		_chkSince.CheckedChanged += delegate
		{
			_stDays.Enabled = _chkSince.Checked;
		};
		_stDays.Enabled = false;
		_bStart.Click += async delegate
		{
			await StartAsync();
		};
		_bCancel.Click += delegate
		{
			_cts?.Cancel();
			_bCancel.Enabled = false;
			SetStatus("ABORTING...", Theme.Warn, pulse: true);
		};
		_fSrc.Inner.AllowDrop = true;
		_fSrc.Inner.DragEnter += DragEnterHandler;
		_fSrc.Inner.DragDrop += delegate(object? _, DragEventArgs e)
		{
			if (e.Data?.GetData(DataFormats.FileDrop) is string[] array2)
			{
				string[] array3 = array2;
				foreach (string text in array3)
				{
					_fSrc.AppendText(text + Environment.NewLine);
				}
			}
		};
		_fDst.Inner.AllowDrop = true;
		_fDst.Inner.DragEnter += DragEnterHandler;
		_fDst.Inner.DragDrop += delegate(object? _, DragEventArgs e)
		{
			if (e.Data?.GetData(DataFormats.FileDrop) is string[] array && array.Length > 0)
			{
				_fDst.Text = (Directory.Exists(array[0]) ? array[0] : (Path.GetDirectoryName(array[0]) ?? ""));
			}
		};
		base.FormClosing += delegate(object? _, FormClosingEventArgs e)
		{
			SaveSettings();
			if (_job != null && !HudDialog.Confirm(this, "JOB RUNNING", "A transfer is still running.\nAbort it and exit?", danger: true, "ABORT + EXIT", "STAY"))
			{
				e.Cancel = true;
			}
			else
			{
				_cts?.Cancel();
			}
		};
	}

	private static void DragEnterHandler(object? s, DragEventArgs e)
	{
		IDataObject? data = e.Data;
		e.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
	}

	private void ApplyMode()
	{
		JobMode currentMode = CurrentMode;
		bool flag = currentMode == JobMode.Delete;
		_fDst.Locked = flag;
		_bBrowse.Enabled = !flag;
		_segOver.Enabled = !flag;
		_chkVerify.Enabled = !flag;
		_stBuffer.Enabled = !flag;
		_segIo.Enabled = !flag;
		_chkFlush.Enabled = !flag;
		_chkDirTimes.Enabled = !flag;
		_chkExtras.Enabled = !flag;
		_chkSplit.Enabled = !flag;
		_chkRecycle.Enabled = flag;
		HudButton bStart = _bStart;
		bStart.Text = currentMode switch
		{
			JobMode.Move => "Execute Move", 
			JobMode.Sync => "Execute Sync", 
			JobMode.Delete => "Execute Delete", 
			_ => "Execute Copy", 
		};
		_bStart.Accent = ((currentMode == JobMode.Delete || currentMode == JobMode.Sync) ? Theme.Bad : Theme.Accent);
		GaugeView gauge = _gauge;
		Color arcColor = (_segBar.BarColor = currentMode switch
		{
			JobMode.Sync => Theme.Warn, 
			JobMode.Delete => Theme.Bad, 
			_ => Theme.Accent, 
		});
		gauge.ArcColor = arcColor;
		_pHero.AccentColor = _gauge.ArcColor;
		_pHero.TagText = ((_job == null) ? (currentMode.ToString().ToUpperInvariant() + "  /  STANDBY") : (currentMode.ToString().ToUpperInvariant() + "  /  ACTIVE"));
		_graph.Fmt = ((currentMode == JobMode.Delete) ? new Func<double, string>(Theme.FilesPerSec) : new Func<double, string>(Theme.Rate));
		_graph.MinPeak = ((currentMode == JobMode.Delete) ? 50 : 1048576);
		_readout.Caption = ((currentMode == JobMode.Delete) ? "Deletion rate" : "Throughput");
		_gauge.Invalidate();
		_segBar.Invalidate();
	}

	private void AddSourceFolder()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Choose a source folder",
			UseDescriptionForTitle = true
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
		{
			_fSrc.AppendText(folderBrowserDialog.SelectedPath + Environment.NewLine);
		}
	}

	private void AddSourceFiles()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Multiselect = true,
			Title = "Choose source files"
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			string[] fileNames = openFileDialog.FileNames;
			foreach (string text in fileNames)
			{
				_fSrc.AppendText(text + Environment.NewLine);
			}
		}
	}

	private void BrowseDest()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Choose the destination folder",
			UseDescriptionForTitle = true
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
		{
			_fDst.Text = folderBrowserDialog.SelectedPath;
		}
	}

	private void UpdateVerifyLabel()
	{
		VerifyAlgo algo = _cfg.VerifyAlgoIndex switch
		{
			1 => VerifyAlgo.XxHash128, 
			2 => VerifyAlgo.Sha256, 
			3 => VerifyAlgo.Blake3, 
			_ => VerifyAlgo.XxHash3, 
		};
		_chkVerify.Text = "Verify after copy  (" + VerifyAlgos.Name(algo) + ")";
		_chkVerify.Invalidate();
	}

	private void OpenDiagnostics()
	{
		string ver = typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "1.0";
		using DiagnosticsForm diagnosticsForm = new DiagnosticsForm(() => new DiagnosticsInput("CopyForge " + ver, _lastRec, BuildOptions(), _log.Snapshot(200), CrashLog.Recent(), CrashLog.FilePath));
		diagnosticsForm.ShowDialog(this);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == (Keys.D | Keys.Control))
		{
			OpenDiagnostics();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	private JobOptions BuildOptions()
	{
		JobOptions jobOptions = new JobOptions();
		jobOptions.Sources = _fSrc.Lines.Where((string l) => !string.IsNullOrWhiteSpace(l)).ToList();
		List<string> dests = _fDst.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select((string d) => d.Trim().Trim('"')).Where((string d) => d.Length > 0).ToList();
		jobOptions.Destination = ((dests.Count > 0) ? dests[0] : "");
		jobOptions.ExtraDestinations = dests.Skip(1).ToList();
		jobOptions.Mode = CurrentMode;
		JobOptions jobOptions2 = jobOptions;
		jobOptions2.Overwrite = _segOver.SelectedIndex switch
		{
			1 => OverwriteMode.IfNewer, 
			2 => OverwriteMode.Always, 
			3 => OverwriteMode.Skip, 
			_ => OverwriteMode.IfDifferent, 
		};
		jobOptions.Verify = _chkVerify.Checked && CurrentMode != JobMode.Delete;
		jobOptions.VerifyAlgo = _cfg.VerifyAlgoIndex switch
		{
			1 => VerifyAlgo.XxHash128, 
			2 => VerifyAlgo.Sha256, 
			3 => VerifyAlgo.Blake3, 
			_ => VerifyAlgo.XxHash3, 
		};
		jobOptions.BlockClone = _cfg.BlockClone;
		jobOptions.EjectAfter = _cfg.EjectAfter && CurrentMode != JobMode.Delete;
		jobOptions.DirectWriteRemovable = _cfg.DirectWriteRemovable;
		jobOptions.LimitMBps = _cfg.LimitMBps;
		jobOptions.VerifySkipped = _cfg.VerifySkipped;
		jobOptions.LogPath = (_cfg.WriteLog ? AppSettings.LogPath : "");
		jobOptions.Include = _fInc.Text;
		jobOptions.Exclude = _fExc.Text;
		jobOptions.MinSizeBytes = _stMin.Value * 1024 * 1024;
		jobOptions.MaxSizeBytes = _stMax.Value * 1024 * 1024;
		jobOptions.ModifiedSinceUtc = (_chkSince.Checked ? new DateTime?(DateTime.UtcNow.AddDays(-_stDays.Value)) : null);
		jobOptions.Threads = (int)_stThreads.Value;
		jobOptions.BufferSizeMB = (int)_stBuffer.Value;
		JobOptions jobOptions3 = jobOptions;
		jobOptions3.Io = _segIo.SelectedIndex switch
		{
			1 => IoMode.Buffered, 
			2 => IoMode.Direct, 
			_ => IoMode.Auto, 
		};
		JobOptions jobOptions4 = jobOptions;
		jobOptions4.Method = _cfg.MethodIndex switch
		{
			1 => CopyMethod.Pipeline, 
			2 => CopyMethod.Native, 
			_ => CopyMethod.Auto, 
		};
		jobOptions.ChunkWorkers = _cfg.Chunks;
		jobOptions.Retries = _cfg.Retries;
		jobOptions.ReconnectWaitSeconds = _cfg.WaitSeconds;
		jobOptions.FlushToDisk = _chkFlush.Checked;
		jobOptions.PreserveDirTimes = _chkDirTimes.Checked;
		jobOptions.CopyNtfsExtras = _chkExtras.Checked && CurrentMode != JobMode.Delete;
		jobOptions.SplitOversize = _chkSplit.Checked && CurrentMode != JobMode.Delete;
		jobOptions.RecycleBin = _chkRecycle.Checked && CurrentMode == JobMode.Delete;
		jobOptions.WriteManifest = _cfg.Manifest && CurrentMode != JobMode.Delete;
		jobOptions.ManifestSignKey = ((_cfg.Manifest && _cfg.SignManifest) ? AppSettings.KeyPath : "");
		jobOptions.DeltaUpdate = _cfg.Delta && CurrentMode != JobMode.Delete;
		jobOptions.AutoTune = _cfg.AutoTune && CurrentMode != JobMode.Delete;
		JobOptions jobOptions5 = jobOptions;
		bool flag = _cfg.Vss;
		if (flag)
		{
			JobMode currentMode = CurrentMode;
			bool flag2 = (uint)currentMode <= 2u;
			flag = flag2;
		}
		jobOptions5.UseVss = flag;
		return jobOptions;
	}

	private void LoadSettings()
	{
		_fDst.Text = _cfg.Destination;
		_fInc.Text = _cfg.Include;
		_fExc.Text = _cfg.Exclude;
		_segOver.SelectedIndex = Math.Clamp(_cfg.OverwriteIndex, 0, 3);
		_chkVerify.Checked = _cfg.Verify;
		_stThreads.Value = _cfg.Threads;
		_stBuffer.Value = _cfg.BufferMB;
		_segIo.SelectedIndex = Math.Clamp(_cfg.IoIndex, 0, 2);
		_chkFlush.Checked = _cfg.Flush;
		_chkDirTimes.Checked = _cfg.DirTimes;
		_chkExtras.Checked = _cfg.Extras;
		_chkSplit.Checked = _cfg.Split;
		_chkRecycle.Checked = _cfg.Recycle;
		UpdateVerifyLabel();
	}

	private void SaveSettings()
	{
		_cfg.Destination = _fDst.Text.Trim();
		_cfg.Include = _fInc.Text;
		_cfg.Exclude = _fExc.Text;
		_cfg.OverwriteIndex = _segOver.SelectedIndex;
		_cfg.Verify = _chkVerify.Checked;
		_cfg.Threads = (int)_stThreads.Value;
		_cfg.BufferMB = (int)_stBuffer.Value;
		_cfg.IoIndex = _segIo.SelectedIndex;
		_cfg.Flush = _chkFlush.Checked;
		_cfg.DirTimes = _chkDirTimes.Checked;
		_cfg.Extras = _chkExtras.Checked;
		_cfg.Split = _chkSplit.Checked;
		_cfg.Recycle = _chkRecycle.Checked;
		_cfg.Save();
	}

	public void AddPaths(IEnumerable<string> paths)
	{
		if (_job == null)
		{
			HashSet<string> hashSet = new HashSet<string>(_fSrc.Lines.Select((string l) => l.Trim()), StringComparer.OrdinalIgnoreCase);
			foreach (string path in paths)
			{
				if ((File.Exists(path) || Directory.Exists(path)) && hashSet.Add(path))
				{
					_fSrc.AppendText(path + Environment.NewLine);
				}
			}
		}
		if (base.WindowState == FormWindowState.Minimized)
		{
			base.WindowState = FormWindowState.Normal;
		}
		Activate();
	}

	private async Task<string> PreviewTextAsync(JobOptions o)
	{
		try
		{
			JobOptions po = o.Clone();
			po.DryRun = true;
			po.Verify = false;
			po.WriteManifest = false;
			Cursor = Cursors.WaitCursor;
			SetStatus("CHECKING WHAT WILL CHANGE", Theme.Accent, pulse: true);
			PlanPreview preview = (await Task.Run(() => new CopyJob(po).RunAsync(CancellationToken.None))).Preview;
			if (preview == null)
			{
				return "";
			}
			string text = ((o.Mode == JobMode.Delete) ? $"{preview.DeleteCount:N0} file(s) will be deleted." : ($"{preview.CopyCount:N0} file(s) to {((o.Mode == JobMode.Move) ? "move" : "copy")} ({Theme.Bytes(preview.CopyBytes)}), {preview.SkipCount:N0} unchanged" + ((o.Mode == JobMode.Sync) ? $", {preview.DeleteCount:N0} to DELETE from the destination." : ".")));
			List<string> list = (from x in preview.DeleteList.Take(5)
				select "   - " + x).ToList();
			return "\n\nPREVIEW:  " + text + ((list.Count > 0 && o.Mode == JobMode.Sync) ? ("\nFor example deleted:\n" + string.Join("\n", list) + ((preview.DeleteCount > 5) ? "\n   ..." : "")) : "");
		}
		catch
		{
			return "";
		}
		finally
		{
			Cursor = Cursors.Default;
			SetStatus("READY", Theme.Accent, pulse: false);
		}
	}

	private async Task<bool> ConfirmAsync(JobOptions o)
	{
		string list = string.Join("\n", o.Sources.Take(8)) + ((o.Sources.Count > 8) ? $"\n... and {o.Sources.Count - 8} more" : "");
		if (o.Mode == JobMode.Copy)
		{
			return true;
		}
		string text = await PreviewTextAsync(o);
		return o.Mode switch
		{
			JobMode.Delete => HudDialog.Confirm(this, "CONFIRM DELETE", (o.RecycleBin ? ("Send the following to the Recycle Bin?\n\n" + list) : ("PERMANENTLY DELETE the following?\nThis bypasses the Recycle Bin.\n\n" + list)) + text, !o.RecycleBin, o.RecycleBin ? "RECYCLE" : "DELETE"), 
			JobMode.Sync => HudDialog.Confirm(this, "CONFIRM SYNC", $"Make\n{o.Destination}\nan exact mirror of\n{list}\n\nFiles in the destination that are not in the source will be DELETED." + text, danger: true, "SYNC"), 
			JobMode.Move => HudDialog.Confirm(this, "CONFIRM MOVE", $"Move to {o.Destination}?\n\n{list}\n\nSource files are removed after they are safely copied." + text, danger: false, "MOVE"), 
			_ => true, 
		};
	}

	private JobOptions OfferSplit(JobOptions o)
	{
		if (o.Mode == JobMode.Delete || o.SplitOversize || string.IsNullOrWhiteSpace(o.Destination))
		{
			return o;
		}
		try
		{
			string text = Path.GetPathRoot(Path.GetFullPath(o.Destination)) ?? "";
			if (text.StartsWith("\\\\"))
			{
				return o;
			}
			DriveInfo driveInfo = new DriveInfo(text);
			if (!driveInfo.IsReady || !driveInfo.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
			{
				return o;
			}
			int count = 0;
			string example = null;
			foreach (string source in o.Sources)
			{
				if (File.Exists(source))
				{
					Check(source);
				}
				else
				{
					if (!Directory.Exists(source))
					{
						continue;
					}
					foreach (string item in Directory.EnumerateFiles(source, "*", new EnumerationOptions
					{
						RecurseSubdirectories = true,
						IgnoreInaccessible = true
					}))
					{
						Check(item);
					}
				}
			}
			if (count == 0)
			{
				return o;
			}
			if (!HudDialog.Confirm(this, "FAT32 4 GB LIMIT", $"{count} file(s) are over 4 GB (e.g. {example}) and FAT32 cannot store them.\n\n" + "Split them into .001, .002 ... parts? Join later with:  copy /b name.001+name.002 name\n\nChoose SKIP to copy everything else and leave those files out.", danger: false, "SPLIT THEM", "SKIP THEM"))
			{
				return o;
			}
			_chkSplit.Checked = true;
			return BuildOptions();
			void Check(string f)
			{
				try
				{
					if (new FileInfo(f).Length > uint.MaxValue)
					{
						count++;
						if (example == null)
						{
							example = Path.GetFileName(f);
						}
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
			return o;
		}
	}

	private async Task StartAsync(JobOptions? preset = null)
	{
		if (_job != null)
		{
			return;
		}
		JobOptions opts = preset ?? BuildOptions();
		if (opts.ExtraDestinations.Count > 0 && opts.Mode != JobMode.Copy && opts.Mode != JobMode.Sync)
		{
			HudDialog.Notify(this, "ONE DESTINATION ONLY", "Several destinations (separated by ;) work with COPY and SYNC.\n\nMOVE removes the source after the first destination, and DELETE has no destination.");
			return;
		}
		bool flag = preset == null;
		if (flag)
		{
			flag = !(await ConfirmAsync(opts));
		}
		if (flag)
		{
			return;
		}
		if (preset == null)
		{
			opts = OfferSplit(opts);
		}
		SaveSettings();
		_failedPairs = null;
		_failedOpts = null;
		_log.Clear();
		_graph.Reset();
		_gaugeTarget = 0.0;
		_gauge.Value = 0.0;
		_segBar.Value = 0.0;
		_lastFiles = 0L;
		_lastPollMs = 0L;
		StatTile[] array = new StatTile[6] { _tFiles, _tData, _tSkipped, _tFailed, _tElapsed, _tEta };
		foreach (StatTile obj in array)
		{
			obj.Value = "—";
			obj.ValueColor = Theme.Text;
		}
		_job = new CopyJob(opts);
		_lastRec = new JobRecord(DateTime.Now, opts, null, null);
		_job.Message += delegate(string m)
		{
			string j = m;
			BeginInvoke(delegate
			{
				if (j.StartsWith("Ejecting ", StringComparison.Ordinal))
				{
					SetStatus("EJECTING  -  COPY FINISHED", Theme.Warn, pulse: true);
					_header.Status = "EJECTING DRIVE";
					_bPause.Visible = false;
				}
				_log.Add(j);
			});
		};
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;
		CopyJob job = _job;
		_log.Add((opts.ExplicitPairs != null) ? $"RETRY  -  {opts.Mode.ToString().ToUpperInvariant()}  -  {opts.ExplicitPairs.Count} failed file(s)" : ($"JOB START  -  {opts.Mode.ToString().ToUpperInvariant()}  -  {opts.Sources.Count} source(s)" + (opts.Verify ? "  -  verify ON" : "")), Theme.Accent);
		SetRunning(running: true);
		SetStatus("TRANSFER ACTIVE", Theme.Accent, pulse: true);
		try
		{
			JobResult jobResult = await Task.Run(() => job.RunAsync(token));
			if (opts.ExtraDestinations.Count > 0)
			{
				jobResult = await RunExtraDestinationsAsync(opts, jobResult, token);
			}
			_lastRec = _lastRec with
			{
				Result = jobResult
			};
			if (_cfg.ShutdownAfter && opts.Mode != JobMode.Delete && !jobResult.Cancelled && jobResult.FilesFailed == 0 && !opts.DryRun)
			{
				PowerAction.Run("shutdown", out var powerMsg);
				_log.Add(powerMsg, Theme.Warn);
			}
			IReadOnlyList<FilePair> failedPairs = jobResult.FailedPairs;
			flag = failedPairs != null && failedPairs.Count > 0;
			if (flag)
			{
				JobMode mode = opts.Mode;
				bool flag2 = (uint)mode <= 1u;
				flag = flag2;
			}
			if (flag)
			{
				_failedPairs = jobResult.FailedPairs.ToList();
				_failedOpts = opts;
			}
			Poll();
			ShowResult(opts, jobResult);
			NotifyFinished(jobResult.Cancelled ? "CopyForge - aborted" : ((jobResult.FilesFailed > 0) ? "CopyForge - finished with errors" : "CopyForge - finished"), $"{jobResult.FilesProcessed:N0} files, {Theme.Bytes(jobResult.BytesTransferred)}" + ((jobResult.FilesFailed > 0) ? $", {jobResult.FilesFailed:N0} failed" : ""), jobResult.FilesFailed > 0 || jobResult.Cancelled);
		}
		catch (InvalidOperationException ex)
		{
			_lastRec = _lastRec with
			{
				Problem = ex.Message
			};
			SetStatus("NOT STARTED", Theme.Warn, pulse: false);
			_log.Add("REJECTED: " + ex.Message, Theme.Warn);
			HudDialog.Notify(this, "CANNOT START", ex.Message);
		}
		catch (Exception ex2)
		{
			_lastRec = _lastRec with
			{
				Problem = "FATAL: " + ex2.Message
			};
			CrashLog.Log("Job", ex2);
			SetStatus("FAILED", Theme.Bad, pulse: false);
			_log.Add("FATAL: " + ex2.Message, Theme.Bad);
			HudDialog.Notify(this, "FATAL ERROR", ex2.Message);
		}
		finally
		{
			if (_lastRec != null)
			{
				JobHistory.Append(_lastRec, "gui");
			}
			_ejectPath = _job?.EjectPendingPath;
			_job = null;
			_cts?.Dispose();
			_cts = null;
			SetRunning(running: false);
			ApplyMode();
			HudButton bRetry = _bRetry;
			List<FilePair> failedPairs2 = _failedPairs;
			bRetry.Visible = failedPairs2 != null && failedPairs2.Count > 0;
			if (_bRetry.Visible)
			{
				_bRetry.Text = $"Retry failed ({_failedPairs.Count})";
			}
			if (_ejectPath != null && !_bRetry.Visible)
			{
				_bEject.Text = "Eject " + DriveEject.Root(_ejectPath).TrimEnd('\\');
				_bEject.Visible = true;
			}
		}
	}

	private async Task<JobResult> RunExtraDestinationsAsync(JobOptions opts, JobResult first, CancellationToken token)
	{
		JobResult total = first;
		foreach (string extra in opts.ExtraDestinations)
		{
			if (token.IsCancellationRequested || total.Cancelled)
			{
				break;
			}
			string where = extra;
			BeginInvoke(delegate
			{
				_log.Add("ALSO COPYING TO  -  " + where, Theme.Accent);
			});
			JobOptions o2 = opts.Clone();
			o2.Destination = extra;
			o2.ExtraDestinations = new List<string>();
			o2.ExplicitPairs = null;
			try
			{
				CopyJob j2 = new CopyJob(o2);
				j2.Message += delegate(string m)
				{
					string line = m;
					BeginInvoke(delegate
					{
						_log.Add(line);
					});
				};
				_job = j2;
				JobResult r2 = await Task.Run(() => j2.RunAsync(token));
				total = JobResults.Merge(total, r2);
			}
			catch (Exception ex)
			{
				string why = ex.Message;
				BeginInvoke(delegate
				{
					_log.Add("DESTINATION FAILED  -  " + where + "  -  " + why, Theme.Bad);
				});
				total = JobResults.Merge(total, new JobResult(0L, 0L, 0L, 1L, 0L, TimeSpan.Zero, Cancelled: false, new string[1] { where + ": " + why }));
			}
		}
		return total;
	}

	private static string QueueTitle(JobOptions o)
	{
		object obj;
		if (o.Sources.Count != 0)
		{
			string fileName = Path.GetFileName(o.Sources[0].TrimEnd('\\', '/'));
			obj = ((fileName != null && fileName.Length > 0) ? fileName : o.Sources[0]);
		}
		else
		{
			obj = "?";
		}
		string text = (string)obj;
		if (o.Sources.Count > 1)
		{
			text += $" (+{o.Sources.Count - 1})";
		}
		if (o.Mode != JobMode.Delete)
		{
			return $"{o.Mode.ToString().ToUpperInvariant(),-5}  {text}  ->  {o.Destination}" + (o.Verify ? "  [verify]" : "");
		}
		return "DELETE  " + text;
	}

	private async Task<QueuedJob?> AddToQueueAsync()
	{
		JobOptions o2 = BuildOptions();
		if (o2.Sources.Count == 0 || (o2.Mode != JobMode.Delete && string.IsNullOrWhiteSpace(o2.Destination)))
		{
			HudDialog.Notify(this, "QUEUE", "Choose the source and destination in the main window first.");
			return null;
		}
		if (!(await ConfirmAsync(o2)))
		{
			return null;
		}
		o2 = OfferSplit(o2);
		return new QueuedJob(QueueTitle(o2), o2);
	}

	private async Task OpenQueueAsync()
	{
		if (_job == null)
		{
			bool runRequested;
			using (QueueForm queueForm = new QueueForm(_queue, AddToQueueAsync))
			{
				queueForm.ShowDialog(this);
				runRequested = queueForm.RunRequested;
			}
			if (runRequested)
			{
				await RunQueueAsync();
			}
		}
	}

	private async Task RunQueueAsync()
	{
		_queueRunning = true;
		int done = 0;
		int withErrors = 0;
		int total = _queue.Count;
		string stopped = "";
		try
		{
			while (_queue.Count > 0)
			{
				QueuedJob queuedJob = _queue[0];
				_log.Add($"QUEUE  -  job {done + 1} of {total}: {queuedJob.Title}", Theme.Accent);
				await StartAsync(queuedJob.Options);
				JobResult jobResult = _lastRec?.Result;
				if (jobResult == null)
				{
					stopped = "a job could not start";
					break;
				}
				if (jobResult.Cancelled)
				{
					stopped = "you aborted a job";
					break;
				}
				_queue.RemoveAt(0);
				done++;
				if (jobResult.FilesFailed > 0)
				{
					withErrors++;
				}
			}
		}
		finally
		{
			_queueRunning = false;
		}
		string text = $"{done} of {total} job(s) finished" + ((withErrors > 0) ? $", {withErrors} with failed files (see the event log)." : ".") + ((stopped.Length > 0) ? ("\nQueue stopped because " + stopped + "; the remaining jobs are still queued.") : "");
		_log.Add("QUEUE  -  " + text.Replace("\n", " "), (stopped.Length > 0 || withErrors > 0) ? Theme.Warn : Theme.Ok);
		HudDialog.Notify(this, "QUEUE FINISHED", text, stopped.Length > 0 || withErrors > 0);
	}

	private void ShowResult(JobOptions o, JobResult r)
	{
		string value = o.Mode switch
		{
			JobMode.Move => "moved", 
			JobMode.Delete => "deleted", 
			_ => "copied", 
		};
		long value2 = ((o.Mode == JobMode.Delete) ? r.FilesDeleted : r.FilesProcessed);
		string text = (r.Cancelled ? "ABORTED  -  " : "COMPLETE  -  ") + $"{value2:N0} files {value}" + ((o.Mode == JobMode.Delete) ? "" : (" (" + Theme.Bytes(r.BytesTransferred) + ")")) + ((r.FilesSkipped > 0) ? $", {r.FilesSkipped:N0} skipped" : "") + ((o.Mode == JobMode.Sync && r.FilesDeleted > 0) ? $", {r.FilesDeleted:N0} removed" : "") + ((r.FilesFailed > 0) ? $", {r.FilesFailed:N0} FAILED" : "") + $"  -  {r.Elapsed:hh\\:mm\\:ss\\.f}";
		Color color = (r.Cancelled ? Theme.Warn : ((r.FilesFailed > 0) ? Theme.Bad : Theme.Ok));
		_log.Add(text, color);
		SetStatus(r.Cancelled ? "JOB ABORTED" : ((r.FilesFailed > 0) ? "COMPLETE WITH ERRORS" : "JOB COMPLETE"), color, pulse: false);
		_gauge.Phase = (r.Cancelled ? "ABORTED" : "COMPLETE");
		if (!r.Cancelled)
		{
			_gaugeTarget = 1.0;
			_gauge.Value = 1.0;
			_segBar.Value = 1.0;
		}
		_ticker.Text = "";
		_ticker.Invalidate();
		_gauge.Invalidate();
		_segBar.Invalidate();
		_header.Status = (r.Cancelled ? "JOB ABORTED" : ((r.FilesFailed > 0) ? "DONE - ERRORS" : "JOB COMPLETE"));
		_header.StatusColor = color;
		_header.Blink = false;
		if (r.FilesFailed > 0 && !_queueRunning)
		{
			string obj = $"{r.FilesFailed:N0} file(s) failed.\nSee the event log for details.";
			List<FilePair> failedPairs = _failedPairs;
			HudDialog.Notify(this, "ERRORS", obj + ((failedPairs != null && failedPairs.Count > 0) ? "\n\nUse RETRY FAILED to try just those files again." : ""));
		}
	}

	private void SetRunning(bool running)
	{
		_bStart.Enabled = !running;
		_bCancel.Enabled = running;
		_bPause.Visible = running;
		_bPause.Text = "Pause";
		if (running)
		{
			_bEject.Visible = false;
			_ejectPath = null;
			_bRetry.Visible = false;
		}
		GaugeView gauge = _gauge;
		SegBar segBar = _segBar;
		bool flag2 = (_graph.Running = running);
		bool running2 = (segBar.Running = flag2);
		gauge.Running = running2;
		Control[] array = new Control[20]
		{
			_segMode, _segOver, _chkVerify, _stThreads, _stBuffer, _segIo, _chkFlush, _chkDirTimes, _chkExtras, _chkSplit,
			_chkRecycle, _stMin, _stMax, _chkSince, _bFolder, _bFiles, _bClear, _bBrowse, _bSettings, _bQueue
		};
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Enabled = !running;
		}
		_stDays.Enabled = !running && _chkSince.Checked;
		HudField fSrc = _fSrc;
		HudField fInc = _fInc;
		flag2 = (_fExc.Locked = running);
		running2 = (fInc.Locked = flag2);
		fSrc.Locked = running2;
		_fDst.Locked = running || CurrentMode == JobMode.Delete;
		if (!running)
		{
			ApplyMode();
		}
		else
		{
			_gauge.Phase = "STARTING";
		}
	}

	private void SetStatus(string text, Color c, bool pulse)
	{
		_status.Text = text;
		_status.Color = c;
		_status.Pulse = pulse;
		_status.Invalidate();
		_header.Status = text;
		_header.StatusColor = c;
		_header.Blink = pulse;
	}

	private void Poll()
	{
		CopyJob job = _job;
		if (job != null)
		{
			ProgressSnapshot snapshot = job.GetSnapshot();
			bool flag = CurrentMode == JobMode.Delete;
			_gaugeTarget = snapshot.Fraction;
			_gauge.Phase = snapshot.Phase;
			_segBar.Value = snapshot.Fraction;
			double num2;
			if (flag)
			{
				long elapsedMilliseconds = _clock.ElapsedMilliseconds;
				double num = ((_lastPollMs == 0L) ? 0.25 : Math.Max(0.05, (double)(elapsedMilliseconds - _lastPollMs) / 1000.0));
				num2 = (double)(snapshot.FilesDone - _lastFiles) / num;
				_lastFiles = snapshot.FilesDone;
				_lastPollMs = elapsedMilliseconds;
				_readout.Set(num2.ToString("0"), "files/s");
			}
			else
			{
				num2 = snapshot.BytesPerSecond;
				Theme.RateParts(num2, out string value, out string unit);
				_readout.Set(value, unit);
			}
			_graph.Add(num2);
			_tFiles.Value = $"{snapshot.FilesDone:N0} / {snapshot.FilesTotal:N0}";
			_tData.Value = (flag ? "—" : (Theme.Bytes(snapshot.BytesDone) + " / " + Theme.Bytes(snapshot.BytesTotal)));
			_tSkipped.Value = snapshot.FilesSkipped.ToString("N0");
			_tFailed.Value = snapshot.FilesFailed.ToString("N0");
			_tFailed.ValueColor = ((snapshot.FilesFailed > 0) ? Theme.Bad : Theme.Text);
			_tElapsed.Value = snapshot.Elapsed.ToString("hh\\:mm\\:ss");
			StatTile tEta = _tEta;
			TimeSpan? eta = snapshot.Eta;
			tEta.Value = ((!eta.HasValue) ? "—" : eta.GetValueOrDefault().ToString("hh\\:mm\\:ss"));
			_ticker.Text = ((snapshot.CurrentFile.Length > 0) ? (">  " + snapshot.CurrentFile) : "");
			_ticker.Invalidate();
		}
	}

	private void OnAnimTick()
	{
		long elapsedMilliseconds = _clock.ElapsedMilliseconds;
		if (!_bootDone)
		{
			foreach (var item5 in _boot)
			{
				HudPanel item = item5.Panel;
				int item2 = item5.DelayMs;
				item.Reveal = (float)Ease((double)(elapsedMilliseconds - item2) / 650.0);
			}
			_header.Reveal = (float)Ease((double)elapsedMilliseconds / 500.0);
			_header.Invalidate();
			while (_bootLogIdx < _bootLog.Count && elapsedMilliseconds >= _bootLog[_bootLogIdx].AtMs)
			{
				(int AtMs, string Text, Color? Color) tuple = _bootLog[_bootLogIdx++];
				string item3 = tuple.Text;
				Color? item4 = tuple.Color;
				_log.Add(item3, item4);
			}
			if (elapsedMilliseconds > 1500 && _bootLogIdx >= _bootLog.Count)
			{
				_bootDone = true;
				_header.Reveal = 1f;
				foreach (var item6 in _boot)
				{
					item6.Panel.Reveal = 1f;
				}
				SetStatus("ENGINE IDLE", Theme.Dim, pulse: false);
			}
		}
		if (elapsedMilliseconds - _lastHeaderMs >= 200)
		{
			_lastHeaderMs = elapsedMilliseconds;
			_header.Invalidate();
			if (_status.Pulse)
			{
				_status.Invalidate();
			}
		}
		bool num = _job != null;
		double num2 = _gaugeTarget - _gauge.Value;
		if (Math.Abs(num2) > 0.0005)
		{
			_gauge.Value += num2 * 0.18;
			_gauge.Invalidate();
		}
		if (num)
		{
			_gauge.Invalidate();
			_segBar.Invalidate();
			_graph.Invalidate();
			if (elapsedMilliseconds - _lastPollMsAnim >= 250)
			{
				_lastPollMsAnim = elapsedMilliseconds;
				Poll();
			}
		}
	}

	private static double Ease(double t)
	{
		t = Math.Clamp(t, 0.0, 1.0);
		return 1.0 - Math.Pow(1.0 - t, 3.0);
	}
}
