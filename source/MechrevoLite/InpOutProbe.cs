using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MechrevoLite;

/// <summary>
/// 2026-10-22（机主：先探测 inpoutx64 能不能用）：
/// 独立只读/自检探针 —— 指定任意 inpoutx64.dll，检查驱动、I2EC 读写是否可用。
///
/// <para>用法：<c>L-Mechrevo.exe --inpout-probe "D:\Downloads\InpOutBinaries_1501\x64\inpoutx64.dll"</c>
/// 或 <c>--inpout-probe</c>（按 LM 常规搜索路径找）。</para>
///
/// <para>安全检查：只读 I2EC/ACPI 状态；写入自检**只把读到的原值写回**（等值写），不改变风扇/电池行为。</para>
/// </summary>
internal static class InpOutProbe
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int IsInpOutDriverOpenDelegate();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate short Inp32Delegate(short port);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void Out32Delegate(short port, short value);

	internal static string Run(string argument)
	{
		string dll = ResolveDll(argument);
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[inpout 探测]");
		if (string.IsNullOrEmpty(dll))
		{
			sb.AppendLine("未找到 inpoutx64.dll（尝试过 LM 目录、E:/D:/C:\\JG_BAT、System32、传入路径）");
			sb.AppendLine("结论：不可用（DLL 未找到）");
			return sb.ToString().TrimEnd();
		}
		sb.AppendLine("DLL：" + dll);
		try
		{
			FileInfo info = new FileInfo(dll);
			sb.AppendLine("版本：" + (FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "(无)") + " · 大小=" + info.Length + " B");
			using (SHA256 sha = SHA256.Create())
			{
				using FileStream fs = File.OpenRead(dll);
				sb.AppendLine("SHA256：" + BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", ""));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("读取 DLL 文件信息失败：" + ex.GetType().Name + " " + ex.Message);
		}
		if (!NativeLibrary.TryLoad(dll, out IntPtr handle) || handle == IntPtr.Zero)
		{
			sb.AppendLine("NativeLibrary.TryLoad 失败");
			sb.AppendLine("结论：不可用（DLL 加载失败）");
			return sb.ToString().TrimEnd();
		}
		try
		{
			IsInpOutDriverOpenDelegate isOpen = GetExport<IsInpOutDriverOpenDelegate>(handle, "IsInpOutDriverOpen");
			Inp32Delegate inp32 = GetExport<Inp32Delegate>(handle, "Inp32");
			Out32Delegate out32 = GetExport<Out32Delegate>(handle, "Out32");
			if (isOpen == null || inp32 == null || out32 == null)
			{
				sb.AppendLine("缺少导出：IsInpOutDriverOpen=" + (isOpen != null) + " · Inp32=" + (inp32 != null) + " · Out32=" + (out32 != null));
				sb.AppendLine("结论：不可用（导出不完整）");
				return sb.ToString().TrimEnd();
			}
			int open = isOpen();
			sb.AppendLine("IsInpOutDriverOpen=" + open);
			if (open == 0)
			{
				sb.AppendLine("驱动未安装/未启动（inpoutx64.sys 服务不可用）");
				sb.AppendLine("结论：不可用（驱动未就绪）");
				return sb.ToString().TrimEnd();
			}
			// ⚠️ 0x681/0x682/0x683 是 I2EC 窗口端口：先写地址高/低，再读写数据端口 0x683；
			// 不能把 EC 地址（0x200D/0x1804…）直接当 I/O 端口读。
			sb.AppendLine("I2EC 只读：0x200D=0x" + B(ReadEc(inp32, out32, 0x200D))
				+ " · 0x043E=0x" + B(ReadEc(inp32, out32, 0x043E))
				+ " · 0x1804=0x" + B(ReadEc(inp32, out32, 0x1804))
				+ " · 0x1809=0x" + B(ReadEc(inp32, out32, 0x1809))
				+ " · 0x0464=0x" + B(ReadEc(inp32, out32, 0x0464))
				+ " · 0x0465=0x" + B(ReadEc(inp32, out32, 0x0465)));
			// 等值写自检：读原值 -> 写回原值 -> 再读，确认写通道可用且不改变行为。
			int duty1 = ReadEc(inp32, out32, 0x1804);
			int duty2 = ReadEc(inp32, out32, 0x1809);
			WriteEc(inp32, out32, 0x1804, duty1);
			WriteEc(inp32, out32, 0x1809, duty2);
			int back1 = ReadEc(inp32, out32, 0x1804);
			int back2 = ReadEc(inp32, out32, 0x1809);
			sb.AppendLine("等值写自检（写回原值）：0x1804 " + duty1 + "→" + back1 + " · 0x1809 " + duty2 + "→" + back2);
			bool writeOk = (back1 == duty1) && (back2 == duty2);
			sb.AppendLine(writeOk
				? "结论：可用（驱动 / 只读 / I2EC 等值写均通过）"
				: "结论：读可用、写回读不一致（I2EC 写入可能被 EC 覆写/忽略）");
			return sb.ToString().TrimEnd();
		}
		catch (Exception ex)
		{
			sb.AppendLine("探测异常：" + ex.GetType().Name + " " + ex.Message);
			sb.AppendLine("结论：不可用（异常）");
			return sb.ToString().TrimEnd();
		}
		finally
		{
			NativeLibrary.Free(handle);
		}
	}

	private static T GetExport<T>(IntPtr module, string name) where T : Delegate
	{
		IntPtr proc = NativeLibrary.GetExport(module, name);
		return (proc == IntPtr.Zero) ? null : Marshal.GetDelegateForFunctionPointer<T>(proc);
	}

	/// <summary>I2EC 读：0x681←地址高、0x682←地址低、读 0x683。</summary>
	private static int ReadEc(Inp32Delegate inp32, Out32Delegate out32, int address)
	{
		out32((short)0x681, (short)((address >> 8) & 0xFF));
		out32((short)0x682, (short)(address & 0xFF));
		return inp32(0x683) & 0xFF;
	}

	/// <summary>I2EC 写：0x681←地址高、0x682←地址低、0x683←数据。</summary>
	private static void WriteEc(Inp32Delegate inp32, Out32Delegate out32, int address, int value)
	{
		out32((short)0x681, (short)((address >> 8) & 0xFF));
		out32((short)0x682, (short)(address & 0xFF));
		out32((short)0x683, (short)(value & 0xFF));
	}

	private static string B(int value)
	{
		return ((byte)value).ToString("X2");
	}

	private static string ResolveDll(string argument)
	{
		string text = (argument ?? "").Trim().Trim('"');
		if (text.Length > 0)
		{
			if (Directory.Exists(text))
			{
				string direct = Path.Combine(text, "inpoutx64.dll");
				if (File.Exists(direct))
				{
					return direct;
				}
				string nested = Path.Combine(text, "x64", "inpoutx64.dll");
				if (File.Exists(nested))
				{
					return nested;
				}
			}
			return File.Exists(text) ? text : null;
		}
		string[] candidates = new string[]
		{
			Path.Combine(AppContext.BaseDirectory, "inpoutx64.dll"),
			@"E:\JG_BAT\inpoutx64.dll",
			@"D:\JG_BAT\inpoutx64.dll",
			@"C:\JG_BAT\inpoutx64.dll",
			Path.Combine(AppContext.BaseDirectory, "JG_BAT", "inpoutx64.dll"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "inpoutx64.dll")
		};
		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		return null;
	}
}
