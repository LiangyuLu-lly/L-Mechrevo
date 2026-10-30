using System.Runtime.CompilerServices;
// ⭐ task-12：为 MechrevoLite.Core 提供**旧 WinForms UI 的兼容 shim**
// 目的：业务程序集先能 0 错误编译，为 WPF 改引用 Core 铺路。
// 约束：本文件只允许 UI 通知/交互/入口门面的 shim；真实业务逻辑（SMU、模式键、偏移范围、灯效颜色数）
//       保持与旧实现相同，不得改成空壳。
using System;
using System.Reflection;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using MechrevoLite.Gpu;
using PawnIO;

[assembly: InternalsVisibleTo("L-Mechrevo.WpfP0")]
[assembly: InternalsVisibleTo("L-Mechrevo")]
[assembly: InternalsVisibleTo("mro_monitor")]

namespace MechrevoLite
{
    /// <summary>
    /// 旧 WinForms 入口 <c>Program</c> 的 Core 兼容门面。
    /// 只保留业务程序集实际引用的静态状态/安全动作；UI 动作一律 no-op（由未来 UI 层实现）。
    /// ⚠️ 该类同时解决 Probe.dll 全局 internal Program 造成的 CS0122 绑定歧义。
    /// </summary>
    internal static class Program
    {
        internal enum PowerSource
        {
            Battery,
            Barrel,
            USBC
        }

        internal static AsusACPI? acpi;
        internal static MechrevoHw? hw;
        internal static MechrevoService? service;
        internal static KeyboardRgb? rgb;
        internal static SettingsForm? settingsForm;
        internal static Mode.ModeControl? modeControl;
        internal static Helpers.ToastForm? toast;

        internal static bool UiAuditMode { get; set; }
        internal static bool IsLightingTemporarilySuspended { get; set; }
        internal static bool LightingIdleSuspended { get; set; }
        internal static void NotifyLightingUserIntent() { }
        internal static void MarkKeyboardCustomStatusBaseline() { }

        private static PowerSource? _lastObservedSource;

        internal static PowerSource currentSource
        {
            get => _lastObservedSource ?? PowerSource.Barrel;
            set => _lastObservedSource = value;
        }

        private static string? _releaseVersion;
        private static string? _releaseLabel;

        internal static string ReleaseVersion => _releaseVersion ??= ReadInformational("Version");
        internal static string ReleaseLabel => _releaseLabel ??= ReadInformational("Label");

        /// <summary>与旧实现同一判据：按电源线状态生成短键（0=离电 / 1=在线）。</summary>
        internal static int PerformanceKey()
        {
            try
            {
                return (int)System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
            }
            catch
            {
                return 0;
            }
        }

        // ── 以下均为旧 UI 入口动作：Core 里安全 no-op（未来由 UI 层注册实现）──
        internal static void StopVendorLightingIdleMonitorForBaolongda() { }
        internal static void ScheduleGpuOcShutdownGuard() { }
        internal static void ScheduleIdleBackendWarmup() { }
        internal static void RequestShutdownForUpdate() { }
        internal static bool SetAutoModes(bool powerChanged = false, bool init = false, bool wakeup = false) => true;
        internal static Task<(bool AllIssued, bool Attempted, string IssuedSummary)> SetExternalLightingPowerAsync(
            bool on, bool restoreEffect, bool useTemporarySnapshot, int? restoreRequestId = null)
            => Task.FromResult((true, false, "core-shim-noop"));
        internal static Task<bool> RestoreKeyboardLightingAsync(bool force = false) => Task.FromResult(false);
        internal static Task SuspendLightingTemporarilyAsync() => Task.CompletedTask;
        internal static bool SyncKbPowerFromHardwareIfDiverged() => false;

