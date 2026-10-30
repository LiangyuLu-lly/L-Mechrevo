using System;
using System.Linq;
using System.Management;

namespace MechrevoLite.Hardware;

/// <summary>星耀 P916F 的 WMI 快捷开关（Win 锁 / Fn 锁 / Copilot 锁 / 触摸板）。</summary>
internal static class XingyaoQuickSwitch
{
    private const string WmiNamespace = @"root\wmi";
    private const string PrimaryClass = "OemWMIMethod";
    private const string FallbackClass = "AcpiTest_WMI";
    private const string MethodName = "OemWMIfun";
    private const int InputSize = 64;

    private static bool _resolved;
    private static ManagementClass? _class;
    private static ManagementObject? _instance;

    internal static bool IsApplicable(MechrevoHw? hw)
    {
        return hw != null
            && !hw.IsIntelNucMachine
            && !hw.IsUmiMachine
            && XingyaoPmc2.IsXingyaoPlatform();
    }

    internal static bool TryGet(string key, out bool value, out string detail)
    {
        value = false;
        detail = "";
        if (!TryResolveAction(key, out int getSfid, out int setSfid))
        {
            detail = "未知快捷开关：" + key;
            return false;
        }
        try
        {
            if (!ResolveMailbox(out detail)) return false;
            byte[] input = new byte[InputSize];
            input[0] = 0x03;
            input[1] = (byte)getSfid;
            byte[]? output = Invoke(input);
            if (output == null || output.Length < 2)
            {
                detail = key + " 读取返回空/长度不足";
                Logger.WriteLineThrottled("xingyao-quick-read-" + key, "[xingyao-quick] " + detail, 30000);
                return false;
            }
            value = output[1] != 0;
            detail = $"{key} 读取 status={output[0]} value={output[1]} raw={Hex(output, 6)}";
            return output[0] == 0;
        }
        catch (Exception ex)
        {
            detail = key + " 读取异常：" + ex.GetType().Name + " " + ex.Message;
            return false;
        }
    }

    internal static bool TrySet(string key, bool on, out string detail)
    {
        detail = "";
        if (!TryResolveAction(key, out int getSfid, out int setSfid))
        {
            detail = "未知快捷开关：" + key;
            return false;
        }
        try
        {
            if (!ResolveMailbox(out detail)) return false;
            byte[] input = new byte[InputSize];
            input[0] = 0x03;
            input[1] = (byte)setSfid;
            input[2] = on ? (byte)1 : (byte)0;
            byte[]? output = Invoke(input);
            if (output == null)
            {
                detail = key + " 写入返回空";
                Logger.WriteError("[xingyao-quick] " + detail);
                return false;
            }
            bool readOk = TryGet(key, out bool read, out string readDetail);
            bool ok = readOk && read == on;
            detail = $"{key} 写入 on={on} status={output[0]} readOk={readOk} read={read} raw={Hex(output, 6)} · {readDetail}";
            if (!ok) Logger.WriteError("[xingyao-quick] " + detail);
            return ok;
        }
        catch (Exception ex)
        {
            detail = key + " 写入异常：" + ex.GetType().Name + " " + ex.Message;
            Logger.WriteError("[xingyao-quick] " + detail);
            return false;
        }
    }

    /// <summary>OpenRevo set_kb_sleep_timeout：[3,12,1,0]=常亮，[3,12,0,0]=10 秒。</summary>
    internal static bool TrySetKeyboardSleepTimeout(bool alwaysOn, out string detail)
    {
        detail = "";
        try
        {
            if (!ResolveMailbox(out detail)) return false;
            byte[] input = new byte[InputSize];
            input[0] = 0x03;
            input[1] = 0x0C;
            input[2] = alwaysOn ? (byte)1 : (byte)0;
            input[3] = 0x00;
            byte[]? output = Invoke(input);
            if (output == null)
            {
                detail = "键盘睡眠写入返回空";
                Logger.WriteError("[xingyao-quick] " + detail);
                return false;
            }
            bool ok = output[0] == 0;
            detail = $"键盘睡眠 alwaysOn={alwaysOn} status={output[0]} ok={ok} raw={Hex(output, 6)}";
            if (!ok) Logger.WriteError("[xingyao-quick] " + detail);
            return ok;
        }
        catch (Exception ex)
        {
            detail = "键盘睡眠写入异常：" + ex.GetType().Name + " " + ex.Message;
            Logger.WriteError("[xingyao-quick] " + detail);
            return false;
        }
    }

