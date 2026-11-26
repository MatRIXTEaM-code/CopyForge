using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopyForge.Engine;

namespace CopyForge.App;

internal sealed class SettingsForm : Form
{
	private readonly AppSettings _cfg;

	private readonly HudPanel _panel = new HudPanel
	{
		Title = "SETTINGS",
		TagText = "ENGINE"
	};

	private readonly HudLabel _lblMethod = new HudLabel
	{
		Text = "Copy method"
	};

	private readonly HudSegmented _segMethod = new HudSegmented
	{
		Items = new string[3] { "AUTO", "PIPELINE", "WINDOWS NATIVE" }
	};

	private readonly HudLabel _lblVerify = new HudLabel
	{
		Text = "Verify checksum (when Verify after copy is on)"
	};

	private readonly HudSegmented _segVerify = new HudSegmented
	{
		Items = new string[4] { "xxHash3 (FAST)", "xxHash128", "SHA-256", "BLAKE3" }
	};

	private readonly HudCheck _chkEject = new HudCheck
	{
		Text = "Safely eject a removable destination when a copy finishes without errors"
	};

	private readonly HudCheck _chkDirect = new HudCheck
	{
		Text = "USB sticks: write small files straight to their final name (faster)"
	};

	private readonly HudCheck _chkClone = new HudCheck
	{
		Text = "ReFS block cloning: instant same-volume copies (falls back to a normal copy)"
	};

	private readonly HudStepper _stChunks = new HudStepper
	{
		Label = "Workers per big file",
		Min = 0L,
		Max = 8L,
		ZeroText = "AUTO"
	};

	private readonly HudStepper _stRetries = new HudStepper
	{
		Label = "Retries per file",
		Min = 0L,
		Max = 10L
	};

	private readonly HudStepper _stWait = new HudStepper
	{
		Label = "Wait for unplugged drive",
		Min = 0L,
		Max = 3600L,
		Step = 10L,
		Suffix = " s"
	};

	private readonly HudStepper _stLimit = new HudStepper
	{
		Label = "Speed limit (0 = none)",
		Min = 0L,
		Max = 10000L,
		Step = 10L,
		Suffix = " MB/s"
	};

	private readonly HudCheck _chkSkipVerify = new HudCheck
	{
		Text = "Verify also compares files skipped as identical, byte for byte (slower)"
	};

	private readonly HudCheck _chkLog = new HudCheck
	{
		Text = "Write a per-file CSV log of every job (in your AppData folder)"
	};

	private readonly HudCheck _chkShutdown = new HudCheck
	{
		Text = "Shut down the PC when a copy finishes without errors (60 s to cancel)"
	};

	private readonly HudCheck _chkVersions = new HudCheck
	{
		Text = "Keep old copies of replaced / sync-deleted files (.cf_versions folder)"
	};

	private readonly HudStepper _stResume = new HudStepper
	{
		Label = "Resume big files from (0 = off)",
		Min = 0L,
		Max = 100000L,
		Step = 64L,
		Suffix = " MB"
	};

	private readonly HudStepper _stParity = new HudStepper
	{
		Label = "Repair data after copy (0 = off)",
		Min = 0L,
		Max = 50L,
		Step = 1L,
		Suffix = " %"
	};

	private readonly HudButton _bRepair = new HudButton
	{
		Text = "Check / repair a folder..."
	};

	private readonly HudCheck _chkPlug = new HudCheck
	{
		Text = "Check a plugged-in drive against its repair data (if it has any)"
	};

	private readonly HudButton _bLocalSend = new HudButton
	{
		Text = "LocalSend..."
	};

	private readonly HudCheck _chkTop = new HudCheck
	{
		Text = "Keep the CopyForge window on top"
	};

