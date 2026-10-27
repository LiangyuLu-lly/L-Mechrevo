using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using MechrevoLite;
using MechrevoLite.Gpu;
using MechrevoLite.Properties;
using MechrevoLite.Usage;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// P0-4：首次使用引导的 WPF 版 —— 逐项等价迁移自 WinForms
/// <c>MechrevoLite.FirstRunGuideForm</c>（source/MechrevoLite/FirstRunGuideForm.cs）。
///
/// <para>⭐ 迁移目标：解除 <see cref="OriginalFormHostWindow"/> 对旧 WinForms 窗口的托管依赖（审计 P0-4），
/// 引导本身的**步骤 / 配置键 / 完成动作**必须与旧版逐项一致：</para>
/// <list type="bullet">
/// <item>完整引导（首次启动）：<c>onboarding_version &lt; 1</c> 时显示；
/// 步骤 1/2/3（警告）/ 可选步骤 4（核显模式三选一）/ 可选步骤 5（匿名统计开关）；</item>
/// <item>单问模式：升级用户补问「核显模式」一题（<c>igpuOnlyStepOnly: true</c>，紧凑窗口、短正文、按钮「确定」）；</item>
/// <item>配置键：<c>igpu_only_ui</c>（由 <see cref="IgpuOnlyUiMode" />.Set 写入三态）、
/// <c>igpu_only_asked=1</c>、<c>IgpuOnlyAttempt.RememberAnswer/RememberUnknownAnswer</c>、
/// <c>usage_telemetry</c>（仅当用户真的改了才写，走 <see cref="UsageTelemetry" />.SetEnabled）；</item>
/// <item>关闭（含 X / Esc / 两个按钮）统一在 <see cref="Window.Closed" /> 里落盘；
/// <see cref="Program" />.UiAuditMode 下**一律不写配置**；宝龙达/硬件未知时步骤 4 整步不建且**绝不落盘**。</item>
/// </list>
///
/// <para>⚠️ 不变项（与旧版一致，改前改后行为不变）：</para>
/// <list type="number">
/// <item>文本框内容全部来自 <see cref="Strings" />（同一套中英资源，不硬编码）；</item>
/// <item>步骤 4 默认选中「不确定」（fail-safe：保持显示，交给硬件判据）；</item>
/// <item>步骤 5 仅在 <c>ui_show_privacy_feedback=1</c> 时出现（机主要求暂时隐藏）；</item>
/// <item>单问模式不显示步骤 1/2/3/5，也不写 <c>usage_telemetry</c>。</item>
/// </list>
/// </summary>
public partial class FirstRunGuideWindow : Window
{
    /// <summary>步骤 4 的三态取值（与旧版 FirstRunGuideForm 的字符串常量逐字一致）。</summary>
    private const string ChoiceYes = "yes";
    private const string ChoiceNo = "no";
    private const string ChoiceUnknown = "unknown";

    /// <summary>2026-10-22（机主）：匿名统计说明步骤暂时隐藏；配置 ui_show_privacy_feedback=1 可恢复。</summary>
    private static bool ShowTelemetryGuideStep => AppConfig.Get("ui_show_privacy_feedback", 0) == 1;

    private readonly bool _igpuOnlyStepOnly;

    /// <summary>
    /// 完整引导里**跳过步骤 4**（「你的电脑有『集显模式』这一档吗?」）。
    /// 旧版注释（等价迁移）：宝龙达没有核显模式；硬件未知时也不问。三处联动缺一不可：
    /// ① 不建步骤 4 控件；② <see cref="PersistIgpuOnlyChoice" /> 直接 return（不写 igpu_only_ui/igpu_only_asked）；
    /// ③ 判据只对完整引导生效（单问模式在宝龙达/未知硬件下不会被调用）。
    /// </summary>
    private readonly bool _skipIgpuOnlyStep;

    private string _igpuOnlyChoice = ChoiceUnknown;

    private ToggleButton? _igpuOnlyYesButton;
    private ToggleButton? _igpuOnlyNoButton;
    private ToggleButton? _igpuOnlyUnknownButton;

    // 2026-10-09（telemetryguide）：步骤 5 的勾选框。与设置页开关读写同一个 usage_telemetry 键，
    // 初始值取自同一个读取点 UsageTelemetry.Enabled；只有"用户确实改过"才落盘（X/Esc/稍后都不写）。
    private CheckBox? _telemetryCheck;
    private bool _telemetryInitial;

