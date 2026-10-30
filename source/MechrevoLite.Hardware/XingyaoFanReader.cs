using System;
using System.Linq;
using System.Management;

namespace MechrevoLite.Hardware;

/// <summary>星耀 14（P916F）风扇只读遥测：WMI OemWMIMethod / OemWMIfun。</summary>
internal static class XingyaoFanReader
{
    private const string WmiNamespace = @"root\wmi";
    private const string PrimaryClass = "OemWMIMethod";
    private const string FallbackClass = "AcpiTest_WMI";
    private const string MethodName = "OemWMIfun";
    private const int InputSize = 64;
    private const int OutputSize = 256;

    private static bool _resolved;
    private static ManagementClass? _class;
    private static ManagementObject? _instance;

    internal static bool TryReadFanRpm(out int mainFanRpm, out int secondaryFanRpm)
    {
        mainFanRpm = -1;
        secondaryFanRpm = -1;
        try
        {
            if (!XingyaoPmc2.IsXingyaoPlatform())
            {
                return false;
            }
            if (!ResolveMailbox())
            {
                return false;
            }
            // GFNS 的 selector 在 request byte 0x02：0x00=FNS0，0x01=FNS1。
            // 每次调用只返回一个 word（response byte 1..2）。旧版照 OpenRevo
            // manifest 用一次 [2,1] 同时解析 offset1/offset3，第二个 word 是无效的。
            bool ok0 = TryReadChannel(0x00, out mainFanRpm, out string d0);
            bool ok1 = TryReadChannel(0x01, out secondaryFanRpm, out string d1);
            Logger.WriteLineThrottled("xingyao-fan-ok",
                $"[xingyao-fan] GFNS selector0 cpu={mainFanRpm} ok={ok0} · selector1 gpu={secondaryFanRpm} ok={ok1} · {d0} · {d1}",
                30000);
            if (!ok0 && !ok1)
            {
                Logger.WriteLineThrottled("xingyao-fan-wmi-empty", "[xingyao-fan] GFNS 两路都失败： " + d0 + " / " + d1, 60000);
                return false;
            }
            return mainFanRpm > 0 || secondaryFanRpm > 0;
        }
        catch (Exception ex)
        {
            Logger.WriteLineThrottled("xingyao-fan-wmi", "[xingyao-fan] WMI 读取异常：" + ex.Message, 60000);
            return false;
        }
    }

    private static bool TryReadChannel(byte selector, out int rpm, out string detail)
    {
        rpm = -1;
        detail = "";
        try
        {
            byte[] input = new byte[InputSize];
            input[0] = 0x02;
            input[1] = 0x01;
            input[2] = selector;
            byte[]? output = Invoke(input);
            if (output == null || output.Length < 3)
            {
                detail = "selector=" + selector + " 返回空/长度不足";
                return false;
            }
            rpm = output[1] | (output[2] << 8);
            detail = "selector=" + selector + " status=" + output[0] + " raw=" + BitConverter.ToString(output, 0, Math.Min(5, output.Length)) + " rpm=" + rpm;
            return output[0] == 0 && rpm >= 0;
        }
        catch (Exception ex)
        {
            detail = "selector=" + selector + " 异常：" + ex.GetType().Name + " " + ex.Message;
            return false;
        }
    }

    private static bool ResolveMailbox()
    {
        if (_resolved) return _class != null && _instance != null;
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
                if (_instance == null)
                {
                    continue;
                }
                _class = new ManagementClass(WmiNamespace, className, null);
                Logger.WriteLine("[xingyao-fan] WMI mailbox class=" + className + " instance=" + _instance.Path.RelativePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLineThrottled("xingyao-fan-wmi-class", "[xingyao-fan] " + className + " 枚举失败：" + ex.Message, 60000);
            }
        }
        Logger.WriteLineThrottled("xingyao-fan-wmi-none", "[xingyao-fan] root\\wmi 未找到 OemWMIMethod/AcpiTest_WMI", 60000);
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
}