	private readonly Label _lblHook = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Dim,
		Font = Theme.FSmall,
		Text = "RUN AFTER EACH JOB  (full path of a .ps1 / .cmd / .bat / .exe; empty = none)"
	};

	private readonly HudField _fHook = new HudField
	{
		Hint = "C:\\scripts\\after-copy.cmd"
	};

	private readonly HudCheck _chkManifest = new HudCheck
	{
		Text = "Write a SHA-256 checksum manifest (cfmanifest.json) after each copy"
	};

	private readonly HudCheck _chkSign = new HudCheck
	{
		Text = "Sign the manifest (key kept in your AppData folder)"
	};

	private readonly HudCheck _chkDelta = new HudCheck
	{
		Text = "Delta update: rewrite only changed blocks of big existing files"
	};

	private readonly HudCheck _chkVss = new HudCheck
	{
		Text = "Copy locked files via Volume Shadow Copy (run as administrator)"
	};

	private readonly HudCheck _chkAuto = new HudCheck
	{
		Text = "Auto-tune: adjust parallel files live (when threads = AUTO)"
	};

	private readonly HudButton _bAudit = new HudButton
	{
		Text = "Audit a folder..."
	};

	private readonly HudCheck _chkShell = new HudCheck
	{
		Text = "Explorer right-click: Add to CopyForge"
	};

	private readonly Label _lblInfo = new Label
	{
		AutoSize = false,
		BackColor = Theme.Panel,
		ForeColor = Theme.Dim,
		Font = Theme.FSmall
	};

	private readonly HudButton _bReset = new HudButton
	{
		Text = "Defaults"
	};

	private readonly HudButton _bClose = new HudButton
	{
		Text = "Close",
		Accent = Theme.Accent
	};

	public bool RequestLocalSend { get; private set; }

	private bool _loading = true;

	public SettingsForm(AppSettings cfg)
	{
		_cfg = cfg;
		Text = "CopyForge - Settings";
		BackColor = Theme.Bg;
		base.Padding = new Padding(10);
		base.StartPosition = FormStartPosition.CenterParent;
		base.ClientSize = new Size(1080, 560);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		base.ShowInTaskbar = false;
		base.KeyPreview = true;
		NativeUi.DarkTitleBar(this);
		_panel.Dock = DockStyle.Fill;
		base.Controls.Add(_panel);
		_panel.Controls.AddRange(_lblMethod, _segMethod, _lblVerify, _segVerify, _chkClone, _chkEject, _chkDirect, _stLimit, _chkSkipVerify, _chkLog, _chkShutdown, _chkVersions, _stResume, _stParity, _bRepair, _bLocalSend, _chkPlug, _chkTop, _lblHook, _fHook, _stChunks, _stRetries, _stWait, _chkManifest, _chkSign, _chkDelta, _chkAuto, _chkVss, _chkShell, _lblInfo, _bReset, _bAudit, _bClose);
		_lblMethod.SetBounds(20, 42, 300, 14);
		_segMethod.SetBounds(20, 60, 500, 28);
		_stChunks.SetBounds(20, 104, 300, 26);
		_stRetries.SetBounds(20, 142, 300, 26);
		_stWait.SetBounds(20, 180, 300, 26);
		_lblVerify.SetBounds(20, 222, 400, 14);
		_segVerify.SetBounds(20, 240, 500, 28);
		_chkManifest.SetBounds(20, 288, 500, 22);
		_chkSign.SetBounds(20, 314, 500, 22);
		_chkDelta.SetBounds(20, 340, 500, 22);
		_chkAuto.SetBounds(20, 366, 500, 22);
		_chkVss.SetBounds(20, 392, 500, 22);
		_chkClone.SetBounds(560, 42, 500, 22);
		_chkEject.SetBounds(560, 68, 500, 22);
		_chkDirect.SetBounds(560, 94, 500, 22);
		_stLimit.SetBounds(560, 124, 300, 26);
		_chkSkipVerify.SetBounds(560, 160, 500, 22);
		_chkLog.SetBounds(560, 186, 500, 22);
		_chkShutdown.SetBounds(560, 212, 500, 22);
		_chkVersions.SetBounds(560, 238, 500, 22);
		_stResume.SetBounds(560, 270, 340, 26);
		_stParity.SetBounds(560, 306, 340, 26);
		_chkTop.SetBounds(560, 342, 500, 22);
		_lblHook.SetBounds(560, 374, 500, 14);
		_fHook.SetBounds(560, 392, 500, 28);
		_chkShell.SetBounds(20, 426, 480, 22);
		_chkPlug.SetBounds(560, 428, 500, 22);
		_lblInfo.SetBounds(20, 456, 1040, 46);
		_lblInfo.Text = "AUTO uses Windows' own copy (instant on ReFS / offload-capable storage) when source and destination are on the same drive, and the CopyForge pipeline otherwise. Block cloning is tried first on same-volume ReFS copies. Workers per big file: AUTO only splits very large files between fast SSDs. Settings file: " + AppSettings.FilePath;
		_bReset.SetBounds(20, 510, 110, 34);
		_bAudit.SetBounds(146, 510, 170, 34);
		_bClose.SetBounds(930, 510, 130, 34);
		_bRepair.SetBounds(332, 510, 220, 34);
		_bLocalSend.SetBounds(560, 510, 150, 34);
		_segMethod.SelectedIndex = Math.Clamp(cfg.MethodIndex, 0, 2);
		_segVerify.SelectedIndex = Math.Clamp(cfg.VerifyAlgoIndex, 0, 3);
		_chkClone.Checked = cfg.BlockClone;
		_chkEject.Checked = cfg.EjectAfter;
		_chkDirect.Checked = cfg.DirectWriteRemovable;
		_stChunks.Value = cfg.Chunks;
		_stRetries.Value = cfg.Retries;
		_stWait.Value = cfg.WaitSeconds;
		_stLimit.Value = cfg.LimitMBps;
		_chkLog.Checked = cfg.WriteLog;
		_chkSkipVerify.Checked = cfg.VerifySkipped;
		_chkShutdown.Checked = cfg.ShutdownAfter;
		_chkShell.Checked = ShellMenu.IsInstalled();
		_chkVersions.Checked = cfg.KeepVersions;
		_stResume.Value = cfg.ResumeMB;
		_stParity.Value = cfg.ParityPct;
		_chkPlug.Checked = cfg.CheckOnPlug;
		_chkTop.Checked = cfg.AlwaysOnTop;
		_fHook.Text = cfg.OnFinish;
		_chkManifest.Checked = cfg.Manifest;
		_chkSign.Checked = cfg.SignManifest;
		_chkDelta.Checked = cfg.Delta;
		_chkAuto.Checked = cfg.AutoTune;
		_chkVss.Checked = cfg.Vss;
		_loading = false;
		_chkManifest.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.Manifest = _chkManifest.Checked;
			}
		};
		_chkSign.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.SignManifest = _chkSign.Checked;
			}
		};
		_chkDelta.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.Delta = _chkDelta.Checked;
			}
		};
		_chkVss.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.Vss = _chkVss.Checked;
			}
		};
		_chkAuto.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.AutoTune = _chkAuto.Checked;
			}
		};
		_bAudit.Click += async delegate
		{
			await AuditAsync();
		};
		_segMethod.SelectedIndexChanged += delegate
		{
			if (!_loading)
			{
				_cfg.MethodIndex = _segMethod.SelectedIndex;
			}
		};
		_stChunks.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.Chunks = (int)_stChunks.Value;
			}
		};
		_stRetries.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.Retries = (int)_stRetries.Value;
			}
		};
		_stWait.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.WaitSeconds = (int)_stWait.Value;
			}
		};
		_chkShell.CheckedChanged += delegate
		{
			if (_loading)
			{
				return;
			}
			try
			{
				if (_chkShell.Checked)
				{
					ShellMenu.Install();
				}
				else
				{
					ShellMenu.Remove();
				}
			}
			catch (Exception ex)
			{
				_loading = true;
				_chkShell.Checked = ShellMenu.IsInstalled();
				_loading = false;
				HudDialog.Notify(this, "EXPLORER MENU", "Could not change the Explorer menu:\n" + ex.Message);
			}
		};
		_bReset.Click += delegate
		{
			_loading = true;
			_segMethod.SelectedIndex = 0;
			_stChunks.Value = 0L;
			_stRetries.Value = 3L;
			_stWait.Value = 120L;
			_cfg.LimitMBps = 0;
			_stLimit.Value = 0L;
			_cfg.WriteLog = false;
			_cfg.VerifySkipped = false;
			_chkSkipVerify.Checked = false;
			_chkLog.Checked = false;
			_cfg.ShutdownAfter = false;
			_chkShutdown.Checked = false;
			_chkVersions.Checked = false;
			_stResume.Value = 256L;
			_stParity.Value = 0L;
			_chkPlug.Checked = false;
			_chkTop.Checked = false;
			_fHook.Text = "";
			HudCheck chkManifest = _chkManifest;
			HudCheck chkSign = _chkSign;
			HudCheck chkDelta = _chkDelta;
			HudCheck chkAuto = _chkAuto;
			bool flag3 = (_chkVss.Checked = false);
			bool flag5 = (chkAuto.Checked = flag3);
			bool flag7 = (chkDelta.Checked = flag5);
			bool @checked = (chkSign.Checked = flag7);
			chkManifest.Checked = @checked;
			_loading = false;
			_cfg.MethodIndex = 0;
			_cfg.VerifyAlgoIndex = 0;
			_cfg.BlockClone = true;
			_cfg.EjectAfter = false;
			_chkEject.Checked = false;
			_cfg.DirectWriteRemovable = true;
			_chkDirect.Checked = true;
			_segVerify.SelectedIndex = 0;
			_chkClone.Checked = true;
			_cfg.Chunks = 0;
			_cfg.Retries = 3;
			_cfg.WaitSeconds = 120;
			AppSettings cfg2 = _cfg;
			AppSettings cfg3 = _cfg;
			AppSettings cfg4 = _cfg;
			AppSettings cfg5 = _cfg;
			flag3 = (_cfg.Vss = false);
			flag5 = (cfg5.AutoTune = flag3);
			flag7 = (cfg4.Delta = flag5);
			@checked = (cfg3.SignManifest = flag7);
			cfg2.Manifest = @checked;
		};
		_segVerify.SelectedIndexChanged += delegate
		{
			if (!_loading)
			{
				_cfg.VerifyAlgoIndex = _segVerify.SelectedIndex;
			}
		};
		_stLimit.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.LimitMBps = (int)_stLimit.Value;
			}
		};
		_chkSkipVerify.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.VerifySkipped = _chkSkipVerify.Checked;
			}
		};
		_chkLog.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.WriteLog = _chkLog.Checked;
			}
		};
		_chkVersions.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.KeepVersions = _chkVersions.Checked;
			}
		};
		_chkPlug.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.CheckOnPlug = _chkPlug.Checked;
			}
		};
		_stParity.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.ParityPct = (int)_stParity.Value;
			}
		};
		_bLocalSend.Click += delegate
		{
			RequestLocalSend = true;
			Close();
		};
		_bRepair.Click += async delegate
		{
			await RepairAsync();
		};
		_stResume.ValueChanged += delegate
		{
			if (!_loading)
			{
				_cfg.ResumeMB = (int)_stResume.Value;
			}
		};
		_chkTop.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.AlwaysOnTop = _chkTop.Checked;
			}
		};
		_fHook.Inner.TextChanged += delegate
		{
			if (!_loading)
			{
				_cfg.OnFinish = _fHook.Text.Trim();
			}
		};
		_chkShutdown.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.ShutdownAfter = _chkShutdown.Checked;
			}
		};
		_chkEject.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.EjectAfter = _chkEject.Checked;
			}
		};
		_chkDirect.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.DirectWriteRemovable = _chkDirect.Checked;
			}
		};
		_chkClone.CheckedChanged += delegate
		{
			if (!_loading)
			{
				_cfg.BlockClone = _chkClone.Checked;
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

	private async Task AuditAsync()
	{
		using FolderBrowserDialog dlg = new FolderBrowserDialog
		{
			Description = "Pick a folder that contains a cfmanifest.json (a copy made with the manifest option on)",
			UseDescriptionForTitle = true
		};
		if (dlg.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		_bAudit.Enabled = false;
		try
		{
			string root = Manifest.FindRoot(dlg.SelectedPath) ?? dlg.SelectedPath;
			AuditResult auditResult = await Task.Run(() => Manifest.AuditAsync(root, Math.Clamp(Environment.ProcessorCount / 2, 2, 8), listUnlisted: true, null, null, CancellationToken.None));
			if (!auditResult.ManifestFound)
			{
				HudDialog.Notify(this, "AUDIT", "No cfmanifest.json found in that folder.\nCopy with the manifest option on first.");
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			if (!string.Equals(root.TrimEnd('\\'), dlg.SelectedPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
			{
				stringBuilder.AppendLine("Manifest found in: " + root);
			}
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(40, 4, stringBuilder2);
			handler.AppendLiteral("Checked ");
			handler.AppendFormatted(auditResult.Checked, "N0");
			handler.AppendLiteral(" files: ");
			handler.AppendFormatted(auditResult.Ok, "N0");
			handler.AppendLiteral(" OK, ");
			handler.AppendFormatted(auditResult.Changed.Count, "N0");
			handler.AppendLiteral(" changed, ");
			handler.AppendFormatted(auditResult.Missing.Count, "N0");
			handler.AppendLiteral(" missing.");
			stringBuilder2.AppendLine(ref handler);
			stringBuilder.AppendLine("Signature: " + auditResult.Signature + ((auditResult.KeyFingerprint != null) ? (" (key id " + auditResult.KeyFingerprint + ")") : ""));
			foreach (string item in auditResult.Changed.Take(8))
			{
				stringBuilder.AppendLine("CHANGED  " + item);
			}
			foreach (string item2 in auditResult.Missing.Take(8))
			{
				stringBuilder.AppendLine("MISSING  " + item2);
			}
			if (auditResult.Changed.Count + auditResult.Missing.Count > 16)
			{
				stringBuilder.AppendLine("... (run 'cfcopy audit' for the full list)");
			}
			HudDialog.Notify(this, auditResult.Clean ? "AUDIT: CLEAN" : "AUDIT: PROBLEMS FOUND", stringBuilder.ToString(), !auditResult.Clean);
		}
		catch (Exception ex)
		{
			HudDialog.Notify(this, "AUDIT", "Audit failed: " + ex.Message);
		}
		finally
		{
			_bAudit.Enabled = true;
		}
	}

	private async Task RepairAsync()
	{
		using FolderBrowserDialog dlg = new FolderBrowserDialog
		{
			Description = "Pick a folder that has repair data (a .cf_parity folder inside it)",
			UseDescriptionForTitle = true
		};
		if (dlg.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		string root = dlg.SelectedPath;
		_bRepair.Enabled = false;
		try
		{
			ParityResult check = await Task.Run(() => Parity.Repair(root, checkOnly: true, null, CancellationToken.None));
			if (check.Files == check.NoParity)
			{
				HudDialog.Notify(this, "REPAIR DATA", "No repair data found in that folder.\nCopy with 'Repair data after copy' on, or run 'cfcopy protect'.");
				return;
			}
			string summary = $"Checked {check.Files:N0} files: {check.Damaged:N0} damaged, {check.NoParity:N0} without repair data.";
			if (check.Damaged == 0)
			{
				HudDialog.Notify(this, "REPAIR DATA: ALL GOOD", summary, warn: false);
				return;
			}
			string list = string.Join("\n", check.Problems.Take(8));
			if (!HudDialog.Confirm(this, "DAMAGED FILES FOUND", summary + "\n\nRebuild the damaged files from the repair data now?", danger: false, "REPAIR", "NOT NOW"))
			{
				return;
			}
			ParityResult fix = await Task.Run(() => Parity.Repair(root, checkOnly: false, null, CancellationToken.None));
			string text = $"{fix.Repaired:N0} repaired, {fix.Unrepairable:N0} could not be repaired.";
			if (fix.Problems.Count > 0)
			{
				text += "\n\n" + string.Join("\n", fix.Problems.Take(8));
			}
			HudDialog.Notify(this, (fix.Unrepairable == 0) ? "REPAIR DONE" : "REPAIR: PROBLEMS", text, fix.Unrepairable > 0);
		}
		catch (Exception ex)
		{
			HudDialog.Notify(this, "REPAIR DATA", "Failed: " + ex.Message);
		}
		finally
		{
			_bRepair.Enabled = true;
		}
	}
}
