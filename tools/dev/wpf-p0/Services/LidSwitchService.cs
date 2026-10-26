using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>盖子状态变化事件参数（<see cref="Closed" /> = true 表示合盖）。</summary>
internal sealed class LidStateChangedEventArgs : EventArgs
{
    internal LidStateChangedEventArgs(bool closed)
    {
        Closed = closed;
    }

    /// <summary>true = 合盖（旧 LIDSWITCH Data=0）· false = 开盖（Data=1）。</summary>
    internal bool Closed { get; }
}

/// <summary>
/// ⭐⭐ 盖子开关监听服务（旧 WinForms <c>Program.cs:1175</c> 注册 + <c>SettingsForm.cs:12273</c> 处理的 WPF 等价）✅
/// <para>⭐ 旧实现：<c>RegisterPowerSettingNotification(settingsForm.Handle, LIDSWITCH_STATE_CHANGE)</c>，
/// WndProc 收到 <c>PBT_POWERSETTINGCHANGE</c> 后按 Data 0/1 处理。本服务用**独立的 message-only 窗口**
/// （<c>HWND_MESSAGE</c>）做同样的事，⛔ 不依赖任何 UI 窗口、⛔ 不改窗口 WndProc。</para>
/// <para>⭐⭐ <b>本服务只负责“监听 + 解析 + 广播”</b>：
/// 合盖关/开盖恢复键盘背光由 <see cref="OriginalStyleWindow" /> 订阅 <see cref="LidStateChanged" /> 后，
/// 用**现有等价 API** 实现（HID：<c>KeyboardRgb.StopCurrentEffect()</c>/<c>KbPowerSyncFromHardware(false)</c>/
/// <c>ApplyKeyboardLightAsync()</c>；EC/固件：<c>BackendProbe.SetKeyboardPowerAsync(false)</c>/
/// <c>ApplyKeyboardLightAsync()</c>）。</para>
/// <para>⚠️ 旧 <c>Aura.ApplyBrightness</c> / <c>InputDispatcher.GetBacklight</c> 在可得的反编译源里是
/// **空实现（stub）**（见基线提交 <c>d8c59545:source/MechrevoLite/Aura.cs</c> / <c>InputDispatcher.cs</c>），
/// 没有可照搬的真实算法 ⇒ 本服务**不伪造**任何 Aura 行为，只按“关/恢复”两个语义调用现有 API ✅。</para>
/// <para>⚠️ 不真实合盖：<see cref="RunSelfCheck" /> 用**合成 lParam** 校验 GUID/常量/解析，不触发硬件。</para>
/// </summary>
internal sealed class LidSwitchService : IDisposable
{
    /// <summary>旧 LIDSWITCH_STATE_CHANGE GUID（与 <c>NativeMethods.PowerSettingGuid</c> 同一来源）。</summary>
    internal static readonly Guid LidSwitchGuid = NativeMethods.PowerSettingGuid.LIDSWITCH_STATE_CHANGE;

    /// <summary>期望的 GUID 字面量（自检用；防止上游常量被误改）。</summary>
    internal static readonly Guid ExpectedLidSwitchGuid = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");

    private const uint DeviceNotifyWindowHandle = 0u;

    /// <summary>HWND_MESSAGE：message-only 窗口父句柄（(HWND)-3）。</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly object _gate = new();
    private HwndSource? _source;
    private nint _registration;
    private bool _disposed;
    private bool _lidClosed;
    private string _lastError = "";

    /// <summary>注册是否成功；false ⇒ 本服务安全 no-op（只记日志，不影响程序）。</summary>
    internal bool Registered { get; private set; }

    /// <summary>当前是否合盖（最后一次收到的状态；未收到过为 false）。</summary>
    internal bool LidClosed
    {
        get
        {
            lock (_gate)
            {
                return _lidClosed;
            }
        }
    }

    /// <summary>最近一次失败原因（未失败为空串）。</summary>
    internal string LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <summary>盖子状态变化（在 UI 线程的 HwndSource 钩子上同步触发）。</summary>
    internal event EventHandler<LidStateChangedEventArgs>? LidStateChanged;

