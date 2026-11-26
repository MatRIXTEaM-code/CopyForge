using System;
using System.Drawing;
using System.Windows.Forms;

namespace CopyForge.App;

internal sealed class HudDialog : Form
{
	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ClassStyle |= 131072;
			return createParams;
		}
	}

	private HudDialog(string title, string tag, string message, Color accent, string yes, string? no)
	{
		string no2 = no;
		HudDialog hudDialog = this;
		base.FormBorderStyle = FormBorderStyle.None;
		base.StartPosition = FormStartPosition.CenterParent;
		base.ShowInTaskbar = false;
		base.KeyPreview = true;
		BackColor = Theme.Bg;
		base.Padding = new Padding(1);
		int num = Math.Min(TextRenderer.MeasureText(message, Theme.FBody, new Size(512, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 4, 340);
		int num2 = 44 + num + 22 + 40 + 18;
		base.ClientSize = new Size(560, num2);
		HudPanel hudPanel = new HudPanel
		{
			Dock = DockStyle.Fill,
			Title = title,
			TagText = tag,
			AccentColor = accent
		};
		base.Controls.Add(hudPanel);
		Label label = new Label
		{
			AutoSize = false,
			BackColor = Theme.Panel,
			ForeColor = Theme.Text,
			Font = Theme.FBody,
			Location = new Point(24, 46),
			Size = new Size(512, num)
		};
		label.Text = message;
		hudPanel.Controls.Add(label);
		HudButton bYes = new HudButton
		{
			Text = yes,
			Accent = accent,
			Size = new Size(150, 34),
			Location = new Point(386, num2 - 18 - 34)
		};
		bYes.Click += delegate
		{
			hudDialog.DialogResult = DialogResult.Yes;
			hudDialog.Close();
		};
		hudPanel.Controls.Add(bYes);
		if (no2 != null)
		{
			HudButton bNo = new HudButton
			{
				Text = no2,
				Size = new Size(120, 34),
				Location = new Point(254, num2 - 18 - 34)
			};
			bNo.Click += delegate
			{
				hudDialog.DialogResult = DialogResult.No;
				hudDialog.Close();
			};
			hudPanel.Controls.Add(bNo);
			base.Shown += delegate
			{
				((accent == Theme.Bad) ? bNo : bYes).Focus();
			};
		}
		else
		{
			base.Shown += delegate
			{
				bYes.Focus();
			};
		}
		base.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				hudDialog.DialogResult = ((no2 != null) ? DialogResult.No : DialogResult.Yes);
				hudDialog.Close();
			}
		};
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
		using Pen pen = new Pen(Theme.Line);
		e.Graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
	}

	public static bool Confirm(IWin32Window owner, string title, string message, bool danger, string yes = "CONFIRM", string no = "CANCEL")
	{
		using HudDialog hudDialog = new HudDialog(title, danger ? "WARNING" : "CONFIRM", message, danger ? Theme.Bad : Theme.Accent, yes, no);
		return hudDialog.ShowDialog(owner) == DialogResult.Yes;
	}

	public static void Notify(IWin32Window owner, string title, string message, bool warn = true)
	{
		using HudDialog hudDialog = new HudDialog(title, warn ? "NOTICE" : "INFO", message, warn ? Theme.Warn : Theme.Accent, "OK", null);
		hudDialog.ShowDialog(owner);
	}
}
