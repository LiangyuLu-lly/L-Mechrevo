using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MechrevoLite.Helpers;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// V 线 · V1：顶层窗口背景模式（纯色 / 云母 Mica）。
/// <para>默认 <see cref="BackdropMode.Solid"/>（= 现状逐像素不变）；只有配置 <c>ui_backdrop = mica</c> 且
/// Windows 11 22H2+ 才会尝试 Mica，任何一步失败都会把窗口恢复原样并回退成纯色。</para>
/// </summary>
internal enum BackdropMode
{
    Solid = 0,
    Mica = 1
}

/// <summary>
/// V 线 · V1/V2：把 <c>DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW)</c> 接到 WPF 窗口上。
/// <para>⚠️ 关键点（Mica 不生效的头号原因）：<see cref="WindowChrome.GlassFrameThickness"/> 必须是 <c>-1</c>
/// （把玻璃frame扩展到整个客户区），XAML 里现在写的是 <c>0</c>；同时窗口 <see cref="Window.Background"/>
/// 必须是「带 alpha 的底色」——V5 起是 <c>WindowBgBrush 主题基色 + ui_mica_tint 算出的 alpha</c>
/// （tint=0 时 alpha=0，等价于原来的全透明），否则 WPF 会把一整块不透明画布盖在 DWM 背景上面。</para>
/// <para>⚠️ <see cref="WindowChrome"/> 可能是被多个窗口共享的实例：这里一律 <c>Clone()</c> 后再
/// <see cref="WindowChrome.SetWindowChrome"/>，绝不原地改 XAML 里那个实例（否则会影响同实例的其它窗口，
/// 回退时也没法保证「原样」）。</para>
/// </summary>
internal static class WindowBackdrop
{
    /// <summary>配置键：<c>solid</c>（缺省）/ <c>mica</c>。读写都走 <see cref="MechrevoLite.AppConfig" />。</summary>
    internal const string ConfigKey = "ui_backdrop";

    /// <summary>
    /// V 线 · V5：云母强度配置键，<b>0–100</b>。
    /// <para>语义：<c>0</c> = 窗口底色完全透明（DWM 的 Mica 最强）、<c>100</c> = 完全不透明（云母看不见）。
    /// 缺省 <see cref="DefaultTint" />（55）：深色下留 45% 的云母透过率，观感和 Win11 原生设置页接近。</para>
    /// <para>⚠️ 这个键只在 Mica 真的生效时起作用：<see cref="Mode" /> 不是 Mica / 套用失败时窗口背景走回退逻辑，
    /// 强度既不读也不写（关闭 Mica 后必须逐像素回到原来的 <c>WindowBgBrush</c>）。</para>
    /// </summary>
    internal const string TintConfigKey = "ui_mica_tint";

    /// <summary>云母强度缺省值（0–100）。</summary>
    internal const int DefaultTint = 100;

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE（Win11 22H2 / build 22621 起）。</summary>
    private const int DwmwaSystemBackdropType = 38;

    /// <summary>DWMSBT_NONE。</summary>
    private const int DwmsbtNone = 1;

    /// <summary>DWMSBT_MAINWINDOW = Mica。</summary>
    private const int DwmsbtMainWindow = 2;

    /// <summary>22H2 = 22621 起才有 DWMWA_SYSTEMBACKDROP_TYPE；更低版本一律回退。</summary>
    private const int MinimumMicaBuild = 22621;

    /// <summary>
    /// 每个成功套用 Mica 的窗口的「原样」快照，用于关闭 Mica / 失败回退时逐项还原。
    /// 用 <see cref="ConditionalWeakTable{TKey,TValue}"/>：窗口关掉就自动回收，不会拖住 UI 对象。
    /// </summary>
    private sealed class AppliedState
    {
        /// <summary>改背景前 <see cref="DependencyObject.ReadLocalValue" /> 拿到的本地值（通常是
        /// <c>{DynamicResource WindowBgBrush}</c> 的资源引用表达式，而不是解析后的画刷），优先还它。</summary>
        internal object? OriginalLocalBackground;

        /// <summary>改背景前解析出来的画刷（资源引用还原失败时的兜底）。</summary>
        internal Brush? OriginalBackground;

