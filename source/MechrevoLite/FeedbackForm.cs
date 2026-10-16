using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;
using MechrevoLite.UI;
using MechrevoLite.Usage;

namespace MechrevoLite;

public sealed class FeedbackForm : RForm
{
	private readonly RTextBox _message;

	private readonly RTextBox _contact;

	private readonly RCheckBox _attach;

	private readonly Label _status;

	private readonly RButton _send;

	private readonly RButton _cancel;

	private CancellationTokenSource? _sending;

	private bool _sent;

	internal static Func<string, string?, bool, CancellationToken, Task<FeedbackResult>>? SendOverride { get; set; }

	internal string StatusText => _status.Text;

	public FeedbackForm()
	{
		Text = Strings.FeedbackTitle;
		StartPosition = FormStartPosition.CenterParent;
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(SettingsForm.CompactDashboardLogicalClientSize.Width, 430);
		MinimumSize = new Size(320, 280);
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		InitTheme(setDPI: true);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 1,
			RowCount = 1,
			Padding = new Padding(24, 16, 24, 12),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(WrappingLabel(Strings.FeedbackLead, 9f, UiVisualStyle.Muted, tableLayoutPanel2), 0, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 6,
			Padding = new Padding(16, 12, 16, 8),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Window
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < 6; i++)
		{
			tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		}
		tableLayoutPanel3.Controls.Add(FieldLabel(Strings.FeedbackMessageLabel), 0, 0);
		_message = new RTextBox
		{
			Name = "textFeedbackMessage",
			Multiline = true,
			AcceptsReturn = true,
			ScrollBars = ScrollBars.Vertical,
			WordWrap = true,
			MaxLength = 4000,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Height = 120,
			Margin = new Padding(0, 4, 0, 8),
			PlaceholderText = Strings.FeedbackMessagePlaceholder,
			AccessibleName = Strings.FeedbackMessageLabel
		};
		tableLayoutPanel3.Controls.Add(_message, 0, 1);
		tableLayoutPanel3.Controls.Add(FieldLabel(Strings.FeedbackContactLabel), 0, 2);
		_contact = new RTextBox
		{
			Name = "textFeedbackContact",
			MaxLength = 120,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right),
			Margin = new Padding(0, 4, 0, 8),
			PlaceholderText = Strings.FeedbackContactPlaceholder,
			AccessibleName = Strings.FeedbackContactLabel
		};
		tableLayoutPanel3.Controls.Add(_contact, 0, 3);
		_attach = new RCheckBox
		{
			Name = "checkFeedbackAttach",
			Text = Strings.FeedbackAttach,
			Checked = true,
			AutoSize = true,
			ForeColor = UiVisualStyle.Text,
			BackColor = UiVisualStyle.Window,
			Margin = new Padding(0, 0, 0, 4),
			AccessibleDescription = Strings.FeedbackAttachTip
		};
		tableLayoutPanel3.Controls.Add(_attach, 0, 4);
		_status = WrappingLabel(Strings.FeedbackAttachTip, 8.5f, UiVisualStyle.Muted, tableLayoutPanel3);
		_status.Name = "labelFeedbackStatus";
		tableLayoutPanel3.Controls.Add(_status, 0, 5);
		panel.Controls.Add(tableLayoutPanel3);
		tableLayoutPanel.Controls.Add(panel, 0, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(16, 8, 16, 8),
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_cancel = CreateButton(Strings.FeedbackCancel);
		_cancel.Name = "buttonFeedbackCancel";
		_cancel.Click += (object? _, EventArgs _) =>
		{
			Close();
		};
		_send = CreateButton(Strings.FeedbackSend);
		_send.Name = "buttonFeedbackSend";
		_send.Click += async (object? _, EventArgs _) =>
		{
			await SendAsync();
		};
		UiVisualStyle.ApplyPrimaryButton(_send);
		tableLayoutPanel4.Controls.Add(_cancel, 0, 0);
		tableLayoutPanel4.Controls.Add(_send, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 2);
		CancelButton = _cancel;
		ActiveControl = _message;
		UiVisualStyle.ApplyWindow(this);
		RTextBox[] array = new RTextBox[2] { _message, _contact };
		foreach (RTextBox obj in array)
		{
			obj.ApplyTheme(darkTheme);
			obj.BackColor = UiVisualStyle.Input;
			obj.ForeColor = UiVisualStyle.Text;
		}
		tableLayoutPanel2.BackColor = UiVisualStyle.Surface;
		tableLayoutPanel4.BackColor = UiVisualStyle.Surface;
		UiVisualStyle.ApplyPrimaryButton(_send);
		ResponsiveLayout.ScaleFrom96(this, this);
		FormClosing += (object? _, FormClosingEventArgs _) =>
		{
			try
			{
				_sending?.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		};
	}

	internal async Task SendAsync()
	{
		if (_sent)
		{
			Close();
			return;
		}
		string text = _message.Text;
		if (!FeedbackUpload.IsSendable(text))
		{
			ShowStatus(Strings.FeedbackTooShort, UiVisualStyle.Danger);
			_message.Focus();
			return;
		}
		SetInputsEnabled(enabled: false);
		ShowStatus(Strings.FeedbackSending, UiVisualStyle.Muted);
		using CancellationTokenSource sending = new CancellationTokenSource();
		_sending = sending;
		FeedbackResult feedbackResult2;
		try
		{
			Func<string, string, bool, CancellationToken, Task<FeedbackResult>> sendOverride = SendOverride;
			FeedbackResult feedbackResult = ((sendOverride == null) ? (await FeedbackUpload.SendAsync(text, _contact.Text, _attach.Checked, sending.Token)) : (await sendOverride(text, _contact.Text, _attach.Checked, sending.Token)));
			feedbackResult2 = feedbackResult;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_sending = null;
		}
		if (!IsDisposed)
		{
			if (feedbackResult2.Ok)
			{
				_sent = true;
				ShowStatus(string.Format(Strings.FeedbackSent, feedbackResult2.Ticket), UiVisualStyle.Ok);
				_send.Text = Strings.FeedbackClose;
				_send.Enabled = true;
				_cancel.Enabled = false;
				Program.toast?.RunToast(string.Format(Strings.FeedbackSent, feedbackResult2.Ticket));
			}
			else
			{
				ShowStatus(string.Format(Strings.FeedbackFailed, feedbackResult2.Error), UiVisualStyle.Danger);
				SetInputsEnabled(enabled: true);
			}
		}
	}

	internal void SetInputsForTests(string message, string? contact, bool attach)
	{
		_message.Text = message;
		_contact.Text = contact ?? "";
		_attach.Checked = attach;
	}

	private void SetInputsEnabled(bool enabled)
	{
		_message.ReadOnly = !enabled;
		_contact.ReadOnly = !enabled;
		_attach.Enabled = enabled;
		_send.Enabled = enabled;
	}

	private void ShowStatus(string text, Color color)
	{
		_status.Text = text;
		_status.ForeColor = color;
	}

	private static Label FieldLabel(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Font = UiVisualStyle.Font(9f, FontStyle.Bold),
			ForeColor = UiVisualStyle.Text,
			Margin = Padding.Empty
		};
	}