        private static string ReadInformational(string which)
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
                string? value = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return "0.0.0";
                }
                if (which == "Label")
                {
                    return value;
                }
                return value.Split('+')[0];
            }
            catch
            {
                return "0.0.0";
            }
        }
    }

    /// <summary>
    /// 旧 <c>SettingsForm</c> 的 Core shim：业务只通过它做 UI 通知（Visualise*/Invoke），
    /// 这里不创建控件；方法仅执行委托/记录，保证语义“通知到 UI”，不承载业务。
    /// </summary>
    internal sealed class SettingsForm : System.Windows.Forms.IWin32Window
    {
        internal bool IsDisposed => false;
        internal bool InvokeRequired => false;
        internal bool IsHandleCreated => false;
        public IntPtr Handle => IntPtr.Zero;

        internal object? Invoke(Action action)
        {
            action();
            return null;
        }

        internal object? BeginInvoke(Action action)
        {
            action();
            return null;
        }

        internal void VisualiseBattery(int limit) { }
        internal void VisualiseBatteryUnknown() { }
        internal void VisualiseBatteryFull() { }
        internal void VisualiseScreen(bool screenEnabled, bool screenAuto, int frequency, int maxFrequency,
            int overdrive, bool overdriveSetting, bool hdr, bool acm, int fhd, int hdrControl) { }
    }

    /// <summary>旧 LightForm 的纯业务静态表：灯效颜色数（与旧实现逐字一致）。</summary>
    internal static class LightForm
    {
        internal static int LightEffectColorMode(string effect)
        {
            if (effect == "Single" || effect == "Raindrop")
            {
                return 1;
            }
            if (effect == "Breathing" || effect == "Impact" || effect == "ColorMarquee")
            {
                return 7;
            }
            return 0;
        }
    }

    /// <summary>旧 CustomModeForm 的纯业务范围计算（与旧实现一致；无 UI 依赖）。</summary>
    internal static class CustomModeForm
    {
        internal const int VramOffsetNegativeFloor = -1000;

        internal static (int CoreMin, int CoreMax, int MemMin, int MemMax) GpuOffsetUiRange(MechrevoHw? hw)
        {
            int coreMin = -900;
            int coreMax = 2000;
            int memMin = VramOffsetNegativeFloor;
            int memMax = 2000;
            if (NvapiPStates.TryGetP0OffsetRange(out int nvCoreMin, out int nvCoreMax, out int nvMemMin, out int nvMemMax))
            {
                coreMin = Math.Max(nvCoreMin, -900);
                coreMax = Math.Min(nvCoreMax, 2000);
                memMin = Math.Max(nvMemMin, VramOffsetNegativeFloor);
                memMax = Math.Min(nvMemMax, 2000);
            }
            else
            {
                int vendorCoreMin = hw?.GpuCoreOffsetUserMinimum ?? (-1);
                int vendorCoreMax = hw?.GpuCoreOffsetUserMaximum ?? (-1);
                int vendorMemMin = hw?.GpuMemoryOffsetUserMinimum ?? (-1);
                int vendorMemMax = hw?.GpuMemoryOffsetUserMaximum ?? (-1);
                if (vendorCoreMax > vendorCoreMin)
                {
                    coreMin = Math.Max(vendorCoreMin, -900);
                    coreMax = Math.Min(vendorCoreMax, 2000);
                }
                if (vendorMemMax > vendorMemMin)
                {
                    memMin = Math.Max(vendorMemMin, VramOffsetNegativeFloor);
                    memMax = Math.Min(vendorMemMax, 2000);
                }
            }
            if (coreMax <= coreMin)
            {
                coreMin = -900;
                coreMax = 2000;
            }
            if (memMax <= memMin)
            {
                memMin = VramOffsetNegativeFloor;
                memMax = 2000;
            }
            return (coreMin, coreMax, memMin, memMax);
        }
    }

    namespace Helpers
    {
        /// <summary>旧 Toast 的 Core shim：只做通知，不创建任何窗体。</summary>
        internal class ToastForm
        {
            internal static void ShowFailure(string message)
            {
                Logger.WriteLine("[toast][failure] " + message);
                MechrevoLite.Helpers.ToastBridge.ShowFailure(message);
            }

            internal static void ShowNotice(string message)
            {
                Logger.WriteLine("[toast][notice] " + message);
                MechrevoLite.Helpers.ToastBridge.ShowNotice(message);
            }
        }
    }

    namespace Mode
    {
        /// <summary>
        /// 旧 ModeControl 的 Core shim：仅保留业务实际调用的**纯逻辑**（SMU 单例、模式键同步）；
        /// UI 相关（SetModeLabel/设置窗协调）不进入 Core。
        /// </summary>
        internal class ModeControl
        {
            private static readonly object _smuLock = new();
            private static RyzenSmuService? _smu;

            internal static RyzenSmuService? SharedSmu
            {
                get
                {
                    lock (_smuLock)
                    {
                        if (_smu != null && _smu.IsInitialized)
                        {
                            return _smu;
                        }
                        _smu?.Dispose();
                        _smu = new RyzenSmuService();
                        if (!_smu.Initialize(Assembly.GetExecutingAssembly()))
                        {
                            _smu.Dispose();
                            _smu = null;
                        }
                        return _smu;
                    }
                }
            }

            /// <summary>与旧实现逐字一致：只写模式键（不触发 UI/硬件下发）。</summary>
            internal static void SyncExternalModeStatic(int mode)
            {
                Modes.SetCurrent(mode);
                AppConfig.Set("performance_mode", mode);
            }
        }
    }
}

namespace MechrevoLite
{
    /// <summary>旧 UI 静态能力判定的 Core 兼容 shim（无控件）。</summary>
    internal static class BatteryLimitPanel
    {
        internal static bool IsSupportedBoard(MechrevoHw hw)
        {
            try
            {
                return hw != null && hw.UsesBaolongda
                    && MechrevoLite.Hardware.BoardWhitelist.Matches(MechrevoLite.Hardware.BoardWhitelist.ReadBoardProduct());
            }
            catch
            {
                return false;
            }
        }
    }
}

