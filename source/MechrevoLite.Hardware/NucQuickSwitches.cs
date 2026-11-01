using System;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace MechrevoLite.Hardware;

// 机型适配参考：OpenRevo (https://github.com/faintonce/open-revo, GPL-3.0)。

/// <summary>NUC/UMI 高频快捷开关：Fn 锁、摄像头、Win 键锁，直接走 ACPI EC。</summary>
internal static class NucQuickSwitches
{
    private const int FnLockAddress = 1870;      // 0x74E bit4
    private const int CameraCutoffAddress = 1831; // 0x727 bit3
    private const int WinKeyStatusAddress = 1896; // 0x768 bit0
    private const int TriggerAddress = 1895;      // 0x767 bit0
    private const int FnLockBit = 0x10;
    private const int CameraCutoffBit = 0x08;
    private const int WinKeyBit = 0x01;
    private const int TouchpadAddress = 1958;   // 0x7A6 bit3
    private const int TouchpadBit = 0x08;
    private const int UsbChargeTriggerBit = 0x10;
    private const int AcRecoveryAddress = 1830; // 0x726 bit3
    private const int AcRecoveryBit = 0x08;
    private const int FanBoostAddress = 1873; // 0x751
    private const int FanBoostBit = 0x40;

    internal static bool TryReadFnLock(out bool on)
    {
        on = false;
        if (!TryRead(FnLockAddress, out int current))
        {
            return false;
        }
        on = (current & FnLockBit) != 0;
        return true;
    }

    internal static bool TrySetFnLock(bool on, out bool confirmed)
    {
        confirmed = false;
        if (!TryRead(FnLockAddress, out int current))
        {
            return false;
        }
        int target = on ? (current | FnLockBit) : (current & ~FnLockBit);
        if (!TryWrite(FnLockAddress, target))
        {
            return false;
        }
        confirmed = TryRead(FnLockAddress, out int after) && ((after & FnLockBit) != 0) == on;
        return confirmed;
    }

    /// <summary>on=true 表示摄像头启用；EC bit3=1 表示物理断电。</summary>
    internal static bool TrySetCamera(bool on, out bool confirmed)
    {
        confirmed = false;
        if (!TryRead(CameraCutoffAddress, out int current))
        {
            return false;
        }
        int target = on ? (current & ~CameraCutoffBit) : (current | CameraCutoffBit);
        if (!TryWrite(CameraCutoffAddress, target))
        {
            return false;
        }
        confirmed = TryRead(CameraCutoffAddress, out int after) && ((after & CameraCutoffBit) == 0) == on;
        return confirmed;
    }

