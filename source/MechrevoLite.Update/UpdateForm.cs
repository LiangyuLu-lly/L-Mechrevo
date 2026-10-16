using System;
using MechrevoLite.Properties;using System.Diagnostics;
using MechrevoLite.Properties;using System.Drawing;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.UI;
using MechrevoLite.Properties;
namespace MechrevoLite.Update;

internal sealed class UpdateForm : RForm
{
	private readonly Label _headline = new Label();

	private readonly Label _detail = new Label();

	private readonly RTextBox _notes = new RTextBox();

	private readonly Label _notesEmpty = new Label();

	private readonly Label _status = new Label();

	private readonly ProgressBar _progress = new ProgressBar();

	private readonly RButton _install = new RButton();

	private readonly RButton _downloadPage = new RButton();

	private readonly RButton _later = new RButton();

	private readonly RButton _feedback = new RButton();

	private TableLayoutPanel _root;

	private int _designClientWidth;

	private int _designClientHeight;

	private UpdateInfo? _info;

	private bool _busy;

	internal UpdateForm(bool autoCheck)
	{
		UpdateForm updateForm = this;
		Initialize();
		Shown += async (object? _, EventArgs _) =>
		{
			await updateForm.RefreshAsync(autoCheck);
		};
	}

	internal UpdateForm(UpdateInfo info)
	{
		Initialize();
		Apply(info, autoCheck: false);
	}

