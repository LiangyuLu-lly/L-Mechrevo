using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Display;

/// <summary>从厂商 ICC 云端按“面板 ID + 序列号/MAC”匹配并下载 ICC。</summary>
public static class CloudIccFetcher
{
    private const string PrimaryServer = "http://iccprofile.uniwill.com.tw/api/iccprofile/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public sealed record Result(bool Ok, string? FileName, string Detail);

    public static async Task<Result> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            string serial = ReadBiosSerial();
            List<string> panelIds = ReadPanelIds();
            List<string> macs = ReadMacAddresses();
            if (panelIds.Count == 0)
            {
                return new Result(false, null, "未读到面板 ID");
            }
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string panel in panelIds)
            {
                if (!string.IsNullOrWhiteSpace(serial)) names.Add(panel + "_" + serial);
                foreach (string mac in macs) names.Add(panel + "_" + mac);
                names.Add(panel);
            }
            foreach (string name in names)
            {
                foreach (string suffix in new[] { "", ".icm" })
                {
                    ct.ThrowIfCancellationRequested();
                    string url = PrimaryServer + Uri.EscapeDataString(name + suffix);
                    try
                    {
                        using HttpResponseMessage resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            continue;
                        }
                        byte[] data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        if (data.Length < 40 || data[36] != (byte)'a' || data[37] != (byte)'c' ||
                            data[38] != (byte)'s' || data[39] != (byte)'p')
                        {
                            continue;
                        }
                        string temp = Path.Combine(Path.GetTempPath(), "lm-cloud-" + Guid.NewGuid().ToString("N") + ".icm");
                        await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
                        string? imported = IccHelper.ImportIcc(temp);
                        try { File.Delete(temp); } catch { }
                        if (string.IsNullOrWhiteSpace(imported))
                        {
                            return new Result(false, null, "ICC 下载成功但导入失败");
                        }
                        return new Result(true, imported, $"已从云端获取：{url}（{data.Length} 字节）");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                    }
                }
            }
            return new Result(false, null, "云端未找到匹配当前面板的 ICC（已尝试序列号和 MAC）");
        }
        catch (OperationCanceledException)
        {
            return new Result(false, null, "已取消");
        }
        catch (Exception ex)
        {
            return new Result(false, null, "ICC 云端获取异常：" + ex.GetType().Name);
        }
    }

    private static string ReadBiosSerial()
    {
        try
        {
            using ManagementObjectSearcher searcher = new("SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                string value = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch { }
        return "";
    }

    private static List<string> ReadPanelIds()
    {
        List<string> ids = new();
        try
        {
            using ManagementObjectSearcher searcher = new(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
            foreach (ManagementObject obj in searcher.Get())
            {
                string instance = obj["InstanceName"]?.ToString() ?? "";
                int start = instance.IndexOf("DISPLAY\\", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    string rest = instance[(start + "DISPLAY\\".Length)..];
                    int slash = rest.IndexOf('\\');
                    if (slash > 0) ids.Add(rest[..slash]);
                }
                if (ids.Count == 0)
                {
                    string friendly = DecodeMonitorString(obj["UserFriendlyName"]);
                    if (!string.IsNullOrWhiteSpace(friendly)) ids.Add(friendly);
                }
            }
        }
        catch { }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string DecodeMonitorString(object? raw)
    {
        try
        {
            if (raw is ushort[] values)
            {
                return new string(values.Where(v => v > 0).Select(v => (char)v).ToArray()).Trim('\0', ' ');
            }
            if (raw is byte[] bytes)
            {
                return System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
            }
        }
        catch { }
        return "";
    }

    private static List<string> ReadMacAddresses()
    {
        List<string> list = new();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                {
                    continue;
                }
                string mac = nic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrWhiteSpace(mac)) list.Add(mac.ToUpperInvariant());
            }
        }
        catch { }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