    internal static bool TrySetWinKeyLock(bool on, out bool confirmed)
    {
        confirmed = false;
        if (!TryRead(WinKeyStatusAddress, out int status))
        {
            return false;
        }
        if (((status & WinKeyBit) != 0) == on)
        {
            confirmed = true;
            return true;
        }
        if (!TryRead(TriggerAddress, out int trigger) || !TryWrite(TriggerAddress, trigger | WinKeyBit))
        {
            return false;
        }
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(50);
            if (TryRead(WinKeyStatusAddress, out int now) && ((now & WinKeyBit) != 0) == on)
            {
                confirmed = true;
                return true;
            }
        }
        return false;
    }

    internal static bool TrySetTouchpad(bool on, out bool confirmed)
    {
        confirmed = false;
        if (!TryRead(TouchpadAddress, out int current))
        {
            return false;
        }
        int target = on ? (current | TouchpadBit) : (current & ~TouchpadBit);
        if (!TryWrite(TouchpadAddress, target))
        {
            return false;
        }
        confirmed = TryRead(TouchpadAddress, out int after) && ((after & TouchpadBit) != 0) == on;
        WritePtpRegistry(on);
        // Windows 输入子系统还需要 EnableTouchpad 开关；只写 EC/PTP 注册表不足以真正启用/禁用触摸板。
        bool systemOk = TrySystemTouchpad(on, out string systemDetail);
        Logger.WriteLine($"[touchpad] EC={confirmed} System={systemOk}（{systemDetail}）");
        confirmed = confirmed && (systemOk || !File.Exists(SystemTouchpadTool));
        return confirmed;
    }

    /// <summary>
    /// NUC/UMI 的关机 USB 供电是 trigger bit4 的“翻转脉冲”，不是常驻位。
    /// 旧实现只置位、不清零，会把 USB 触发位一直挂住，并可能让固件顺带清掉
    /// 来电自启（1830 bit3）；这里改成“置位→30ms→清零”，并恢复来电自启位。
    /// </summary>
    internal static bool TryPulseUsbCharging(bool requestedOn, bool currentOn, out bool confirmed)
    {
        confirmed = false;
        // 已处于目标状态：不重复翻转（翻转位每写一次会切一次）。
        if (requestedOn == currentOn)
        {
            confirmed = true;
            return true;
        }
        if (!TryRead(TriggerAddress, out int trigger))
        {
            return false;
        }
        bool acBefore = TryRead(AcRecoveryAddress, out int acOld) && (acOld & AcRecoveryBit) != 0;
        if (!TryWrite(TriggerAddress, trigger | UsbChargeTriggerBit))
        {
            return false;
        }
        Thread.Sleep(30);
        // 清掉 trigger，别让它一直挂着。
        TryWrite(TriggerAddress, trigger & ~UsbChargeTriggerBit);
        bool pulseOk = TryRead(TriggerAddress, out int after) && (after & UsbChargeTriggerBit) == 0;
        // USB 触发可能顺带清掉来电自启；按切换前的值恢复。
        if (TryRead(AcRecoveryAddress, out int acAfter) && (acAfter & AcRecoveryBit) == 0 && acBefore)
        {
            TryWrite(AcRecoveryAddress, acAfter | AcRecoveryBit);
        }
        confirmed = pulseOk;
        return pulseOk;
    }

    internal static bool TrySetAcRecovery(bool on, out bool confirmed)
    {
        confirmed = false;
        if (!TryRead(AcRecoveryAddress, out int current))
        {
            return false;
        }
        int target = on ? (current | AcRecoveryBit) : (current & ~AcRecoveryBit);
        if (!TryWrite(AcRecoveryAddress, target))
        {
            return false;
        }
        confirmed = TryRead(AcRecoveryAddress, out int after) && ((after & AcRecoveryBit) != 0) == on;
        return confirmed;
    }

    internal static bool TrySetFanBoost(bool on, out bool confirmed)
    {
        confirmed = false;
        // NUC/UMI 30 官方 SetEcFanBoost：开写 0x40，关写 0x00；
        // 保留同字节其它位，避免踩到 Q-Key/其它 clamp 状态。
        if (!TryRead(FanBoostAddress, out int current))
        {
            return false;
        }
        int target = on ? (current | 0x40) : (current & ~0x40);
        if (!TryWrite(FanBoostAddress, target))
        {
            return false;
        }
        confirmed = TryRead(FanBoostAddress, out int after) && ((after & 0x40) != 0) == on;
        return confirmed;
    }

    private const string SystemTouchpadTool = @"C:\Windows\System32\SystemSettingsAdminFlows.exe";

    private static bool TrySystemTouchpad(bool on, out string detail)
    {
        detail = "";
        try
        {
            if (!File.Exists(SystemTouchpadTool))
            {
                detail = "SystemSettingsAdminFlows 不存在";
                return false;
            }
            ProcessStartInfo psi = new(SystemTouchpadTool, "EnableTouchpad " + (on ? "1" : "0"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process? process = Process.Start(psi))
            {
                if (process == null)
                {
                    detail = "启动失败";
                    return false;
                }
                process.WaitForExit(2000);
            }
            if (on)
            {
                // 从设备管理器 PnP 禁用状态恢复；只做 enable，不做 disable（禁用由 Windows 输入子系统负责）。
                foreach (string deviceId in new[] { @"HID\UNIW0001&Col01", @"HID\UNIW0001&Col02" })
                {
                    ProcessStartInfo pnputil = new("pnputil", "/enable-device /deviceid \"" + deviceId + "\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using Process? p = Process.Start(pnputil);
                    p?.WaitForExit(1500);
                }
            }
            detail = "已调用 Windows EnableTouchpad";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name;
            return false;
        }
    }

    private static void WritePtpRegistry(bool on)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status", writable: true);
            key?.SetValue("Enabled", on ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch
        {
        }
    }

    internal static void Sync(MechrevoHw hw)
    {
        if (TryRead(FnLockAddress, out int fn))
        {
            hw.QuickSwitches["fnkey"] = (fn & FnLockBit) != 0;
        }
        if (CameraPnpControl.TryGetEnabled(out bool cameraEnabled, out _))
        {
            hw.QuickSwitches["webcam"] = cameraEnabled;
        }
        else if (TryRead(CameraCutoffAddress, out int cam))
        {
            hw.QuickSwitches["webcam"] = (cam & CameraCutoffBit) == 0;
        }
        if (TryRead(WinKeyStatusAddress, out int win))
        {
            hw.QuickSwitches["winkey"] = (win & WinKeyBit) != 0;
        }
        if (TryRead(TouchpadAddress, out int tp))
        {
            hw.QuickSwitches["touchpad"] = (tp & TouchpadBit) != 0;
        }
        if (TryRead(AcRecoveryAddress, out int ac))
        {
            hw.QuickSwitches["acrecovery"] = (ac & AcRecoveryBit) != 0;
        }
        if (TryRead(FanBoostAddress, out int fb))
        {
            hw.QuickSwitches["fanboost"] = (fb & FanBoostBit) != 0;
        }
    }

    private static bool TryRead(int address, out int value)
    {
        if (AcpiDriverEc.TryReadByte(address, out byte b))
        {
            value = b;
            return true;
        }
        if (EcPortIo.TryReadByteStable(address, out int legacy, out _))
        {
            value = legacy;
            return true;
        }
        value = -1;
        return false;
    }

    private static bool TryWrite(int address, int value)
    {
        return AcpiDriverEc.TryWriteByte(address, value) || EcPortIo.TryWriteByte(address, value);
    }
}
