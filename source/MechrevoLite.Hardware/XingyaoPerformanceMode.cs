using System;
using System.Linq;
using System.Management;

namespace MechrevoLite.Hardware;

/// <summary>星耀 14 性能模式：WMI WMAA [3,9]/[3,10]（只读/切档，不碰 EC）。</summary>
internal static class XingyaoPerformanceMode
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

    internal static bool IsApplicable(MechrevoHw? hw)
    {
        // OpenRevo 的 Xingyao 14 manifest 同时覆盖 P916F-STX(AMD) 和 P916F-PTL(Intel)。
        // Intel PTL 的 WJXY GCU 不提供 TurboMode/性能档字段（诊断实测 TurboModeSupport=0），
        // 所以即使 GCU 已连接也必须走 WMAA；AMD + GCU 仍保留原 GCU 链路。
        if (hw == null || hw.IsIntelNucMachine || hw.IsUmiMachine || !XingyaoPmc2.IsXingyaoPlatform())
        {
            return false;
        }
        return CpuVendor.IsIntel || !hw.IsGcuConnected;
    }

    internal static bool TryReadVisualMode(out int visualMode, out string detail)
    {
        visualMode = -1;
        detail = "";
        try
        {
            if (!ResolveMailbox(out detail)) return false;
            byte[] input = new byte[InputSize];
            input[0] = 0x03;
            input[1] = 0x09;
            byte[]? output = Invoke(input);
            if (output == null || output.Length < 2)
            {
                detail = "读取性能档：WMI 返回空/长度不足";
                Logger.WriteLineThrottled("xingyao-mode-read", "[xingyao-mode] " + detail, 30000);
                return false;
            }
            int code = output[1];
            visualMode = code switch
            {
                1 => 2, // office -> 静音
                2 => 0, // balance -> 平衡
                3 => 1, // turbo -> 狂暴
                _ => -1
            };
            detail = $"读取性能档 code={code} visual={(visualMode >= 0 ? visualMode.ToString() : "未知")} status={output[0]} raw={Hex(output, 8)}";
            return visualMode >= 0;
        }
        catch (Exception ex)
        {
            detail = "读取性能档异常：" + ex.GetType().Name + " " + ex.Message;
            Logger.WriteLineThrottled("xingyao-mode-read-ex", "[xingyao-mode] " + detail, 30000);
            return false;
        }
    }

    internal static bool TryApplyVisualMode(int visualMode, out string detail)
    {
        detail = "";
        int code = visualMode switch
        {
            2 => 1, // 静音 -> office
            0 => 2, // 平衡 -> balance
            1 => 3, // 狂暴 -> turbo
            _ => -1
        };
        if (code < 0)
        {
            detail = "不支持的视觉档：" + visualMode;
            return false;
        }
        try
        {
            if (!ResolveMailbox(out detail)) return false;
            byte[] input = new byte[InputSize];
            input[0] = 0x03;
            input[1] = 0x0A;
            input[2] = (byte)code;
            byte[]? output = Invoke(input);
            if (output == null || output.Length < 2)
            {
                detail = $"切换性能档 code={code}：WMI 返回空/长度不足";
                return false;
            }
            int status = output[0];
            int readCode = output[1];
            // SPFM 的 WMAA 返回状态在 PTL 上是 ECMD 结果码：0x94/0x95/0x96
            // 分别对应三个档位，不是失败；OpenRevo 同样不把状态字节当错误。
            bool statusAccepted = status == 0 || status == 0x94 || status == 0x95 || status == 0x96;
            bool ok = statusAccepted;
            detail = $"切换性能档 visual={visualMode} code={code} status={status} readCode={readCode} ok={ok} raw={Hex(output, 8)}";
            return ok;
        }
        catch (Exception ex)
        {
            detail = "切换性能档异常：" + ex.GetType().Name + " " + ex.Message;
            return false;
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
                Logger.WriteLine("[xingyao-mode] " + detail);
                return true;
            }
            catch (Exception ex)
            {
                detail = className + " 枚举失败：" + ex.Message;
                Logger.WriteError("[xingyao-mode] " + detail);
            }
        }
        detail = "root\\wmi 未找到 OemWMIMethod/AcpiTest_WMI";
        Logger.WriteError("[xingyao-mode] " + detail);
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