    /// <summary>防止 Closed 被重复处理（WPF 正常只触发一次，这里仍显式幂等）。</summary>
    private bool _persisted;

    public FirstRunGuideWindow()
        : this(igpuOnlyStepOnly: false)
    {
    }

    /// <param name="igpuOnlyStepOnly">
    /// true = 只显示「核显模式」这一个问题（用于升级用户：onboarding_version 已是 1，
    /// 看不到完整首次引导，但仍需问一次）。
    /// </param>
    public FirstRunGuideWindow(bool igpuOnlyStepOnly)
    {
        InitializeComponent();
        _igpuOnlyStepOnly = igpuOnlyStepOnly;
        // 与旧版同一判据：完整引导里，宝龙达（已知无核显模式）或硬件未知 ⇒ 跳过步骤 4。
        bool bdKnown = L_Mechrevo.WpfP0.Services.WpfBackend.Hw?.UsesBaolongda ?? false;
        bool bdUnknown = L_Mechrevo.WpfP0.Services.WpfBackend.Hw == null;
        _skipIgpuOnlyStep = !igpuOnlyStepOnly && (bdKnown || bdUnknown);
        Title = igpuOnlyStepOnly ? Strings.IgpuOnlyGuideTitle : Strings.FirstRun;
        BuildGuide();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Closed += (_, _) => PersistAll();
    }

    // =====================================================================================
    // 界面搭建（等价于旧版构造函数里的 TableLayoutPanel / CreateStep / CreateButton）
    // =====================================================================================
    private void BuildGuide()
    {
        if (_igpuOnlyStepOnly)
        {
            // 单问模式：窗口标题已是「核显模式显示设置」⇒ 去掉重复的大标题，只留一行极小辅助说明；
            // 高度按内容自适应（等价旧版 ApplyCompactIgpuOnlyLayout 的"刚好放得下"）。
            HeaderTitle.Visibility = Visibility.Collapsed;
            HeaderIntro.Text = Strings.IgpuOnlyGuideIntro;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            MinHeight = 0;
            MaxHeight = 460;
            BodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AddStep("?", Strings.FirstRunStep4Title, Strings.FirstRunStep4BodyShort,
                warning: false, CreateIgpuOnlyChoiceRow(compact: true));
            OkButton.Content = Strings.Ok;
        }
        else
        {
            HeaderTitle.Text = Strings.FirstRunWelcome;
            HeaderIntro.Text = Strings.FirstRunIntro;
            AddStep("1", Strings.FirstRunStep1Title, Strings.FirstRunStep1Body);
            AddStep("2", Strings.FirstRunStep2Title, Strings.FirstRunStep2Body);
            AddStep("!", Strings.FirstRunStep3Title, Strings.FirstRunStep3Body, warning: true);
            // 第 4 步：宝龙达/硬件未知 ⇒ 整步不建（与旧版 _skipIgpuOnlyStep 一致）。
            if (!_skipIgpuOnlyStep)
            {
                AddStep("?", Strings.FirstRunStep4Title, Strings.FirstRunStep4Body,
                    warning: false, CreateIgpuOnlyChoiceRow(compact: false));
            }
            // 第 5 步：匿名统计说明（默认隐藏，ui_show_privacy_feedback=1 才出现；只加在完整引导里）。
            if (ShowTelemetryGuideStep)
            {
                AddStep("5", Strings.FirstRunStep5Title, Strings.FirstRunStep5Body,
                    warning: false, CreateTelemetryChoiceRow());
            }
            OkButton.Content = Strings.GoToSystemPage;
        }

        LaterButton.Content = Strings.Later;
        LaterButton.IsCancel = true;   // 与旧版 CancelButton 等价：Esc = 稍后（仍会在 Closed 里落盘已有选择）
        OkButton.IsDefault = true;     // 与旧版 AcceptButton 等价：Enter = 主按钮
        AutomationProperties.SetName(LaterButton, Strings.Later);
        AutomationProperties.SetName(OkButton, OkButton.Content?.ToString() ?? string.Empty);
        ClampToWorkArea();
    }