	private void Initialize()
	{
		BackColor = UiVisualStyle.Window;
		ForeColor = UiVisualStyle.Text;
		Text = Strings.CheckUpdates;
		// 2026-10-12 r6（用户原话「还有检查更新 设置等界面左上角图标 能不能同步按钮的图标」）：
		// 标题栏左上角 = 底栏「更新」按钮那枚字形（Kind.Update，⛔ 不另画、⛔ 不改按钮）。
		UiVisualStyle.ApplyWindowGlyph(this, UiGlyph.Kind.Update);
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		MinimizeBox = false;
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(520, 380);
		InitTheme(setDPI: true);
		TableLayoutPanel tableLayoutPanel = (_root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			Padding = new Padding(20, 16, 20, 16),
			BackColor = UiVisualStyle.Surface
		});
		Controls.Add(tableLayoutPanel);
		_headline.Text = Strings.UpdateChecking;
		_headline.Font = UiVisualStyle.Font(12f, FontStyle.Bold);
		_headline.ForeColor = UiVisualStyle.Text;
		_headline.AutoSize = true;
		_headline.Margin = new Padding(0, 0, 0, 6);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_headline, 0, 0);
		_detail.Text = "";
		_detail.AutoSize = true;
		_detail.Margin = new Padding(0, 0, 0, 8);
		UiVisualStyle.ApplyMuted(_detail);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_detail, 0, 1);
		_notes.Multiline = true;
		_notes.ReadOnly = true;
		_notes.ScrollBars = ScrollBars.Vertical;
		_notes.BackColor = UiVisualStyle.Input;
		_notes.ForeColor = UiVisualStyle.Text;
		_notes.Font = UiVisualStyle.Font(9f);
		_notes.Dock = DockStyle.Fill;
		_notes.Margin = new Padding(0, 0, 0, 8);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(_notes, 0, 2);
		_notesEmpty.Text = Strings.UpdateNoNotes;
		_notesEmpty.ForeColor = UiVisualStyle.Muted;
		_notesEmpty.AutoSize = true;
		_notesEmpty.Margin = new Padding(0, 4, 0, 4);
		_notesEmpty.Visible = false;
		tableLayoutPanel.Controls.Add(_notesEmpty, 0, 2);
		_status.Text = "";
		_status.AutoSize = true;
		_status.Margin = new Padding(0, 0, 0, 8);
		UiVisualStyle.ApplyMuted(_status);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_status, 0, 3);
		_progress.Dock = DockStyle.Fill;
		_progress.Height = 6;
		_progress.Style = ProgressBarStyle.Continuous;
		_progress.Visible = false;
		_progress.Margin = new Padding(0, 0, 0, 8);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_progress, 0, 4);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty,
			BackColor = UiVisualStyle.Surface
		};
		_install.Text = Strings.UpdateDownloadInstall;
		_install.AutoSize = false;
		_install.Size = new Size(96, 32);
		_install.Margin = new Padding(0, 0, 8, 0);
		_install.Cursor = Cursors.Hand;
		_install.Click += async (object? _, EventArgs _) =>
		{
			await StartInstallAsync();
		};
		_downloadPage.Text = Strings.UpdateOpenDownloadPage;
		_downloadPage.AutoSize = false;
		_downloadPage.Size = new Size(96, 32);
		_downloadPage.Margin = new Padding(0, 0, 8, 0);
		_downloadPage.Cursor = Cursors.Hand;
		_downloadPage.Click += (object? _, EventArgs _) =>
		{
			OpenDownloadPage();
		};
		_later.Text = Strings.Later;
		_later.AutoSize = false;
		_later.Size = new Size(88, 32);
		_later.Margin = new Padding(0, 0, 8, 0);
		_later.Cursor = Cursors.Hand;
		_later.Click += (object? _, EventArgs _) =>
		{
			Close();
		};
		_feedback.Text = Strings.UpdateFeedback;
		_feedback.AutoSize = false;
		_feedback.Size = new Size(88, 32);
		_feedback.Margin = Padding.Empty;
		_feedback.Cursor = Cursors.Hand;
		_feedback.Click += (object? _, EventArgs _) =>
		{
			ShowFeedback();
		};
		flowLayoutPanel.Controls.Add(_install);
		flowLayoutPanel.Controls.Add(_downloadPage);
		flowLayoutPanel.Controls.Add(_later);
		flowLayoutPanel.Controls.Add(_feedback);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 5);
		UiVisualStyle.ApplyWindow(this);
		UiVisualStyle.ApplySection(tableLayoutPanel);
		UiVisualStyle.ApplyTitle(_headline, 12f);
		ResponsiveLayout.ScaleFrom96(this, this);
		_designClientWidth = ClientSize.Width;
		_designClientHeight = ClientSize.Height;
		CreateControl();
		ApplyNotesState();
	}

	protected override void OnShown(EventArgs e)
	{
		ApplyNotesState();
		base.OnShown(e);
		if (IsHandleCreated && !IsDisposed)
		{
			ResponsiveLayout.PerformLayoutTree(this);
			Size preferredSize = _root.GetPreferredSize(new Size(_root.Width, 0));
			int height = ((!string.IsNullOrWhiteSpace(_notes.Text)) ? _designClientHeight : Math.Min(_designClientHeight, preferredSize.Height));
			ClientSize = new Size(_designClientWidth, height);
		}
	}

	private void ApplyNotesState()
	{
		bool flag = !string.IsNullOrWhiteSpace(_notes.Text);
		_notes.Visible = flag;
		_notesEmpty.Visible = !flag;
		if (_root.RowStyles.Count > 2)
		{
			_root.RowStyles[2] = (flag ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
		}
		if (IsHandleCreated && !IsDisposed)
		{
			ResponsiveLayout.PerformLayoutTree(this);
			int height = _root.GetPreferredSize(new Size(_root.Width, 0)).Height;
			int height2 = (flag ? _designClientHeight : Math.Min(_designClientHeight, height));
			ClientSize = new Size(_designClientWidth, height2);
		}
	}

	private async Task RefreshAsync(bool autoCheck)
	{
		try
		{
			UpdateInfo updateInfo = UpdateChecker.Cached;
			if ((object)updateInfo == null)
			{
				updateInfo = await UpdateChecker.CheckAsync(!autoCheck);
			}
			UpdateInfo info = updateInfo;
			Apply(info, autoCheck);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("更新窗口刷新失败：" + ex.Message);
			Apply(null, autoCheck);
		}
	}

	private void Apply(UpdateInfo? info, bool autoCheck)
	{
		_info = info;
		if ((object)info == null)
		{
			_headline.Text = Strings.UpdateFailed;
			_detail.Text = Strings.UpdateNetworkError;
			_notes.Text = "";
			ApplyNotesState();
			_status.Text = "";
			_install.Enabled = false;
			_downloadPage.Enabled = false;
			return;
		}
		_install.Enabled = false;
		_downloadPage.Enabled = !string.IsNullOrWhiteSpace(info.DownloadPage);
		_notes.Text = info.Notes ?? "";
		ApplyNotesState();
		if (!info.UpdateAvailable)
		{
			_headline.Text = Strings.UpdateAlreadyLatest;
			_detail.Text = string.Format(Strings.UpdateVersionInfo, info.CurrentVersion, string.IsNullOrWhiteSpace(info.LatestVersion) ? "" : string.Format(Strings.UpdateLatestSuffix, info.LatestVersion));
			_status.Text = "";
			return;
		}
		_headline.Text = string.Format(Strings.UpdateFoundNew, info.LatestVersion);
		_detail.Text = string.Format(Strings.UpdateVersionInfo, info.CurrentVersion, string.IsNullOrWhiteSpace(info.ReleaseDate) ? "" : string.Format(Strings.UpdateReleaseSuffix, info.ReleaseDate));
		if (string.IsNullOrWhiteSpace(info.DownloadUrl))
		{
			_status.Text = Strings.UpdateNetdiskManual;
			_status.ForeColor = UiVisualStyle.Warn;
			_install.Enabled = false;
			return;
		}
		if (!UpdatePolicy.TryAcceptOffer(info, out string reason))
		{
			_install.Enabled = false;
			_status.Text = reason;
			_status.ForeColor = UiVisualStyle.Warn;
			Logger.WriteLine("更新窗口：自动安装被拒 —— " + reason);
			return;
		}
		bool flag = UpdateInstaller.LooksLikeInstallerPackage(info);
		if (!flag && !UpdateInstaller.CanSelfInstall(out string reason2))
		{
			_install.Enabled = false;
			_status.Text = reason2;
			_status.ForeColor = UiVisualStyle.Warn;
		}
		else
		{
			_install.Enabled = true;
			_status.Text = (flag ? Strings.UpdateSha256Verify : Strings.UpdateSha256Auto);
			_status.ForeColor = UiVisualStyle.Ok;
		}
	}

	private async Task StartInstallAsync()
	{
		if (_busy)
		{
			return;
		}
		UpdateInfo info = _info;
		if ((object)info == null || !info.UpdateAvailable)
		{
			return;
		}
		if (!UpdatePolicy.TryAcceptOffer(info, out string reason))
		{
			_status.Text = reason;
			_status.ForeColor = UiVisualStyle.Warn;
			Logger.WriteLine("更新窗口：安装被拒 —— " + reason);
			return;
		}
		bool installer = UpdateInstaller.LooksLikeInstallerPackage(info);
		if (!installer && !UpdateInstaller.CanSelfInstall(out string reason2))
		{
			MessageBox.Show(reason2 + "\n\n" + Strings.UpdateWillOpenDownloadPage, Strings.UpdateCannotAutoInstall, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			OpenDownloadPage();
		}
		else
		{
			if (MessageBox.Show(installer ? Strings.UpdateConfirmInstaller : Strings.UpdateConfirmSelf, Strings.UpdateInstallTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}
			SetBusy(busy: true);
			try
			{
				Progress<int> progress = new Progress<int>((int p) =>
				{
					_progress.Value = Math.Clamp(p, 0, 100);
				});
				DownloadResult downloadResult = await UpdateInstaller.DownloadAsync(info, progress);
				string path = downloadResult.Path;
				if (path == null)
				{
					_status.Text = downloadResult.Reason ?? Strings.UpdateDownloadFailedManual;
					_status.ForeColor = UiVisualStyle.Danger;
					return;
				}
				PackageVerification packageVerification = UpdateInstaller.Verify(path, info);
				if (!packageVerification.Ok)
				{
					UpdateInstaller.DiscardPackage(path);
					_status.Text = string.Format(Strings.UpdateVerifyFailed, packageVerification.Reason);
					_status.ForeColor = UiVisualStyle.Danger;
					Logger.WriteLine("更新包校验失败：" + packageVerification.Reason);
					return;
				}
				if (installer || UpdateInstaller.IsPeExecutable(path))
				{
					if (!UpdateInstaller.StartInstaller(path))
					{
						_status.Text = Strings.UpdateCannotLaunchInstaller;
						_status.ForeColor = UiVisualStyle.Danger;
						return;
					}
					_status.Text = Strings.UpdateInstallerLaunched;
					_status.ForeColor = UiVisualStyle.Ok;
					Logger.WriteLine($"更新安装包已启动：{path}（{packageVerification.Reason}）");
				}
				else
				{
					string text = UpdateInstaller.ExtractPackage(path, info);
					if (text == null)
					{
						_status.Text = Strings.UpdateExtractFailed;
						_status.ForeColor = UiVisualStyle.Danger;
						return;
					}
					string currentExePath = UpdateInstaller.CurrentExePath;
					if (!UpdateInstaller.StartUpdater(text, currentExePath))
					{
						_status.Text = Strings.UpdateCannotLaunchUpdater;
						_status.ForeColor = UiVisualStyle.Danger;
						return;
					}
					_status.Text = Strings.UpdateUpdaterLaunched;
					_status.ForeColor = UiVisualStyle.Ok;
					Logger.WriteLine($"更新安装流程已启动：{text} -> {currentExePath}（{packageVerification.Reason}）");
				}
				await Task.Delay(600);
				Program.RequestShutdownForUpdate();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("更新安装流程失败：" + ex.Message);
				_status.Text = string.Format(Strings.UpdateInstallFailed, ex.Message);
				_status.ForeColor = UiVisualStyle.Danger;
			}
			finally
			{
				SetBusy(busy: false);
			}
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_progress.Visible = busy;
		_progress.Value = 0;
		_install.Enabled = !busy;
		_downloadPage.Enabled = !busy && !string.IsNullOrWhiteSpace(_info?.DownloadPage);
		_later.Enabled = !busy;
		_feedback.Enabled = !busy;
	}

	private void OpenDownloadPage()
	{
		string text = _info?.DownloadPage;
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Logger.WriteLine("打开下载页失败：" + ex.Message);
		}
	}

	private static void ShowFeedback()
	{
		SettingsForm.ShowTestFeedback();
	}
}
