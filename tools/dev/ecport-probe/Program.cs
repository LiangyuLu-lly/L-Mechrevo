// L-Mechrevo — EC 端口 I/O 只读探针（本地验证用 · 非生产代码）
//
// 目的：在不以管理员运行、也不跑整个 LM 应用的前提下，验证
//   PawnIOWrapper.Connect() → LoadModule(LpcACPIEC) → 端口 I/O → EC mailbox 读
// 这条链在**本机**是否工作。
//
// ⚠️ 只读：只发 0x92（读）命令，⛔ 绝不发 0x93（写）、⛔ 不改任何寄存器。
// ⚠️ 它 link 的是**生产源文件本体**（EcPortIo.cs / PawnIOWrapper.cs），不是副本 ⇒ 验证的就是真代码。
//
// 用法：dotnet run --project tools\dev\ecport-probe

using System;
using System.Text;
using MechrevoLite.Hardware;

internal static class Program
{
	private static int Main()
	{
		try
		{
			Console.OutputEncoding = Encoding.UTF8;
		}
		catch (Exception)
		{
		}

		Console.WriteLine("=== EC 端口 I/O 只读探针（LpcACPIEC · 端口 0x62/0x66）===");
		Console.WriteLine("只读声明：只发 0x92 读命令 · 绝不发 0x93 写 · 不改任何寄存器");
		Console.WriteLine();

		Console.WriteLine("PawnIO 驱动   = " + EcPortIo.DescribePawnInstalled());

		// ⭐ 2026-10-16 修正：**先读交叉校验、再读版本号** —— 与 ②d 的诊断口径一致。
		//   理由：版本号可能是任意数据，而 SOC 必须 0~100、模式必须 0/2/0x48；
		//   这两个才是「地址映射对不对」的判据 ⇒ ⭐ 最有价值、必须最先拿到 ✅
		Console.WriteLine();
		Console.WriteLine("交叉校验（先读 · 判断是不是真的在跟一颗 IT5570 对话）：");
		int soc;
		bool socOk = EcPortIo.TryReadByte(0x847C, out soc);
		Console.WriteLine("  SOC  0x847C = " + (socOk
			? (soc + ((soc >= 0 && soc <= 100) ? "  ✅ 在合理区间" : "  ⚠️ 超出合理区间"))
			: ("⛔ 读不到（" + EcPortIo.LastReadFailure + "）")));
		int mode;
		bool modeOk = EcPortIo.TryReadByte(0x83C0, out mode);
		Console.WriteLine("  模式 0x83C0 = " + (modeOk
			? ("0x" + mode.ToString("X2"))
			: ("⛔ 读不到（" + EcPortIo.LastReadFailure + "）")));
		bool mappingConfirmed = socOk && soc >= 0 && soc <= 100 && modeOk && (mode == 0 || mode == 2 || mode == 0x48);
		Console.WriteLine("  地址映射判据 = " + (mappingConfirmed
			? "✅ 已确认（SOC 与模式双双落在合理值内）"
			: "⚠️ 未确认 ⇒ ⭐ 下面的版本号不能当真"));

		Console.WriteLine();
		Console.WriteLine("EC 版本 4 字节（XRAM 0x8410~0x8413）：");
		int[] version = new int[4];
		bool versionOk = EcPortIo.TryReadBytes(0x8410, 4, out version);
		for (int i = 0; i < version.Length; i++)
		{
			Console.WriteLine("  0x" + (0x8410 + i).ToString("X4") + " = "
				+ ((version[i] >= 0) ? ("0x" + version[i].ToString("X2")) : "⛔ 读不到"));
		}
		if (!versionOk)
		{
			Console.WriteLine("  （最后一次读失败原因：" + EcPortIo.LastReadFailure + "）");
		}

		Console.WriteLine();
		Console.WriteLine("通道状态 = " + EcPortIo.DescribeChannel());
		int lastStatus = EcPortIo.LastStatusSeen;
		if (lastStatus >= 0)
		{
			Console.WriteLine("  最近 0x66 状态字节 = 0x" + lastStatus.ToString("X2")
				+ "（bit0=OBF 有数据可读 · bit1=IBF EC 未取走写入）");
		}
		Console.WriteLine();

		int okCount = 0;
		int ffCount = 0;
		foreach (int v in version)
		{
			if (v >= 0)
			{
				okCount++;
				if (v == 0xFF)
				{
					ffCount++;
				}
			}
		}

		if (okCount == 4 && ffCount < 4 && mappingConfirmed)
		{
			Console.WriteLine("结论 = ✅ EC 版本可读（端口通道有效 · 地址映射已确认）");
			Console.WriteLine("       ⇒ 机制通了：Connect → LoadModule → 端口 I/O → mailbox 读 全部工作");
		}
		else if (okCount == 4 && ffCount == 4)
		{
			Console.WriteLine("结论 = ⚠️ 端口通道通了、但 4 字节全 0xFF");
			Console.WriteLine("       ⇒ 机制本身工作（无超时、未中毒），但该地址在本机未映射");
			Console.WriteLine("       ⇒ 本机 EC 不是 IT5570（蛟龙才是）⇒ 对本机而言这是正常结果");
		}
		else if (!mappingConfirmed)
		{
			Console.WriteLine("结论 = ⚠️ 地址映射未确认 ⇒ 版本号（" + okCount + "/4 可读）不足以判定 EC 版本");
			Console.WriteLine("       ⇒ 判据是「值本身合理」（SOC 0~100 · 模式 0/2/0x48），不是「调用没报错」");
			if (okCount > 0)
			{
				Console.WriteLine("       ⇒ 但版本字节读到了 " + okCount + "/4 ⇒ 端口通道机制是工作的；");
				Console.WriteLine("         只是该机 XRAM 布局与本假设不一致（本机 EC 未必是 IT5570）");
			}
		}
		else if (okCount > 0)
		{
			Console.WriteLine("结论 = ⚠️ 部分可读（" + okCount + "/4）");
		}
		else
		{
			Console.WriteLine("结论 = ⛔ 端口通道不通（versionOk=" + versionOk + " socOk=" + socOk + " modeOk=" + modeOk + "）");
			Console.WriteLine("       ⇒ 具体卡在哪看上面「通道状态」那一行");
		}

		Console.WriteLine();
		Console.WriteLine("毒化状态 = " + (EcPortIo.IsPoisoned ? "已中毒（后续拒绝一切 I/O）" : "未中毒"));
		return 0;
	}
}
