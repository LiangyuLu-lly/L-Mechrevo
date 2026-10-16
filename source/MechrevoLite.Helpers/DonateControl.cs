using System;
using System.Drawing;
using System.Windows.Forms;
using MechrevoLite.Properties;
using MechrevoLite.UI;

namespace MechrevoLite.Helpers;

public class DonateControl
{
	private readonly SettingsForm _settings;

	private readonly RBadgeButton _button;

	private CustomContextMenu? _contextMenu;

	private DonateForm? _donateForm;

	public DonateControl(SettingsForm settings, RBadgeButton button)
	{
		_settings = settings;
		_button = button;
	}

	public void Init()
	{
		_button.Click += Button_Click;
		_button.MouseUp += Button_MouseUp;
		if (!AppConfig.Is("donate_dismissed"))
		{
			int num = AppConfig.Get("donate_click");
			int num2 = AppConfig.Get("start_count");
			if (num2 >= ((num < 20) ? 20 : (num + 50)))
			{
				_button.BorderColor = UiVisualStyle.Accent;
				_button.Badge = Math.Clamp((num2 - num) / 50, 1, 5);
			}
		}
	}

	public void ApplyTheme()
	{
		if (_contextMenu != null)
		{
			_contextMenu.BackColor = _settings.BackColor;
			_contextMenu.ForeColor = _settings.ForeColor;
		}
	}

	private void Button_MouseUp(object? sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right)
		{
			if (_contextMenu == null)
			{
				_contextMenu = BuildContextMenu();
			}
			ApplyTheme();
			_contextMenu.Show(_button, new Point(e.X, e.Y));
		}
	}

	private CustomContextMenu BuildContextMenu()
	{
		Padding margin = new Padding(5, 5, 5, 5);
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(Strings.AlreadyDonated)
		{
			Margin = margin
		};
		toolStripMenuItem.Click += (object? s, EventArgs ev) =>
		{
			AppConfig.Set("donate_dismissed", 1);
			SetThankYou();
		};
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(Strings.DontRemind)
		{
			Margin = margin
		};
		toolStripMenuItem2.Click += (object? s, EventArgs ev) =>
		{
			AppConfig.Set("donate_dismissed", 1);
			_button.Badge = 0;
		};
		return new CustomContextMenu
		{
			ShowImageMargin = false,
			Items = 
			{
				(ToolStripItem)toolStripMenuItem,
				(ToolStripItem)toolStripMenuItem2
			},
			Renderer = new CustomMenuRenderer()
		};
	}

	private void Button_Click(object? sender, EventArgs e)
	{
		AppConfig.Set("donate_click", AppConfig.Get("start_count"));
		SetThankYou();
		if (_donateForm == null || _donateForm.IsDisposed)
		{
			_donateForm = new DonateForm();
			_settings.AddOwnedForm(_donateForm);
		}
		if (_donateForm.Visible)
		{
			_donateForm.Close();
		}
		else
		{
			_donateForm.Show();
		}
	}

	private void SetThankYou()
	{
		_button.Badge = 0;
		_button.Text = Strings.ThankYou;
	}
}