        /// <summary>XAML 里那个 <see cref="WindowChrome" /> 原实例（<c>GlassFrameThickness = 0</c>）。</summary>
        internal WindowChrome? OriginalChrome;

        /// <summary>套用底色那一刻 <c>WindowBgBrush</c> 的颜色（深/浅主题各一套）——用来发现「主题换了、底色该重算」。</summary>
        internal Color BaseColor;

        /// <summary>是否已经排了一次「Idle 时对色」的刷新，避免连续点击排出一串。</summary>
        internal bool TintRefreshQueued;
    }

    private static readonly ConditionalWeakTable<Window, AppliedState> Applied = new();

    /// <summary>当前配置模式；缺省/无法识别 = <see cref="BackdropMode.Solid"/>。</summary>
    internal static BackdropMode Mode
    {
        get
        {
            string? raw = MechrevoLite.AppConfig.GetString(ConfigKey);
            // 缺省 = 云母；用户显式选过 solid 时仍尊重配置。
            if (string.IsNullOrWhiteSpace(raw))
            {
                return BackdropMode.Mica;
            }
            return string.Equals(raw.Trim(), "mica", StringComparison.OrdinalIgnoreCase)
                ? BackdropMode.Mica
                : BackdropMode.Solid;
        }
    }

    /// <summary>这个窗口当前是否真的套上了 Mica（不是「配置想要 Mica」）。V2 隐藏背景图只看这个。</summary>
    internal static bool IsActive(Window? window)
    {
        return window is not null && Applied.TryGetValue(window, out _);
    }

    /// <summary>临时关闭某个窗口的云母（大窗口自定义背景图优先时使用），不影响全局配置。</summary>
    internal static void Disable(Window? window)
    {
        try
        {
            if (window is not null && Applied.TryGetValue(window, out AppliedState? state) && state is not null)
            {
                Restore(window, state, Environment.OSVersion.Version.Build);
            }
        }
        catch
        {
        }
    }

    /// <summary>云母强度（0–100，缺省 <see cref="DefaultTint" />=100）：100 = 云母最强、0 = 完全不透明。</summary>
    internal static int Tint
    {
        get => Math.Clamp(MechrevoLite.AppConfig.Get(TintConfigKey, DefaultTint), 0, 100);
    }

