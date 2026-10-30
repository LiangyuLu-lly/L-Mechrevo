using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Display;

/// <summary>从厂商 ICC 云端按“面板 ID + 序列号/BIOS 序列号/MAC”匹配并下载 ICC。</summary>
public static class CloudIccFetcher
{
    private const string AistoneServer = "http://iccprofile.aistoneglobal.com:8001/api/iccprofile/";
    private const string UniwillServer = "http://iccprofile.uniwill.com.tw/api/iccprofile/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public sealed record Result(bool Ok, string? FileName, string Detail, IReadOnlyList<string>? FileNames = null);

    private sealed record MonitorId(string Pnp, string Friendly, string Serial);

    private sealed record Hit(IReadOnlyList<string> FileNames, string Url, int Bytes);

    public static async Task<Result> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            string biosSerial = ReadBiosSerial();
            List<MonitorId> monitors = ReadMonitorIds();
            List<string> macs = ReadMacAddresses();
            if (monitors.Count == 0)
            {
                return new Result(false, null, "云端获取校色失败：未读到面板 ID");
            }

            // 路径 1：<pnp>_<面板序列号> / <pnp>_<BIOS 序列号>；优先 50 系服务器。
            Hit? hit = await TryFetchAsync(
                BuildSerialCandidates(monitors, biosSerial),
                new[] { AistoneServer, UniwillServer },
                ct).ConfigureAwait(false);

            // 路径 2：<pnp>_<MAC>；优先 40 系服务器。
            if (hit == null)
            {
                hit = await TryFetchAsync(
                    BuildMacCandidates(monitors, macs),
                    new[] { UniwillServer, AistoneServer },
                    ct).ConfigureAwait(false);
            }

            // 兜底：<pnp> / <友好名>，两个服务器都试。
            if (hit == null)
            {
                hit = await TryFetchAsync(
                    BuildFallbackCandidates(monitors),
                    new[] { AistoneServer, UniwillServer },
                    ct).ConfigureAwait(false);
            }