    /// <summary>
    /// ⚠️ 高 DPI / 小屏保护：本窗口的尺寸单位是 DIP（1/96 英寸）；1366×768@150% 时可用工作区只有约
    /// 512 DIP 高，若直接用 600 DIP 会超出屏幕、底部按钮看不见。这里按 <see cref="SystemParameters" />.WorkArea
    /// 先夹住（内容超出时由 <c>BodyScroll</c> 滚动兜底）；单问模式仍保持"高度贴合内容"。
    /// </summary>
    private void ClampToWorkArea()
    {
        double maxWidth = Math.Max(360, SystemParameters.WorkArea.Width - 32);
        double maxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 32);
        MaxWidth = maxWidth;
        MaxHeight = _igpuOnlyStepOnly ? Math.Min(460, maxHeight) : maxHeight;
        if (Width > maxWidth)
        {
            Width = maxWidth;
        }
        if (!_igpuOnlyStepOnly)
        {
            Height = Math.Min(Height, maxHeight);
            MinWidth = Math.Min(MinWidth, maxWidth);
            MinHeight = Math.Min(MinHeight, maxHeight);
        }
    }

    /// <summary>
    /// 一个步骤块（等价旧版 <c>CreateStep</c>）：左侧 28×28 圆角标记（普通=强调色、警告=红色系）+ 右侧标题/正文/附加控件。
    /// </summary>
    private void AddStep(string marker, string title, string body, bool warning = false, FrameworkElement? extra = null)
    {
        Grid row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border chip = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 10, 0),
            Background = warning ? Brush("FirstRunWarnBgBrush") : Brush("AccentBrush"),
            Child = new TextBlock
            {
                Text = marker,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = warning ? Brush("FirstRunWarnBrush") : Brushes.White
            }
        };
        Grid.SetColumn(chip, 0);

        StackPanel content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = warning ? Brush("FirstRunWarnBrush") : Brush("TextBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("MutedBrush")
        });
        if (extra != null)
        {
            content.Children.Add(extra);
        }
        Grid.SetColumn(content, 1);

        row.Children.Add(chip);
        row.Children.Add(content);
        StepsPanel.Children.Add(row);
    }

    /// <summary>步骤 4 的「有 / 没有 / 不确定」三选一（等价旧版分段按钮单选组；默认「不确定」= 保持显示）。</summary>
    private FrameworkElement CreateIgpuOnlyChoiceRow(bool compact)
    {
        Grid row = new Grid
        {
            Margin = new Thickness(0, compact ? 8 : 10, 0, 0)
        };
        for (int i = 0; i < 3; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        _igpuOnlyYesButton = CreateChoiceButton(Strings.FirstRunStep4Yes, ChoiceYes, 0);
        _igpuOnlyNoButton = CreateChoiceButton(Strings.FirstRunStep4No, ChoiceNo, 1);
        _igpuOnlyUnknownButton = CreateChoiceButton(Strings.FirstRunStep4Unknown, ChoiceUnknown, 2);
        // 默认选中「不确定」= 保持显示（fail-safe，与旧版一致）
        SetIgpuOnlyChoice(ChoiceUnknown);
        if (_igpuOnlyYesButton != null) row.Children.Add(_igpuOnlyYesButton);
        if (_igpuOnlyNoButton != null) row.Children.Add(_igpuOnlyNoButton);
        if (_igpuOnlyUnknownButton != null) row.Children.Add(_igpuOnlyUnknownButton);
        return row;
    }

    private ToggleButton CreateChoiceButton(string text, string choice, int column)
    {
        ToggleButton button = new ToggleButton
        {
            Content = text,
            Style = FindStyle("FirstRunSegButton"),
            Margin = new Thickness(0, 0, column == 2 ? 0 : 8, 0),
            Tag = choice
        };
        AutomationProperties.SetName(button, text);
        // ⚠️ 用 Click 而不是 Checked：ToggleButton 点已选中项会先取消勾选，Click 里再由 SetIgpuOnlyChoice 统一定稿。
        button.Click += (_, _) => SetIgpuOnlyChoice(choice);
        Grid.SetColumn(button, column);
        return button;
    }

    private void SetIgpuOnlyChoice(string choice)
    {
        _igpuOnlyChoice = choice;
        if (_igpuOnlyYesButton != null)
        {
            _igpuOnlyYesButton.IsChecked = string.Equals(choice, ChoiceYes, StringComparison.Ordinal);
        }
        if (_igpuOnlyNoButton != null)
        {
            _igpuOnlyNoButton.IsChecked = string.Equals(choice, ChoiceNo, StringComparison.Ordinal);
        }
        if (_igpuOnlyUnknownButton != null)
        {
            _igpuOnlyUnknownButton.IsChecked = string.Equals(choice, ChoiceUnknown, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 步骤 5 的「匿名使用统计与匿名日志」勾选行（等价旧版 CreateTelemetryChoiceRow）：
    /// 初始勾选态取自 UsageTelemetry.Enabled（= AppConfig["usage_telemetry"]，缺省 1）。
    /// </summary>
    private FrameworkElement CreateTelemetryChoiceRow()
    {
        _telemetryInitial = UsageTelemetry.Enabled;
        _telemetryCheck = new CheckBox
        {
            Content = Strings.UsageTelemetryToggle,
            Style = FindStyle("SwitchBox"),
            IsChecked = _telemetryInitial,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = Strings.UsageTelemetryTip
        };
        AutomationProperties.SetName(_telemetryCheck, Strings.UsageTelemetryToggle);
        AutomationProperties.SetHelpText(_telemetryCheck, Strings.UsageTelemetryTip);
        return _telemetryCheck;
    }

    // =====================================================================================
    // 关闭与落盘（等价旧版 FormClosed 里的 PersistIgpuOnlyChoice + PersistTelemetryChoice）
    // =====================================================================================
    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PersistAll()
    {
        if (_persisted)
        {
            return;
        }
        _persisted = true;
        PersistIgpuOnlyChoice();
        PersistTelemetryChoice();
    }

    private void PersistIgpuOnlyChoice()
    {
        // ⭐ 宝龙达/硬件未知：步骤 4 整步没建 ⇒ 这一问不存在 ⇒ **绝不落盘**
        //   （写 igpu_only_asked=1 会让用户以后换到同方机器时永远不再被问 = 隐性数据损坏）。
        if (_skipIgpuOnlyStep)
        {
            return;
        }
        // 审计/截图模式（UiAuditRunner 也会创建窗口）不得写配置。
        if (L_Mechrevo.WpfP0.Services.WpfBackend.UiAuditMode)
        {
            return;
        }
        // 三态取值必须走 IgpuOnlyUiMode.Set（它会同时置迁移标记，否则刚写的 show 会被当成老值按 auto 解读）。
        string ui = string.Equals(_igpuOnlyChoice, ChoiceNo, StringComparison.Ordinal)
            ? IgpuOnlyUiMode.Hide
            : (string.Equals(_igpuOnlyChoice, ChoiceYes, StringComparison.Ordinal) ? IgpuOnlyUiMode.Show : IgpuOnlyUiMode.Auto);
        IgpuOnlyUiMode.Set(ui);
        AppConfig.Set("igpu_only_asked", 1);
        if (string.Equals(_igpuOnlyChoice, ChoiceYes, StringComparison.Ordinal))
        {
            IgpuOnlyAttempt.RememberAnswer(show: true);
        }
        else if (string.Equals(_igpuOnlyChoice, ChoiceNo, StringComparison.Ordinal))
        {
            IgpuOnlyAttempt.RememberAnswer(show: false);
        }
        else
        {
            IgpuOnlyAttempt.RememberUnknownAnswer();
        }
        AppConfig.Flush();
        Logger.WriteLine($"iGPU-only UI guide answer={_igpuOnlyChoice} igpu_only_ui={ui} igpu_only_asked=1");
    }

    private void PersistTelemetryChoice()
    {
        // 单问模式没有这个控件；审计/截图模式不写；用户没动过（含 X / Esc / 稍后）一个字节都不写。
        if (L_Mechrevo.WpfP0.Services.WpfBackend.UiAuditMode || _telemetryCheck == null)
        {
            return;
        }
        if (_telemetryCheck.IsChecked == _telemetryInitial)
        {
            return;
        }
        UsageTelemetry.SetEnabled(_telemetryCheck.IsChecked == true);
        AppConfig.Flush();
        Logger.WriteLine($"First-run guide usage telemetry choice: usage_telemetry={(_telemetryCheck.IsChecked == true ? 1 : 0)} (same key as the Settings switch)");
    }

    // =====================================================================================
    // 主题辅助
    // =====================================================================================
    private Brush Brush(string key)
    {
        return FindResource(key) as Brush ?? Brushes.Transparent;
    }

    private Style? FindStyle(string key)
    {
        return FindResource(key) as Style;
    }

    /// <summary>深色标题栏（与项目里其它 WPF 窗口同一手法）。</summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            int enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
