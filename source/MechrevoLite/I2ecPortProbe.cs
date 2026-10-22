using System;
using System.IO;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite;

/// <summary>
/// 实验探针：检测 PawnIO LpcIO 模块是否放行 I2EC 端口（0x681/0x682/0x683）。
/// <para>只读优先；仅当端口读写都放行时，才做一次“地址选择 + 读数据”的只读序列。</para>
/// <para>结果写桌面 lm-i2ec-port-probe-&lt;ts&gt;.txt；退出码 0=有端口可读 / 2=全部被拒 / 1=异常。</para>
/// </summary>
internal static class I2ecPortProbe
{
	private static readonly ushort[] Ports = { 0x681, 0x682, 0x683, 0x680, 0x684 };

	internal static int Run(bool allowWriteProbe = false)
	{
		StringBuilder sb = new StringBuilder();
		try
		{
			sb.AppendLine("=== I2EC 端口探针（只读） ===");
			sb.AppendLine("主机：" + Environment.MachineName + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			sb.AppendLine("PawnIO：" + EcLpcIo.DescribePawnInstalled());
			sb.AppendLine("LPC 通道：" + EcLpcIo.DescribeChannel());
			sb.AppendLine("目标：0x681=地址高 / 0x682=地址低 / 0x683=数据（IT5571 I2EC）");
			sb.AppendLine();

			bool anyReadOk = false;
			foreach (ushort port in Ports)
			{
				bool ok = EcLpcIo.TryRawPortRead(port, out byte value, out string note);
				anyReadOk |= ok;
				sb.AppendLine(string.Format("port 0x{0:X3} 读：{1} · {2}", port, ok ? ("0x" + value.ToString("X2")) : "读不到", note));
			}
			sb.AppendLine();

			bool writeProbeOk = false;
			if (!allowWriteProbe)
			{
				sb.AppendLine("写探测：未启用（默认只读；需要时加 --allow-write-probe）");
				sb.AppendLine();
			}
			else
			{
				foreach (ushort port in new ushort[] { 0x681, 0x682 })
				{
					bool ok = EcLpcIo.TryRawPortWrite(port, 0x00, out string note);
					writeProbeOk |= ok;
					sb.AppendLine(string.Format("port 0x{0:X3} 写探测（写 0x00）：{1}", port, ok ? ("放行 · " + note) : ("被拒 · " + note)));
				}
				sb.AppendLine();
			}

			if (anyReadOk && writeProbeOk)
			{
				sb.AppendLine("== 端口放行 ⇒ 尝试只读序列（select 地址 → 读数据；不写数据端口）==");
				foreach (int address in new[] { 0x200D, 0x0454, 0x030E, 0x0522, 0x0836 })
				{
					bool selHi = EcLpcIo.TryRawPortWrite(0x681, (byte)(address >> 8), out string noteHi);
					bool selLo = false;
					string noteLo = "";
					if (selHi)
					{
						selLo = EcLpcIo.TryRawPortWrite(0x682, (byte)(address & 0xFF), out noteLo);
					}
					if (!selHi || !selLo)
					{
						sb.AppendLine(string.Format("  0x{0:X4}：地址选择失败（{1} / {2}）", address, noteHi, noteLo));
						continue;
					}
					bool rd = EcLpcIo.TryRawPortRead(0x683, out byte data, out string noteRd);
					sb.AppendLine(string.Format("  0x{0:X4} = {1} · {2}", address, rd ? ("0x" + data.ToString("X2")) : "读不到", noteRd));
				}
			}
			else
			{
				sb.AppendLine("== 结论 ==");
				sb.AppendLine(anyReadOk
					? "读放行、写被拒：只能做探测，不能按 I2EC 协议选地址。"
					: "端口全部被 LpcIO 模块拒绝 ⇒ 需要新的 PawnIO 模块。");
			}

			string path = WriteReportFile(sb.ToString());
			sb.AppendLine();
			sb.AppendLine("报告文件：" + path);
			Console.WriteLine(sb.ToString());
			Logger.WriteLine("[i2ec-probe] 完成，报告=" + path);
			return anyReadOk ? 0 : 2;
		}
		catch (Exception ex)
		{
			sb.AppendLine("异常：" + ex.GetType().Name + " · " + ex.Message);
			string path = WriteReportFile(sb.ToString());
			Console.WriteLine(sb.ToString());
			Logger.WriteLine("[i2ec-probe] 异常（报告已写 " + path + "）：" + ex.Message);
			return 1;
		}
		finally
		{
			Logger.Close();
		}
	}

	private static string WriteReportFile(string text)
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"lm-i2ec-port-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
			File.WriteAllText(path, text, Encoding.UTF8);
			return path;
		}
		catch
		{
			return "(写桌面失败)";
		}
	}
}
