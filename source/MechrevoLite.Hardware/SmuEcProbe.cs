// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **SMU-EC 通道只读核对** `--smu-ec-probe` ⭐⭐
//
// ⭐⭐ 背景：机主/负责人看到 7.3 的 `SMUCommands.RyzenAccess.Write_EC_EX / Read_EC_EX / Write_EC_Init`
//    出现在 `Fan_Speed` 里，怀疑还有一条 **SMU 转发 EC** 的通道（我们没试过）。
//    ⭐ 本轮先做**只读核对**：对照 7.3 官方组件行为 看 `RyzenAccess` 到底走什么；再用我们已有的
//      `RyzenSmuService`（PawnIO RyzenSMU）确认它有没有 EC 命令可发。
//
// ⭐ 官方组件行为事实（`内部参考材料（路径略）`）：
//    · `EC.RW.ECRW_Tool.Fan_Speed`（约 931-947 行）确实调用
//      `SMUCommands.RyzenAccess.Write_EC_EX(51260/51261, speed)` 与 `Read_EC_EX(2848)`；
//    · 但 `RyzenAccess.Write_EC_EX / Read_EC_EX`（约 1567/1588 行）的实现是
//      **`RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT=0x4E, ...)` / `(EC_DATA_PORT=0x4F, ...)`**，
//      即 **LPC 端口 I/O**（`0x2E`/`0x2F` 命令 + `0x11/0x10/0x12` 索引），
//      **不是** SMU 邮箱命令；`Write_EC_Init`（约 1555 行）= 读 `0x2000/0x2001`，0x55 则读 `0x1060` 写 `|0x80`；
//    · 同一文件的 SMU 邮箱命令表（`SMUCommands.commands`，约 999 行起）只有 CPU 侧命令
//      （Curve/Clock/Stapm/Fast/Slow/Thm/CoGfx…），**没有 EC 读/写命令**。
//    ⇒ ⭐ 结论（待机主真机确认其它线索前）：**不存在“SMU 转发 EC”的命令号**；
//      7.3 的风扇 EC 通道就是我们已经实现/尝试的 LPC 端口 I/O（写被 EC 拒绝），
//      所以 `RyzenSmuService` 没有可发送的 EC 命令，⛔ 不猜测命令号、⛔ 不向 SMU 乱发。
//
// ⭐ 本命令**只读**：
//    1. 初始化 `RyzenSmuService`（只读版本/代号，不发命令）；
//    2. 打印“命令表里没有 EC 命令”的核对结论（命令来源见下）；
//    3. LPC（`EcLpcIo`）与 ACPI（`EcPortIo`）双通道稳定读同一批 EC 地址并逐项对照；
//    4. 结论：SMU-EC 通道不可用 / 无命令；写通路仍需 ACPI EC 或 LPC（已试过，均被拒）。
//
// ⛔ 不写任何 EC/SMU 字节；⛔ 不打包、不 kill LM。
// ⭐ 输出：桌面 `lm-smu-ec-probe-<时间戳>.txt` + `log.txt`（前缀 `[smu-ec-probe]`）。
// ⭐ 用法：`L-Mechrevo.exe --smu-ec-probe`（需管理员）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Reflection;
using System.Text;
using PawnIO;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ SMU-EC 通道**只读**核对（<c>--smu-ec-probe</c>）：命令来源 + 双通道读数对照 ✅。</summary>
internal static class SmuEcProbe
{
	internal static int Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-smu-ec-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-smu-ec-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch
		{
			return 1;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[smu-ec-probe] " + s); } catch { }
		}