    private static bool TryResolveAction(string key, out int getSfid, out int setSfid)
    {
        getSfid = -1;
        setSfid = -1;
        switch (key)
        {
            case "winkey": getSfid = 1; setSfid = 2; return true;
            case "fnkey": getSfid = 3; setSfid = 4; return true;
            case "copilot": getSfid = 5; setSfid = 6; return true;
            case "touchpad": getSfid = 7; setSfid = 8; return true;
            default: return false;
        }
    }

    private static bool ResolveMailbox(out string detail)
    {
        detail = "";
        if (_resolved)
        {
            if (_class == null || _instance == null)
            {
                detail = "WMAA 类未解析到";
                return false;
            }
            return true;
        }
        _resolved = true;
        foreach (string className in new[] { PrimaryClass, FallbackClass })
        {
            try
            {
                using ManagementObjectSearcher searcher = new(WmiNamespace, "SELECT * FROM " + className);
                ManagementObjectCollection results = searcher.Get();
                ManagementObject? first = null;
                foreach (ManagementObject obj in results)
                {
                    first ??= obj;
                    string instanceName = obj["InstanceName"]?.ToString() ?? "";
                    if (instanceName.IndexOf("HWMI", StringComparison.OrdinalIgnoreCase) >= 0
                        || instanceName.IndexOf("MIFS", StringComparison.OrdinalIgnoreCase) >= 0
                        || obj["Active"] is bool active && active)
                    {
                        _instance = obj;
                        break;
                    }
                }
                _instance ??= first;
                if (_instance == null) continue;
                _class = new ManagementClass(WmiNamespace, className, null);
                detail = "WMAA 类=" + className + " 实例=" + _instance.Path.RelativePath;
                Logger.WriteLine("[xingyao-quick] " + detail);
                return true;
            }
            catch (Exception ex)
            {
                detail = className + " 枚举失败：" + ex.Message;
                Logger.WriteError("[xingyao-quick] " + detail);
            }
        }
        detail = "root\\wmi 未找到 OemWMIMethod/AcpiTest_WMI";
        Logger.WriteError("[xingyao-quick] " + detail);
        return false;
    }

    private static byte[]? Invoke(byte[] input)
    {
        if (_class == null || _instance == null) return null;
        using ManagementBaseObject inParams = _class.GetMethodParameters(MethodName);
        bool set = false;
        foreach (PropertyData property in inParams.Properties)
        {
            if (!property.IsArray)
            {
                inParams[property.Name] = 0;
                continue;
            }
            string name = property.Name ?? "";
            if (name.Equals("u8Input", StringComparison.OrdinalIgnoreCase)
                || name.Equals("InData", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || !set)
            {
                inParams[property.Name] = input;
                set = true;
            }
        }
        if (!set) return null;
        using ManagementBaseObject outParams = _instance.InvokeMethod(MethodName, inParams, null);
        if (outParams == null) return null;
        foreach (string candidate in new[] { "u8Output", "OutData", "OutBuf", "Return" })
        {
            try
            {
                object? value = outParams[candidate];
                if (value is byte[] bytes && bytes.Length > 0) return bytes;
            }
            catch
            {
            }
        }
        foreach (PropertyData property in outParams.Properties)
        {
            if (property.Value is byte[] bytes && bytes.Length > 0) return bytes;
        }
        return null;
    }

    private static string Hex(byte[] data, int count)
    {
        return string.Join(" ", data.Take(Math.Min(count, data.Length)).Select(b => b.ToString("X2")));
    }
}
