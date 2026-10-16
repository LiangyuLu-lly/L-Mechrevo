using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>
/// 屏幕色域检测：优先解析系统校色 ICC（spool\drivers\color）中的 RGB 原色 XYZ，
/// 以 sRGB 三角形面积对比判定屏幕色域。仅作"档位数无法从 GCU 上报取得"时的兜底：
/// 真正的档位数口径是 GCU `Customize/Info` 的 `SupportColorCalibration`
/// （见 ColorCalibrationSwitchPolicy.ResolveLevel）。用户口径：
/// 40 系 + sRGB 屏只有「校准 / 不校准」二分；50 系广色域屏 2 档（default/sRGB）、P3 屏 4 档。
/// 解析失败或找不到匹配 ICC 时返回 null（调用方按现状处理，不破坏原有行为）。
/// </summary>
internal static class DisplayColorGamut
{
	private const double SrgbThresholdRatio = 1.2;
	private static readonly object CacheLock = new();
	private static bool _resolved;
	private static bool? _cached;

	/// <summary>true = sRGB 屏（二分校色）；false = 广色域屏；null = 无法判断。</summary>
	internal static bool? IsSrgbDisplay
	{
		get
		{
			lock (CacheLock)
			{
				if (!_resolved)
				{
					_cached = DetectFromIcc();
					_resolved = true;
				}
				return _cached;
			}
		}
	}

	private static bool? DetectFromIcc()
	{
		try
		{
			string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			string dir = Path.Combine(windows, "System32", "spool", "drivers", "color");
			if (!Directory.Exists(dir))
			{
				return null;
			}
			string[] prefixes = EnumerateDisplayPrefixes();
			if (prefixes.Length == 0)
			{
				return null;
			}
			string? icc = null;
			foreach (string file in Directory.GetFiles(dir, "*.ic*"))
			{
				string name = Path.GetFileNameWithoutExtension(file);
				foreach (string prefix in prefixes)
				{
					if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						icc = file;
						break;
					}
				}
				if (icc != null)
				{
					break;
				}
			}
			if (icc == null)
			{
				return null;
			}
			return ParseIccGamut(icc);
		}
		catch
		{
			return null;
		}
	}

	private static string[] EnumerateDisplayPrefixes()
	{
		List<string> list = new List<string>();
		try
		{
			using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
			{
				if (key != null)
				{
					foreach (string name in key.GetSubKeyNames())
					{
						// 形如 BOE0CBE（厂商3字母+产品4位），不含 & 的实例节点
						if (name.Length >= 7 && name.IndexOf('&') < 0)
						{
							list.Add(name);
						}
					}
				}
			}
		}
		catch
		{
			// 忽略，返回已收集的
		}
		return list.ToArray();
	}

	private static bool? ParseIccGamut(string path)
	{
		byte[] data = File.ReadAllBytes(path);
		if (data.Length < 132 || data[36] != (byte)'a' || data[37] != (byte)'c' || data[38] != (byte)'s' || data[39] != (byte)'p')
		{
			return null;
		}
		int count = BigEndian32(data, 128);
		double[]? r = null, g = null, b = null;
		for (int i = 0; i < count && i < 64; i++)
		{
			int off = 132 + 12 * i;
			if (off + 12 > data.Length)
			{
				break;
			}
			string sig = Encoding.ASCII.GetString(data, off, 4);
			int toff = BigEndian32(data, off + 4);
			int tsize = BigEndian32(data, off + 8);
			if (tsize < 20 || toff + 20 > data.Length)
			{
				continue;
			}
			double[]? xyz = ReadXyz(data, toff + 8);
			if (xyz == null)
			{
				continue;
			}
			if (sig == "rXYZ")
			{
				r = xyz;
			}
			else if (sig == "gXYZ")
			{
				g = xyz;
			}
			else if (sig == "bXYZ")
			{
				b = xyz;
			}
		}
		if (r == null || g == null || b == null)
		{
			return null;
		}
		// 合理性校验：xy 坐标应在 0.05-0.95 之间，防止垃圾数据误判
		foreach (double[] c in new[] { r, g, b })
		{
			foreach (double v in c)
			{
				if (v < 0.05 || v > 0.95)
				{
					return null;
				}
			}
		}
		double area = Area(r[0], r[1], g[0], g[1], b[0], b[1]);
		double areaSrgb = Area(0.640, 0.330, 0.300, 0.600, 0.150, 0.060);
		if (area <= 0.0 || areaSrgb <= 0.0)
		{
			return null;
		}
		return area / areaSrgb < SrgbThresholdRatio;
	}

	private static double[]? ReadXyz(byte[] data, int off)
	{
		if (off + 12 > data.Length)
		{
			return null;
		}
		double x = S15F16(data, off);
		double y = S15F16(data, off + 4);
		double z = S15F16(data, off + 8);
		double sum = x + y + z;
		if (sum <= 0.0)
		{
			return null;
		}
		return new[] { x / sum, y / sum };
	}

	private static double S15F16(byte[] data, int off)
	{
		int value = (data[off] << 24) | (data[off + 1] << 16) | (data[off + 2] << 8) | data[off + 3];
		return value / 65536.0;
	}

	private static int BigEndian32(byte[] data, int off)
	{
		return (data[off] << 24) | (data[off + 1] << 16) | (data[off + 2] << 8) | data[off + 3];
	}

	private static double Area(double rx, double ry, double gx, double gy, double bx, double by)
	{
		return Math.Abs((rx * (gy - by) + gx * (by - ry) + bx * (ry - gy)) / 2.0);
	}

}