		int exitCode = 1;
		try
		{
			L("=== SMU-EC 通道只读核对（--smu-ec-probe）===");
			L("⛔ 本命令只读：不发 SMU 命令、不写任何 EC 字节");
			L("");

			// ── 1. RyzenSmuService 初始化（只读）──
			L("── 1. RyzenSmuService（PawnIO RyzenSMU）──");
			RyzenSmuService smu = null;
			try
			{
				smu = new RyzenSmuService();
				bool init = smu.Initialize(Assembly.GetExecutingAssembly());
				L("SMU 初始化 = " + init);
				if (init)
				{
					exitCode = 0;
					L("CPU 代号 = " + smu.CpuCodeName + " · 家族 = " + smu.Family + " · SMU 版本 = 0x" + smu.SmuVersion.ToString("X"));
					L("⭐ 本服务**只实现 CPU 侧命令**（Curve/CurvePerCore/Clock/Stapm/Fast/Slow/Thm/CoGfx…），没有 EC 读/写命令；");
					L("   ⇒ ⛔ 没有可发送的“SMU-EC”命令号（不猜测、不乱发）");
				}
				else
				{
					L("⚠️ SMU 初始化失败 ⇒ 无法核对（可能非 AMD 或 PawnIO/RyzenSMU 不可用）");
				}
			}
			catch (Exception ex)
			{
				L("SMU 初始化异常：" + ex.GetType().Name + " " + ex.Message);
			}
			finally
			{
				try
				{
					smu?.Dispose();
				}
				catch
				{
				}
			}
			L("");

			// ── 2. 命令来源核对（对照 7.3 官方组件行为）──
			L("── 2. 命令来源核对（7.3 官方组件行为）──");
			L("文件：_upstream\\内部参考材料\\thirdparty-JiaoLong7.3\\第三方蛟龙游戏控制中心.参考材料.cs");
			L("  · EC.RW.ECRW_Tool.Fan_Speed（约 931-947 行）调用 SMUCommands.RyzenAccess.Write_EC_EX(51260/51261, speed)、Read_EC_EX(2848)");
			L("  · RyzenAccess.Write_EC_EX / Read_EC_EX（约 1567/1588 行）实现 = RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT=0x4E / EC_DATA_PORT=0x4F)");
			L("    ⇒ 这是 **LPC 端口 I/O**（0x2E/0x2F 命令 + 0x11/0x10/0x12 索引），**不是 SMU 邮箱命令**");
			L("  · RyzenAccess.Write_EC_Init（约 1555 行）= 读 0x2000/0x2001；0x55 则读 0x1060 写 |0x80（≈ 我们的解锁逻辑）");
			L("  · SMUCommands.commands（约 999 行起）只有 CPU 侧命令（Curve/Clock/Stapm/Fast/Slow/Thm/CoGfx…），**没有 EC 命令**");
			L("  ⇒ ⭐ 结论：7.3 的“EC_EX”是端口 I/O 通道，**没有 SMU 命令号可抄**；");
			L("    我们在 LPC 上已经按同一时序试过（读成功、写被 EC 拒绝），SMU 并不会多出一条 EC 通路 ⚠️");
			L("");

			// ── 3. 双通道只读对照（LPC vs ACPI）──
			L("── 3. 双通道只读对照（LPC EcLpcIo vs ACPI EcPortIo，稳定读 3 次取多数）──");
			int[] addresses = { 0x2000, 0x1060, 0xC411, 0xC83C, 0xC83D, 0xB20, 0xC834, 0xC835 };
			string[] names =
			{
				"0x2000 芯片 ID（期望 0x55）",
				"0x1060 解锁寄存器",
				"0xC411 EC 版本",
				"0xC83C Fan1 目标",
				"0xC83D Fan2 目标",
				"0xB20 接管掩码",
				"0xC834 Fan1 转速字节",
				"0xC835 Fan2 转速字节"
			};
			bool allKnownMatch = true;
			for (int i = 0; i < addresses.Length; i++)
			{
				bool lpcOk = EcLpcIo.TryReadByteStable(addresses[i], out int lpcValue, out string lpcNote);
				bool acpiOk = EcPortIo.TryReadByteStable(addresses[i], out int acpiValue, out string acpiNote);
				bool match = lpcOk && acpiOk && lpcValue == acpiValue;
				if (!match)
				{
					allKnownMatch = false;
				}
				L("  " + names[i] + "：");
				L("    LPC  = " + (lpcOk ? "0x" + lpcValue.ToString("X2") : "读不到") + "（" + lpcNote + "）");
				L("    ACPI = " + (acpiOk ? "0x" + acpiValue.ToString("X2") : "读不到") + "（" + acpiNote + "）");
				L("    一致 = " + match);
			}
			L("");
			L(allKnownMatch
				? "  ⇒ ⭐ 两条通道读数**逐项一致**（EC 可读；写通路问题不在读侧）"
				: "  ⇒ ⚠️ 两条通道读数存在不一致（见上；可能某通道读失败/时序抖动）");
			L("");

			// ── 4. 结论 ──
			L("── 4. 结论 ──");
			L("⛔ SMU-EC 通道**不存在**：7.3 的 Write_EC_EX/Read_EC_EX 是 LPC 端口 I/O，不是 SMU 命令；");
			L("   RyzenSmuService 的命令表里也没有 EC 读/写命令 ⇒ 没有安全的命令号可发（⛔ 不猜测、⛔ 不向 SMU 乱发）。");
			L("⭐ 写通路仍只有两条：LPC（写被 EC 拒绝）· ACPI EC（0x93 写被拒 / 0x81 实验待真机）；");
			L("   若两条都无效 ⇒ 判定「本机 EC RAM 写被 BIOS 锁死」⇒ 放弃风扇曲线/强冷（保持隐藏）。");
		}
		catch (Exception ex)
		{
			L("探针异常：" + ex.GetType().Name + " " + ex.Message);
		}
		return exitCode;
	}
}
