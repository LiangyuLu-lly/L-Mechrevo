using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>独立更新器：等待主程序退出后按 manifest 替换 DLL，失败回滚并重启。</summary>
internal static class DllUpdateHelper
{
    private const string Argument = "--apply-dll-update";

    internal static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 4 || !string.Equals(args[0], Argument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        string packageDir = args[1];
        string installDir = args[2];
        int parentPid = int.TryParse(args[3], out int pid) ? pid : -1;
        try
        {
            WaitForExit(parentPid, TimeSpan.FromSeconds(120));
            Apply(packageDir, installDir);
            Restart(installDir);
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Log("更新失败：" + ex);
            Restart(installDir);
            exitCode = 2;
        }
        return true;
    }

    private static void Apply(string packageDir, string installDir)
    {
        string manifestPath = Path.Combine(packageDir, "manifest.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException(WpfStrings.L("缺少 manifest.json", "Missing manifest.json"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifest.RootElement.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(WpfStrings.L("manifest 无 files", "manifest has no files"));
        string installRoot = Path.GetFullPath(installDir);
        var applied = new System.Collections.Generic.List<(string Target, string Backup)>();
        try
        {
            foreach (JsonElement token in files.EnumerateArray())
            {
                string rel = token.TryGetProperty("path", out JsonElement pathElement) ? pathElement.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(rel)) throw new InvalidDataException(WpfStrings.L("manifest path 为空", "manifest path is empty"));
                string normalized = rel.Replace('\\', '/');
                if (normalized.StartsWith("/") || normalized.Contains("..")) throw new InvalidDataException(WpfStrings.L("非法路径：", "Illegal path: ") + rel);
                string source = Path.GetFullPath(Path.Combine(packageDir, normalized.Replace('/', Path.DirectorySeparatorChar)));
                string target = Path.GetFullPath(Path.Combine(installRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(WpfStrings.L("目标越界：", "Target escapes root: ") + rel);
                if (!File.Exists(source)) throw new FileNotFoundException(WpfStrings.L("更新文件缺失：", "Update file missing: ") + rel);
                string expectedSha = token.TryGetProperty("sha256", out JsonElement shaElement) ? shaElement.GetString() ?? "" : "";
                long expectedSize = token.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long size) ? size : -1;
                VerifyFile(source, expectedSize, expectedSha, rel);
                string backup = target + ".lmupdate.bak";
                try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                if (File.Exists(target)) File.Move(target, backup);
                applied.Add((target, backup));
                File.Copy(source, target, overwrite: true);
                VerifyFile(target, expectedSize, expectedSha, rel);
                Log("已替换：" + rel);
            }
            foreach (var item in applied)
            {
                try { if (File.Exists(item.Backup)) File.Delete(item.Backup); } catch { }
            }
        }
        catch
        {
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                var item = applied[i];
                try
                {
                    if (File.Exists(item.Target)) File.Delete(item.Target);
                    if (File.Exists(item.Backup)) File.Move(item.Backup, item.Target);
                }
                catch { }
            }
            throw;
        }
    }

    private static void VerifyFile(string path, long expectedSize, string expectedSha, string rel)
    {
        var info = new FileInfo(path);
        if (expectedSize > 0 && info.Length != expectedSize) throw new InvalidDataException(WpfStrings.L("大小不符：", "Size mismatch: ") + rel);
        if (!string.IsNullOrWhiteSpace(expectedSha))
        {
            using FileStream fs = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(fs));
            if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(WpfStrings.L("SHA256 不符：", "SHA256 mismatch: ") + rel);
        }
    }

    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        if (pid <= 0) return;
        try
        {
            using Process p = Process.GetProcessById(pid);
            p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException) { }
        Thread.Sleep(1200);
    }

    private static void Restart(string installDir)
    {
        try
        {
            string exe = Path.Combine(installDir, "L-Mechrevo.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = installDir });
            }
        }
        catch (Exception ex) { Log("重启失败：" + ex.Message); }
    }

    private static void Log(string text)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "L-Mechrevo-dll-update.log");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }
}
