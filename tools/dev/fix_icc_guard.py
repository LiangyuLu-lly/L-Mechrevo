# -*- coding: utf-8 -*-
# ICC 应用防御：异常时恢复 UI + 记录日志
import io

def edit(path, old, new, tag):
    t = io.open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# 1) SettingsForm 999 分支：try-catch 包裹，异常记日志并回退
edit(r'D:\openLM\source\MechrevoLite\SettingsForm.cs',
'if (value == 999)\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏\n\t\t\t\t\t\t\tstring? iccName = AppConfig.GetString("calib_custom_icc");\n\t\t\t\t\t\t\tstring iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);\n\t\t\t\t\t\t\tflag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));\n\t\t\t\t\t\t}',
'if (value == 999)\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t// 自定义 ICC：不经由 GCU，直接经 Windows 颜色管理关联到内屏\n\t\t\t\t\t\t\tstring? iccName = AppConfig.GetString("calib_custom_icc");\n\t\t\t\t\t\t\tstring iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);\n\t\t\t\t\t\t\ttry\n\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\tflag4 = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));\n\t\t\t\t\t\t\t}\n\t\t\t\t\t\t\tcatch (Exception ex)\n\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\tLogger.WriteLine("自定义 ICC 应用异常: " + ex.Message);\n\t\t\t\t\t\t\t\tflag4 = false;\n\t\t\t\t\t\t\t}\n\t\t\t\t\t\t}',
'settings-999-try')

# 2) IccHelper.ApplyIcc 整体 try-catch
edit(r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs',
'public static bool ApplyIcc(string profilePath)\n\t{\n\t\tstring? device = ScreenNative.FindLaptopScreen(log: true);',
'public static bool ApplyIcc(string profilePath)\n\t{\n\t\ttry\n\t\t{\n\t\treturn ApplyIccCore(profilePath);\n\t\t}\n\t\tcatch (Exception ex)\n\t\t{\n\t\t\tLogger.WriteLine("自定义 ICC 应用异常: " + ex.Message);\n\t\t\treturn false;\n\t\t}\n\t}\n\n\tprivate static bool ApplyIccCore(string profilePath)\n\t{\n\t\tstring? device = ScreenNative.FindLaptopScreen(log: true);',
'icc-apply-try')

# 3) ClearAllIcc 整体 try-catch（含设备获取）
edit(r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs',
'public static void ClearAllIcc()\n\t{\n\t\tstring? device = ScreenNative.FindLaptopScreen(log: true);',
'public static void ClearAllIcc()\n\t{\n\t\ttry\n\t\t{\n\t\t\tClearAllIccCore();\n\t\t}\n\t\tcatch (Exception ex)\n\t\t{\n\t\t\tLogger.WriteLine("取消自定义 ICC 异常: " + ex.Message);\n\t\t}\n\t}\n\n\tprivate static void ClearAllIccCore()\n\t{\n\t\tstring? device = ScreenNative.FindLaptopScreen(log: true);',
'icc-clear-try')

print('ALL OK')