	private static Label WrappingLabel(string text, float size, Color color, Control host)
	{
		Label label = new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = (AnchorStyles.Top | AnchorStyles.Left),
			Font = UiVisualStyle.Font(size),
			ForeColor = color,
			Margin = Padding.Empty
		};
		bool reflowing = false;
		host.Layout += (object? _, LayoutEventArgs _) =>
		{
			Reflow();
		};
		host.Resize += (object? _, EventArgs _) =>
		{
			Reflow();
		};
		label.FontChanged += (object? _, EventArgs _) =>
		{
			Reflow();
		};
		return label;
		void Reflow()
		{
			if (!reflowing)
			{
				int num = Math.Max(40, host.ClientSize.Width - host.Padding.Horizontal - label.Margin.Horizontal - 2);
				if (label.MaximumSize.Width != num)
				{
					reflowing = true;
					try
					{
						label.MaximumSize = new Size(num, 0);
					}
					finally
					{
						reflowing = false;
					}
				}
			}
		}
	}

	private static RButton CreateButton(string text)
	{
		return new RButton
		{
			Text = text,
			Dock = DockStyle.Fill,
			Margin = new Padding(4, 0, 4, 0),
			Cursor = Cursors.Hand,
			BorderRadius = 4,
			Secondary = true
		};
	}
}