    /// <summary>
    /// Mica 生效时窗口 <see cref="Window.Background" /> = 「主题基色 + 按强度算出的 alpha」。
    /// <para>基色直接取主题资源 <c>WindowBgBrush</c>（<see cref="AppTheme" /> 深/浅各维护一份），
    /// 所以深浅主题的云母底不会串色；<c>ui_card_opacity</c> 动的是 CardBrush，不影响这里。</para>
    /// <para>alpha = tint / 100：tint 0 ⇒ 全透明（窗口底色不挡，云母最强，等同改动前的
    /// <c>Brushes.Transparent</c>），tint 100 ⇒ 全不透明（DWM 的 Mica 被整块盖住，看不见）。</para>
    /// </summary>
    private static SolidColorBrush CreateTintBrush()
    {
        Color baseColor = ResolveBaseColor();
        // 机主口径：ui_mica_tint 是云母强度。
        // 100 = 云母最强（窗口底色全透明），0 = 完全不透明。
        byte alpha = (byte)Math.Round((100 - Tint) * 255.0 / 100.0);
        SolidColorBrush brush = new(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        // 冻结：这个画刷不参与动画，冻住可以让每次重绘直接复用，省掉一层拷贝。
        brush.Freeze();
        return brush;
    }

    /// <summary>主题基色：优先 <c>WindowBgBrush</c>（AppTheme 运行时换过），拿不到再用深色兜底。</summary>
    private static Color ResolveBaseColor()
    {
        try
        {
            if (Application.Current?.TryFindResource("WindowBgBrush") is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }
        catch
        {
        }
        return Color.FromRgb(0x0B, 0x0D, 0x10);
    }

    /// <summary>把一个已套 Mica 的窗口的底色重算成当前强度/主题，并记下这次的基色快照。</summary>
    private static void ApplyTintBrush(Window window, AppliedState state)
    {
        window.Background = CreateTintBrush();
        state.BaseColor = ResolveBaseColor();
    }

    /// <summary>
    /// 让<b>所有</b>已套 Mica 的窗口按当前 <c>ui_mica_tint</c> 重新上色（设置页滑块拖动时即时调用）。
    /// 幂等、只碰「真的用上了 Mica」的窗口；纯色窗口一个像素都不动。
    /// </summary>
    internal static void RefreshTintForOpenWindows()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (Applied.TryGetValue(window, out AppliedState? state) && state is not null)
                {
                    ApplyTintBrush(window, state);
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 按当前配置给一个顶层窗口套背景模式。<b>幂等</b>：可以反复调用（启动、设置页切换、窗口重开）。
    /// </summary>
    /// <returns>调用之后该窗口是否处于 Mica 生效状态。</returns>
    internal static bool Apply(Window? window)
    {
        if (window is null)
        {
            return false;
        }
        int build = Environment.OSVersion.Version.Build;
        try
        {
            bool applied = Applied.TryGetValue(window, out AppliedState? state);

            if (Mode != BackdropMode.Mica)
            {
                if (applied && state is not null)
                {
                    Restore(window, state, build);
                }
                else
                {
                    Log(BackdropMode.Solid, build, 0, "config=solid");
                }
                return false;
            }

            if (applied)
            {
                // 已经套上了：配置/主题/强度可能变了 ⇒ 只重算底色（不重复写 DWM 属性、不动 WindowChrome）。
                if (state is not null)
                {
                    ApplyTintBrush(window, state);
                }
                return true;
            }

            if (build < MinimumMicaBuild)
            {
                Log(BackdropMode.Solid, build, 0, "build<22621");
                return false;
            }

            // 没有 WindowChrome 的窗口（普通 WindowStyle=SingleBorderWindow 的对话框等）没法把玻璃扩展到客户区，
            // 硬套 Mica 只会得到一个「标题栏有色、客户区不透明」的怪窗口 ⇒ 直接回退。
            WindowChrome? originalChrome = WindowChrome.GetWindowChrome(window);
            if (originalChrome is null)
            {
                Log(BackdropMode.Solid, build, 0, "no-windowchrome");
                return false;
            }

            nint hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == nint.Zero)
            {
                Log(BackdropMode.Solid, build, unchecked((int)0x80070006), "no-hwnd");
                return false;
            }

            object? originalLocalBackground = window.ReadLocalValue(Window.BackgroundProperty);
            Brush? originalBackground = window.Background;
            int hr = 0;
            bool ok = false;
            try
            {
                WindowChrome chrome = (WindowChrome)originalChrome.Clone();
                chrome.GlassFrameThickness = new Thickness(-1);
                WindowChrome.SetWindowChrome(window, chrome);
                // V 线 · V5：不再是 Brushes.Transparent，而是「主题基色 × 云母强度」的半透明底色。
                // 强度 0 时 alpha = 0，与改动前完全等价（Mica 最强）。
                window.Background = CreateTintBrush();
                int backdrop = DwmsbtMainWindow;
                hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
                ok = hr == 0;
            }
            catch (Exception ex)
            {
                hr = ex.HResult;
                ok = false;
            }

            if (!ok)
            {
                RestoreBackgroundAndChrome(window, originalChrome, originalLocalBackground, originalBackground);
                Log(BackdropMode.Solid, build, hr, "dwm-or-windowchrome-failed");
                return false;
            }

            Applied.Add(window, new AppliedState
            {
                OriginalLocalBackground = originalLocalBackground,
                OriginalBackground = originalBackground,
                OriginalChrome = originalChrome,
                BaseColor = ResolveBaseColor()
            });
            // 主题切换只会换掉 WindowBgBrush 实例，已经写进 Background 的普通画刷不会跟着变 ⇒
            // 在窗口的下一次交互后（Idle）对一次基色，变了就重建（事件驱动，不轮询、不碰 AppTheme）。
            window.PreviewMouseDown -= OnWindowPreviewMouseDown;
            window.PreviewMouseDown += OnWindowPreviewMouseDown;
            Log(BackdropMode.Mica, build, hr, null, Tint);
            return true;
        }
        catch (Exception ex)
        {
            // 兜底：任何意外都只能回退成纯色，绝不让背景模式把窗口/启动搞崩。
            try
            {
                if (Applied.TryGetValue(window, out AppliedState? broken) && broken is not null)
                {
                    RestoreBackgroundAndChrome(window, broken.OriginalChrome, broken.OriginalLocalBackground, broken.OriginalBackground);
                    Applied.Remove(window);
                }
            }
            catch
            {
            }
            Log(BackdropMode.Solid, build, ex.HResult, "exception:" + ex.GetType().Name);
            return false;
        }
    }

    /// <summary>把窗口恢复到「Mica 之前」：DWM 背景类型改回 NONE → 还原 WindowChrome → 还原背景画刷。</summary>
    private static void Restore(Window window, AppliedState state, int build)
    {
        int hr = 0;
        try
        {
            nint hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != nint.Zero)
            {
                int none = DwmsbtNone;
                hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
            }
            RestoreBackgroundAndChrome(window, state.OriginalChrome, state.OriginalLocalBackground, state.OriginalBackground);
            window.InvalidateVisual();
        }
        catch (Exception ex)
        {
            hr = ex.HResult;
        }
        Applied.Remove(window);
        Log(BackdropMode.Solid, build, hr, "restored");
    }

    /// <summary>还原 WindowChrome（原实例，GlassFrameThickness 仍是 XAML 里的 0）与窗口背景。</summary>
    private static void RestoreBackgroundAndChrome(
        Window window,
        WindowChrome? originalChrome,
        object? originalLocalBackground,
        Brush? originalBackground)
    {
        if (originalChrome is not null)
        {
            WindowChrome.SetWindowChrome(window, originalChrome);
        }
        if (originalLocalBackground is null || ReferenceEquals(originalLocalBackground, DependencyProperty.UnsetValue))
        {
            window.ClearValue(Window.BackgroundProperty);
            return;
        }
        try
        {
            // DynamicResource 的本地值是一个资源引用表达式：还回去以后换主题仍会跟着变（「原样」的关键）。
            window.SetValue(Window.BackgroundProperty, originalLocalBackground);
        }
        catch
        {
            // 表达式还原不了（理论上不会走到）：退回快照画刷，至少视觉和原来一致。
            window.Background = originalBackground;
        }
    }

    /// <summary>
    /// 已套 Mica 的窗口被点了一下：排一次 Idle 对色。按钮的 Click 在 MouseUp 里才触发，
    /// Idle 这一拍跑在它之后 —— 所以「在这一下点击里换了主题」也能当帧跟上，不用去改 AppTheme / 各窗口的主题按钮。
    /// 平时只是一次颜色比较，几乎零成本；不轮询、不 Sleep。
    /// </summary>
    private static void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }
        if (!Applied.TryGetValue(window, out AppliedState? state) || state is null || state.TintRefreshQueued)
        {
            return;
        }
        state.TintRefreshQueued = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            state.TintRefreshQueued = false;
            try
            {
                // 排队期间用户可能刚好关了云母（Restore 已经把 Background 还原成「原本地值」）——
                // 这时状态已被移出 Applied，绝不能再往上写底色。
                if (!Applied.TryGetValue(window, out AppliedState? current) || !ReferenceEquals(current, state))
                {
                    return;
                }
                if (ResolveBaseColor() != state.BaseColor)
                {
                    ApplyTintBrush(window, state);
                }
            }
            catch
            {
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>固定格式的一行日志：<c>[ui] backdrop=mica|solid build=... hr=...</c>（可选 tint / reason）。</summary>
    private static void Log(BackdropMode mode, int build, int hr, string? reason, int tint = -1)
    {
        try
        {
            string text = "[ui] backdrop=" + (mode == BackdropMode.Mica ? "mica" : "solid")
                + " build=" + build
                + " hr=" + hr;
            if (tint >= 0)
            {
                text += " tint=" + tint;
            }
            if (!string.IsNullOrEmpty(reason))
            {
                text += " reason=" + reason;
            }
            Logger.WriteLine(text);
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

}