            return hit != null
                ? new Result(true, hit.FileNames.FirstOrDefault(), $"已从云端获取 {hit.FileNames.Count} 个 ICC：{hit.Url}（{hit.Bytes} 字节）", hit.FileNames)
                : new Result(false, null, "云端获取校色失败：未找到匹配当前面板的 ICC（已尝试序列号、MAC 和兜底路径）");
        }
        catch (OperationCanceledException)
        {
            return new Result(false, null, "云端获取校色失败：已取消");
        }
        catch (Exception ex)
        {
            return new Result(false, null, "云端获取校色失败：" + ex.GetType().Name);
        }
    }

    private static async Task<Hit?> TryFetchAsync(
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> servers,
        CancellationToken ct)
    {
        foreach (string server in servers)
        {
            foreach (string name in candidates)
            {
                foreach (string suffix in new[] { "", ".zip", ".icm" })
                {
                    ct.ThrowIfCancellationRequested();
                    string url = server + Uri.EscapeDataString(name + suffix);
                    try
                    {
                        using HttpResponseMessage resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            continue;
                        }
                        byte[] data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        IReadOnlyList<string> imported = await TryImportAsync(data, name + suffix, ct).ConfigureAwait(false);
                        if (imported.Count > 0)
                        {
                            return new Hit(imported, url, data.Length);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }
            }
        }
        return null;
    }

    private static async Task<IReadOnlyList<string>> TryImportAsync(byte[] data, string safeName, CancellationToken ct)
    {
        List<string> imported = new();
        if (IsZip(data))
        {
            try
            {
                using MemoryStream zipStream = new(data);
                using ZipArchive archive = new(zipStream, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry entry in archive.Entries
                    .Where(e => e.Name.EndsWith(".icm", StringComparison.OrdinalIgnoreCase)
                        || e.Name.EndsWith(".icc", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] icc;
                    await using (Stream stream = entry.Open())
                    {
                        using MemoryStream copy = new();
                        await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
                        icc = copy.ToArray();
                    }
                    if (!IsIcc(icc))
                    {
                        continue;
                    }
                    string? name = await ImportAsync(icc, Path.GetFileNameWithoutExtension(entry.Name) + ".icm", ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(name) && !imported.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        imported.Add(name);
                    }
                }
                return imported;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return imported;
            }
        }
        if (!IsIcc(data))
        {
            return imported;
        }
        string? single = await ImportAsync(data, Path.GetFileNameWithoutExtension(safeName) + ".icm", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(single))
        {
            imported.Add(single);
        }
        return imported;
    }

    private static async Task<string?> ImportAsync(byte[] data, string fileName, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), "lm-cloud-" + Guid.NewGuid().ToString("N") + ".icm");
        try
        {
            await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
            return IccHelper.ImportIcc(temp);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static bool IsIcc(byte[] data)
    {
        return data.Length >= 40 && data[36] == (byte)'a' && data[37] == (byte)'c'
            && data[38] == (byte)'s' && data[39] == (byte)'p';
    }

    private static bool IsZip(byte[] data)
    {
        return data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B;
    }

    private static List<string> BuildSerialCandidates(IReadOnlyList<MonitorId> monitors, string biosSerial)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorId monitor in monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.Pnp))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(monitor.Serial))
            {
                result.Add(monitor.Pnp + "_" + monitor.Serial);
            }
            if (!string.IsNullOrWhiteSpace(biosSerial))
            {
                result.Add(monitor.Pnp + "_" + biosSerial);
            }
        }
        return result.ToList();
    }

    private static List<string> BuildMacCandidates(IReadOnlyList<MonitorId> monitors, IReadOnlyList<string> macs)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorId monitor in monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.Pnp))
            {
                continue;
            }
            foreach (string mac in macs)
            {
                if (!string.IsNullOrWhiteSpace(mac))
                {
                    result.Add(monitor.Pnp + "_" + mac);
                }
            }
        }
        return result.ToList();
    }

    private static List<string> BuildFallbackCandidates(IReadOnlyList<MonitorId> monitors)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorId monitor in monitors)
        {
            if (!string.IsNullOrWhiteSpace(monitor.Pnp))
            {
                result.Add(monitor.Pnp);
            }
            if (!string.IsNullOrWhiteSpace(monitor.Friendly))
            {
                result.Add(monitor.Friendly);
            }
        }
        return result.ToList();
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

    private static List<MonitorId> ReadMonitorIds()
    {
        List<MonitorId> ids = new();
        try
        {
            using ManagementObjectSearcher searcher = new(
                @"root\wmi",
                "SELECT InstanceName, UserFriendlyName, ManufacturerName, ProductCodeID, SerialNumberID FROM WmiMonitorID");
            foreach (ManagementObject obj in searcher.Get())
            {
                string instance = obj["InstanceName"]?.ToString() ?? "";
                string pnp = "";
                int start = instance.IndexOf("DISPLAY\\", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    string rest = instance[(start + "DISPLAY\\".Length)..];
                    int slash = rest.IndexOf('\\');
                    if (slash > 0) pnp = rest[..slash];
                }
                if (string.IsNullOrWhiteSpace(pnp))
                {
                    string mfg = DecodeMonitorString(obj["ManufacturerName"]);
                    string code = DecodeMonitorString(obj["ProductCodeID"]);
                    pnp = (mfg + code).Trim();
                }
                string friendly = DecodeMonitorString(obj["UserFriendlyName"]);
                string serial = DecodeMonitorString(obj["SerialNumberID"]);
                if (string.Equals(serial, "0", StringComparison.OrdinalIgnoreCase))
                {
                    serial = "";
                }
                if (!string.IsNullOrWhiteSpace(pnp) || !string.IsNullOrWhiteSpace(friendly))
                {
                    ids.Add(new MonitorId(pnp, friendly, serial));
                }
            }
        }
        catch { }
        return ids
            .GroupBy(m => m.Pnp + "|" + m.Friendly + "|" + m.Serial, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
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
