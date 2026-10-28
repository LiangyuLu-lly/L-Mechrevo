# -*- coding: utf-8 -*-
"""
补丁：诊断包 GCU 版本读取修复
================================================================================
现象（用户多次反馈）：诊断包系统信息里写「GCU 版本: 未知（未找到 GCU 服务文件）」，
即使 GCU 明明在运行。

根因：DiagnosticSystemInfo.ReadGcuVersion() 只探两个路径，都是 **LM 自带**的 GCU 目录：
        {AppContext.BaseDirectory}\GCU\AiStoneService\MyControlCenter\GCUService.exe
        {AppContext.BaseDirectory}\GCU\AiStoneService\GCUBridge.exe
      用官方控制台安装 GCU（或 NoGCU 版 LM）时，{app}\\GCU 整个目录不存在
      → 永远落到 "未知"。实测本机：C:\\Program Files\\L-Mechrevo\\GCU 不存在。

修法：补充「由已注册服务映像路径推导」的候选路径。
      HKLM\\SYSTEM\\CurrentControlSet\\Services\\<GCUService|GCUBridge>\\ImagePath
      官方布局 = C:\\Program Files\\OEM\\机械革命电竞控制台\\UniwillService\\GCUBridge.exe
      同目录下 MyControlCenter\\GCUService.exe 才是带 GCU 构建版本号的那个（本机 1.0.2.70）。
      该键非管理员可读，且已有现成封装 GcuCoexistence.ReadServiceImagePath。

优先级：LM 自带 GCUService → LM 自带 GCUBridge → 服务映像推导：
        <dir>\\MyControlCenter\\GCUService.exe → <dir>\\GCUService.exe → 映像本身 → <dir>\\GCUBridge.exe
        （GCUService.exe 优先于 GCUBridge.exe：版本号更能标识 GCU 构建）
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite.Diagnostics\DiagnosticSystemInfo.cs'

OLD_DOC = '\t/// <summary>从与 LM 同目录的 GCU 服务读取版本（优先 GCUService.exe，其次 GCUBridge.exe）。</summary>\n'
NEW_DOC = ('\t/// <summary>\n'
           '\t/// 读取 GCU 服务版本（优先 GCUService.exe，其次 GCUBridge.exe）。\n'
           '\t/// 2026-10-04：原实现只探 LM 自带目录 {App}\\GCU\\AiStoneService\\...，用官方控制台安装 GCU\n'
           '\t/// （或 NoGCU 版 LM）时该目录不存在，诊断包永远显示"未找到 GCU 服务文件"；\n'
           '\t/// 现补充由服务映像路径推导的候选，见 EnumerateGcuVersionCandidates。\n'
           '\t/// </summary>\n')

OLD_BODY = (
    '\t\t\tstring[] candidates = new string[2]\n'
    '\t\t\t{\n'
    '\t\t\t\tPath.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "MyControlCenter", "GCUService.exe"),\n'
    '\t\t\t\tPath.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "GCUBridge.exe")\n'
    '\t\t\t};\n'
    '\t\t\tforeach (string path in candidates)\n'
    '\t\t\t{\n'
    '\t\t\t\tif (File.Exists(path))\n'
    '\t\t\t\t{\n'
    '\t\t\t\t\tSystem.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);\n'
    '\t\t\t\t\tstring text = versionInfo.ProductVersion ?? versionInfo.FileVersion;\n'
    '\t\t\t\t\tif (!string.IsNullOrWhiteSpace(text))\n'
    '\t\t\t\t\t{\n'
    '\t\t\t\t\t\treturn text + " (" + Path.GetFileName(path) + ")";\n'
    '\t\t\t\t\t}\n'
    '\t\t\t\t\treturn "(无版本信息) " + Path.GetFileName(path);\n'
    '\t\t\t\t}\n'
    '\t\t\t}\n'
    '\t\t\treturn "未知（未找到 GCU 服务文件）";\n')

NEW_BODY = (
    '\t\t\tforeach (string path in EnumerateGcuVersionCandidates())\n'
    '\t\t\t{\n'
    '\t\t\t\tif (!File.Exists(path))\n'
    '\t\t\t\t{\n'
    '\t\t\t\t\tcontinue;\n'
    '\t\t\t\t}\n'
    '\t\t\t\tSystem.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);\n'
    '\t\t\t\tstring text = versionInfo.ProductVersion ?? versionInfo.FileVersion;\n'
    '\t\t\t\tif (!string.IsNullOrWhiteSpace(text))\n'
    '\t\t\t\t{\n'
    '\t\t\t\t\treturn text + " (" + Path.GetFileName(path) + ")";\n'
    '\t\t\t\t}\n'
    '\t\t\t\treturn "(无版本信息) " + Path.GetFileName(path);\n'
    '\t\t\t}\n'
    '\t\t\treturn "未知（未找到 GCU 服务文件）";\n')

ANCHOR_AFTER = '\tprivate static void AppendCapabilities(StringBuilder sb)\n'

NEW_METHODS = '''
	/// <summary>
	/// 按优先级枚举 GCU 服务可执行文件候选路径（已去重）。
	/// 1) LM 自带 GCU 目录（LM 安装器安装的 GCU）；
	/// 2) 由已注册服务映像路径 HKLM\\SYSTEM\\CurrentControlSet\\Services\\&lt;名&gt;\\ImagePath 推导，
	///    官方控制台布局为 &lt;UniwillService&gt;\\MyControlCenter\\GCUService.exe（本机 1.0.2.70）。
	/// GCUService.exe 优先于 GCUBridge.exe —— 前者版本号更能标识 GCU 构建。
	/// </summary>
	private static List<string> EnumerateGcuVersionCandidates()
	{
		List<string> list = new List<string>();
		AddGcuCandidate(list, Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "MyControlCenter", "GCUService.exe"));
		AddGcuCandidate(list, Path.Combine(AppContext.BaseDirectory, "GCU", "AiStoneService", "GCUBridge.exe"));
		string[] serviceNames = new string[2] { "GCUService", "GCUBridge" };
		foreach (string serviceName in serviceNames)
		{
			string exe = null;
			try
			{
				string imagePath = GcuCoexistence.ReadServiceImagePath(serviceName);
				if (!string.IsNullOrWhiteSpace(imagePath))
				{
					exe = imagePath.Trim().Trim('"');
					if (exe.StartsWith(@"\\??\\", StringComparison.Ordinal))
					{
						exe = exe.Substring(4);
					}
					if (!Path.IsPathRooted(exe))
					{
						exe = null;
					}
				}
			}
			catch
			{
				exe = null;
			}
			if (exe == null)
			{
				continue;
			}
			string dir = null;
			try
			{
				dir = Path.GetDirectoryName(exe);
			}
			catch
			{
				dir = null;
			}
			if (!string.IsNullOrWhiteSpace(dir))
			{
				AddGcuCandidate(list, Path.Combine(dir, "MyControlCenter", "GCUService.exe"));
				AddGcuCandidate(list, Path.Combine(dir, "GCUService.exe"));
			}
			AddGcuCandidate(list, exe);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				AddGcuCandidate(list, Path.Combine(dir, "GCUBridge.exe"));
			}
		}
		return list;
	}

	private static void AddGcuCandidate(List<string> list, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		foreach (string item in list)
		{
			if (string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		list.Add(path);
	}

'''

OPS = [
    (F, OLD_DOC, NEW_DOC, 1, '更新 ReadGcuVersion 文档注释'),
    (F, OLD_BODY, NEW_BODY, 1, 'ReadGcuVersion 改用候选枚举'),
    (F, ANCHOR_AFTER, NEW_METHODS + ANCHOR_AFTER, 1, '新增 EnumerateGcuVersionCandidates / AddGcuCandidate'),
]


def main():
    if '--check' in sys.argv:
        print('（仅校验模式）')
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if 'EnumerateGcuVersionCandidates' in text:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = text.count(old)
        if n != expect:
            errors.append('%-30s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        text = text.replace(old, new, expect)
    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(F, 'wb').write(data)
    print('  写入 %s' % F)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
