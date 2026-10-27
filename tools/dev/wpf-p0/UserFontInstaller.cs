using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace L_Mechrevo.WpfP0;

/// <summary>
/// 把随程序附带的字体注册到当前用户字体目录：
/// - 不写 C:\Windows\Fonts，不需要管理员权限；
/// - 字体出现在“设置 → 个性化 → 字体”列表；
/// - 失败时由调用方回退到文件加载。
/// </summary>
internal static class UserFontInstaller
{
    private const string FontFamilyName = "HarmonyOS Sans SC";
    private const string RegularFileName = "HarmonyOS_Sans_SC_Regular.ttf";
    private const string BoldFileName = "HarmonyOS_Sans_SC_Bold.ttf";
    private const string FontsKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    private const uint WM_FONTCHANGE = 0x001D;
    private const uint HWND_BROADCAST = 0xFFFF;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    internal static bool EnsureInstalled(string sourceRegular, string sourceBold)
    {
        try
        {
            if (IsFamilyAvailable())
            {
                return true;
            }
            if (!File.Exists(sourceRegular) || !File.Exists(sourceBold))
            {
                return false;
            }

            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            Directory.CreateDirectory(targetDir);

            string targetRegular = Path.Combine(targetDir, RegularFileName);
            string targetBold = Path.Combine(targetDir, BoldFileName);

            CopyIfNeeded(sourceRegular, targetRegular);
            CopyIfNeeded(sourceBold, targetBold);

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(FontsKeyPath);
            RegisterValue(key, "HarmonyOS Sans SC Regular (TrueType)", targetRegular);
            RegisterValue(key, "HarmonyOS Sans SC Bold (TrueType)", targetBold);

            AddFontResource(targetRegular);
            AddFontResource(targetBold);
            SendMessageTimeout(
                (nint)HWND_BROADCAST, WM_FONTCHANGE, 0, 0,
                SMTO_ABORTIFHUNG, 2000, out _);

            return IsFamilyAvailableInCurrentProcess();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFamilyAvailable()
    {
        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            if (family.Source.IndexOf(FontFamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsFamilyAvailableInCurrentProcess()
    {
        try
        {
            FontFamily family = new FontFamily(FontFamilyName);
            return family.GetTypefaces().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyIfNeeded(string source, string target)
    {
        if (File.Exists(target))
        {
            return;
        }
        File.Copy(source, target, overwrite: false);
    }

    private static void RegisterValue(RegistryKey key, string name, string path)
    {
        if (key.GetValue(name) is string existing
            && string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        key.SetValue(name, path, RegistryValueKind.String);
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResource(string file);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam,
        uint flags, uint timeout, out nint result);
}
