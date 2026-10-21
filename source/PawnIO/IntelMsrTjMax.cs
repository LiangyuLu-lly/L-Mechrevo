using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PawnIO;

/// <summary>
/// 读取 Intel CPU 的 TjMax（MSR 0x1A2 MSR_TEMPERATURE_TARGET，bits 23:16）。
/// <para>只读；模块 = 内嵌 LMechrevo.IntelMSR.bin（PawnIO.Modules，LGPL-2.1-or-later）。</para>
/// <para>失败返回 0（不影响调用方；有 GCU 上报的 TjMax 时优先用 GCU 的）。</para>
/// </summary>
internal static class IntelMsrTjMax
{
	private const uint MsrTemperatureTarget = 0x1A2u;

	private static readonly object Gate = new object();

	private static bool _attempted;

	private static int _value;

	internal static int TryRead()
	{
		lock (Gate)
		{
			if (_attempted)
			{
				return _value;
			}
			_attempted = true;
			try
			{
				using PawnIOWrapper pawn = new PawnIOWrapper();
				using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LMechrevo.IntelMSR.bin");
				if (stream == null)
				{
					return 0;
				}
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				if (!pawn.LoadModule(memoryStream.ToArray()))
				{
					return 0;
				}
				ulong[] output = new ulong[1];
				if (!pawn.Execute("ioctl_read_msr", new ulong[1] { MsrTemperatureTarget }, output))
				{
					return 0;
				}
				int tj = (int)((output[0] >> 16) & 0xFF);
				if (tj >= 40 && tj <= 125)
				{
					_value = tj;
				}
			}
			catch
			{
			}
			return _value;
		}
	}
}