    /// <summary>
    /// 创建 message-only 窗口并注册 LIDSWITCH 通知（必须在 UI/STA 线程调用）。
    /// <para>⚠️ 失败只记日志并返回 false（安全 no-op），⛔ 不抛异常、⛔ 不伪造支持。</para>
    /// </summary>
    internal bool Start()
    {
        if (_disposed || _source != null)
        {
            return Registered;
        }
        try
        {
            HwndSourceParameters parameters = new("L-Mechrevo.LidSwitch")
            {
                ParentWindow = HwndMessage,
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                Width = 0,
                Height = 0
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _registration = NativeMethods.RegisterPowerSettingNotification(
                _source.Handle,
                LidSwitchGuid,
                DeviceNotifyWindowHandle);
            Registered = _registration != IntPtr.Zero;
            if (!Registered)
            {
                _lastError = "RegisterPowerSettingNotification 返回 0";
                Logger.WriteError("[lid] ⚠️ 注册 LIDSWITCH_STATE_CHANGE 失败 ⇒ 合盖/开盖自动关灯恢复不可用（安全 no-op；不伪造）");
            }
            else
            {
                Logger.WriteLine("[lid] 已注册 LIDSWITCH_STATE_CHANGE（message-only hwnd=0x"
                    + _source.Handle.ToInt64().ToString("X") + "）");
            }
            return Registered;
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;
            Logger.WriteError("[lid] ⚠️ 创建盖子监听失败（安全 no-op，不伪造）：" + _lastError);
            Stop();
            return false;
        }
    }

    /// <summary>注销并销毁 message-only 窗口（幂等）。</summary>
    internal void Stop()
    {
        if (_registration != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterPowerSettingNotification(_registration);
            }
            catch
            {
            }
            _registration = IntPtr.Zero;
        }
        Registered = false;
        try
        {
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }
        catch
        {
        }
        _source = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_POWERBROADCAST || wParam.ToInt32() != NativeMethods.PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        if (!TryReadLidState(lParam, out bool closed))
        {
            return IntPtr.Zero;
        }
        lock (_gate)
        {
            if (_lidClosed == closed)
            {
                // 同状态重复通知 ⇒ 不重复下发/不刷屏（旧实现也没去重，这里更保守更安全）。
                handled = true;
                return IntPtr.Zero;
            }
            _lidClosed = closed;
        }
        Logger.WriteLine("[lid] " + (closed ? "合盖（LIDSWITCH=0）" : "开盖（LIDSWITCH=1）"));
        try
        {
            LidStateChanged?.Invoke(this, new LidStateChangedEventArgs(closed));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[lid] 事件处理异常（已吞掉，⛔ 不影响消息循环）：" + ex.Message);
        }
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>
    /// ⭐ 解析 <c>POWERBROADCAST_SETTING</c>（lParam）→ 盖子状态；非盖开关/长度不足 ⇒ false。
    /// <para>旧处理：Data 0=合盖、1=开盖；这里额外要求 DataLength ≥ 4（与旧 ConsoleDisplayState 的防误读修正同口径）。</para>
    /// </summary>
    internal static bool TryReadLidState(IntPtr lParam, out bool closed)
    {
        closed = false;
        if (lParam == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            NativeMethods.POWERBROADCAST_SETTING setting =
                Marshal.PtrToStructure<NativeMethods.POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting != LidSwitchGuid || setting.DataLength < 4)
            {
                return false;
            }
            if (setting.Data > 1)
            {
                return false;
            }
            closed = setting.Data == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ 服务自检（⛔ 不真实合盖、⛔ 不触发硬件）：校验 GUID/常量 + 用**合成 lParam** 验证解析语义。
    /// <para>供启动日志/离线验证调用；返回 false 时 <paramref name="detail" /> 给出原因。</para>
    /// </summary>
    internal static bool RunSelfCheck(out string detail)
    {
        try
        {
            if (LidSwitchGuid != ExpectedLidSwitchGuid)
            {
                detail = "LIDSWITCH GUID 与基线不一致：" + LidSwitchGuid;
                return false;
            }
            // 用局部变量比较，避免编译器把“常量相等”折叠成恒假条件（会报 CS0162 不可达代码）。
            int expectedWmPowerBroadcast = 0x0218;
            int expectedPbtPowerSettingChange = 0x8013;
            if (NativeMethods.WM_POWERBROADCAST != expectedWmPowerBroadcast
                || NativeMethods.PBT_POWERSETTINGCHANGE != expectedPbtPowerSettingChange)
            {
                detail = "POWERBROADCAST 常量与基线不一致";
                return false;
            }
            IntPtr closedPtr = BuildSyntheticSetting(0u, 4u);
            IntPtr openPtr = BuildSyntheticSetting(1u, 4u);
            IntPtr shortPtr = BuildSyntheticSetting(0u, 2u);
            try
            {
                if (!TryReadLidState(closedPtr, out bool closed) || !closed)
                {
                    detail = "合成 Data=0（合盖）解析失败";
                    return false;
                }
                if (!TryReadLidState(openPtr, out bool open) || open)
                {
                    detail = "合成 Data=1（开盖）解析失败";
                    return false;
                }
                if (TryReadLidState(shortPtr, out _))
                {
                    detail = "DataLength<4 未被拒绝（会被误判为盖开关）";
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(closedPtr);
                Marshal.FreeHGlobal(openPtr);
                Marshal.FreeHGlobal(shortPtr);
            }
            detail = "GUID/常量 OK；合成解析：0=合盖、1=开盖、DataLength<4=拒绝（未真实合盖）";
            return true;
        }
        catch (Exception ex)
        {
            detail = "自检异常：" + ex.GetType().Name + " " + ex.Message;
            return false;
        }
    }

    private static IntPtr BuildSyntheticSetting(uint data, uint dataLength)
    {
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.POWERBROADCAST_SETTING>());
        NativeMethods.POWERBROADCAST_SETTING setting = new()
        {
            PowerSetting = LidSwitchGuid,
            DataLength = dataLength,
            Data = data
        };
        Marshal.StructureToPtr(setting, pointer, fDeleteOld: false);
        return pointer;
    }
}
