using System;
using System.Threading;

namespace MechrevoLite.Hardware;

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

    internal static void Sync(MechrevoHw hw)
    {
        if (TryRead(FnLockAddress, out int fn))
        {
            hw.QuickSwitches["fnkey"] = (fn & FnLockBit) != 0;
        }
        if (TryRead(CameraCutoffAddress, out int cam))
        {
            hw.QuickSwitches["webcam"] = (cam & CameraCutoffBit) == 0;
        }
        if (TryRead(WinKeyStatusAddress, out int win))
        {
            hw.QuickSwitches["winkey"] = (win & WinKeyBit) != 0;
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
