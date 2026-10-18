// 2026-10-16（0.291.9.4 · 宝龙达风扇曲线 · 第 1+2 步 · ⭐ 纯逻辑，⛔ 不碰硬件、不下发）：
// ⭐⭐ 风扇曲线的【数据模型 ＋ 决策器】✅ —— ⭐ 与"下发"彻底解耦 ✅
//
// ⭐⭐ 为什么单独一个文件、⭐ 而且**一个外部依赖都不引** ⚠️（⛔ 没有 Logger、⛔ 没有别的项目类型）：
//    ⭐ 本仓的测试惯例是 `tools\dev\<name>-selftest\`：⭐ 把**生产源文件 link 进来**编成一个 exe，
//       ⭐ 用断言跑自检 ✅（⭐ 见 `kblight-selftest` / ⭐ `igpu-selftest` ✅ —— ⭐ 他们就是这么做的 ✅）
//    ⇒ ⭐ ⭐ 所以本文件必须**自给自足** ⇒ ⭐ 测试工程只需 link 它**一个**文件即可编译 ✅
//
// ⭐⭐ 两个关键设计（⭐ 都取自开源参考实现的"⭐ 做法"✅，⛔ 不搬代码 ⚠️）：
//    ⭐ ① ⭐ **转速用 RPM，⛔ 不用占空比** ✅
//       ⭐ 理由一：⭐ 宝龙达功能号 21 的入参**就是** `RPM/100` ✅ ⇒ ⭐ 用 RPM 少一次换算 ✅
//       ⭐ 理由二：⭐ ⭐ **没有可靠的 max RPM** ⚠️（⭐ 界面百分比一直显示不出来，⭐ 就是因为没有分母 ✅）
//          ⇒ ⭐ 用占空比就**必须编一个 max RPM** ⇒ ⭐ ⭐ **那是猜** ❌
//          ⭐（⭐ 既有实现里的 4400 / 8200 两个参考值⭐ 本文件不采用 ✅ —— ⭐ 见第 1 步说明 ✅）
//    ⭐ ② ⭐ **通道中性命名**（⭐ `ch0` / ⭐ `ch1`，⭐ 对应协议里的 `FanType 0/1` ✅）⚠️
//       ⭐ 理由：⭐ 功能号 13 的两个通道**哪个是 CPU、⭐ 哪个是 GPU 尚未真机确认** ⚠️
//          ⇒ ⭐ ⛔ 数据模型里**不许硬编码 CPU/GPU** ✅
//          ⇒ ⭐ 等 `--fan-watch` 在 CPU/GPU 分别加载下跑过、⭐ 确认归属之后，
//             ⭐ 再在**外面加一层语义映射** ✅（⭐ ⛔ 不回来改本文件 ⚠️）
//
// ⭐ 算法来源（⭐ 只取思路 ✅）：
//    ⭐ 线性插值 —— ⭐ 蛟龙 `JiaolongControl` 的 `AutoFanControl` ✅
//    ⭐ 回差 3℃ —— ⭐ 斗战者 `DOUZHANZHE-Control` 的 `ShouldWrite` ✅
//    ⚠️ `BellatorFanControl` **许可不明**（⭐ 只有 README、⭐ 无 LICENSE ⚠️）
//       ⇒ ⭐ 只参考其**被斗战者转述**的算法思想（⭐ 回差策略 ✅），⛔ 绝不引用其代码 ⚠️
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;

namespace MechrevoLite.Hardware;

/// <summary>⭐ 曲线上的一个点：⭐ 温度（℃）→ ⭐ 目标转速（RPM）✅。</summary>
internal readonly record struct FanCurvePoint(int TempC, int Rpm);

/// <summary>
/// ⭐⭐ 风扇曲线的**纯算法**（⭐ 插值 ＋ ⭐ 钳位 ＋ ⭐ 换算 ✅）—— ⭐ 无状态、⭐ 可单测 ✅
/// <para>⚠️ 本类**不认识**任何后端、⭐ 不知道宝龙达、⭐ 不碰硬件 ✅（⭐ 这是刻意的分层 ✅）。</para>
/// </summary>
internal static class FanCurveEngine
{
	/// <summary>⭐ 默认回差（℃）✅ —— ⭐ 取自斗战者 `ShouldWrite` 的 3℃ ✅。</summary>
	internal const int DefaultHysteresisC = 3;

	/// <summary>
	/// ⭐⭐ 协议档位上限（⭐ `RPM/100` 要装进一个 byte ⚠️）。
	/// <para>⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙放宽）：⭐ 上限由 60 放宽到 **68**（68 × 100 = 6800 RPM）✅
	/// —— ⭐ 与 <see cref="BaolongdaProfile.FanManualMaxUnits100Rpm" /> **同一条事实来源** ✅
	/// （⭐ 参考自 JiaolongControl (MIT) 的 `MAX_FAN_BYTE = 68`，⛔ 未复制代码）。</para>
	/// <para>⚠️ 仍远小于 byte 上限 255；⭐ 实际能否到 68 **以功能号 13 回读为准** ✅。</para>
	/// </summary>
	internal const int ProtocolMaxUnits100Rpm = BaolongdaProfile.FanManualMaxUnits100Rpm;

	/// <summary>⭐ 协议上限对应的 RPM ✅。</summary>
	internal const int ProtocolMaxRpm = ProtocolMaxUnits100Rpm * 100;

	/// <summary>
	/// ⭐ 把"⭐ 并列数组"⭐ 形式的曲线转成**点集** ✅（⭐ 存储格式 → ⭐ 决策器 ✅）。
	/// <para>⚠️ <b>逐项校验</b>：⭐ 长度不一致 / ⭐ 空 / ⭐ null ⇒ ⭐ 返回**空数组** ✅
	/// （⭐ 调用方据此判"⭐ 没有可用曲线"⭐ ⇒ ⭐ ⛔ 不下发 ✅）。</para>
	/// <para>⭐ 丢弃**不可解释**的点（⭐ 温度 ≤ 0 ⚠️ / ⭐ 转速 &lt; 0 ⚠️）⇒ ⭐ ⛔ **不猜、⭐ 不补默认值** ✅。</para>
	/// <para>⭐ 结果**按温度升序** ✅（⭐ 手改配置文件写乱了也不会让风扇乱转 ✅）·
	/// ⭐ 温度相同的点**保留**（⭐ 由 <see cref="ResolveRpm" /> 决定取哪个 ✅）。</para>
	/// </summary>
	internal static FanCurvePoint[] BuildPoints(int[] temperatures, int[] rpms)
	{
		if (temperatures == null || rpms == null
			|| temperatures.Length != rpms.Length || temperatures.Length == 0)
		{
			return Array.Empty<FanCurvePoint>();
		}
		List<FanCurvePoint> list = new List<FanCurvePoint>(temperatures.Length);
		for (int i = 0; i < temperatures.Length; i++)
		{
			if (temperatures[i] <= 0 || rpms[i] < 0)
			{
				continue;
			}
			list.Add(new FanCurvePoint(temperatures[i], rpms[i]));
		}
		if (list.Count == 0)
		{
			return Array.Empty<FanCurvePoint>();
		}
		list.Sort((FanCurvePoint a, FanCurvePoint b) => a.TempC.CompareTo(b.TempC));
		return list.ToArray();
	}

	/// <summary>
	/// ⭐ 纯函数：⭐ 在曲线上按**线性插值**求目标转速（⭐ RPM ✅）。
	/// <para>⭐ <b>为什么是线性插值</b>（⭐ 而不是斗战者的"⭐ 阶梯查找"⚠️）：</para>
	/// <list type="bullet">
	/// <item>⭐ 阶梯在两点之间是**跳变**的 ⇒ ⭐ 相邻档位会有可听的转速阶跃 ⚠️（⭐ 蛟龙用插值正是为了这个 ✅）</item>
	/// <item>⭐ 我们**既有的** `MechrevoHw.InterpolateDuty` **就是线性插值** ✅
	/// ⇒ ⭐ 沿用同一套口径 ⇒ ⭐ 曲线形状与既有界面**语义一致** ✅</item>
	/// <item>⭐ 插值可单测（⭐ 端点、⭐ 中点、⭐ 钳位都是确定值 ✅）；⭐ 阶梯的"⭐ 取哪个点"⭐ 在并列温度下更易歧义 ⚠️</item>
	/// </list>
	/// <para>⭐ 边界：⭐ 低于第一点 ⇒ ⭐ 取第一点；⭐ 高于最后一点 ⇒ ⭐ 取最后一点 ✅（⛔ **不外推** ✅）。</para>
	/// <para>⭐ 结果**夹紧**到 <paramref name="minRpm" /> / <paramref name="maxRpm" /> ✅，
	/// ⭐ 再夹到 <see cref="ProtocolMaxRpm" /> ✅（⭐ 手改出来的越界值也不会漏到协议层 ✅）。</para>
	/// <para>⚠️ <paramref name="points" /> 为空 / ⭐ null ⇒ ⭐ 返回 <paramref name="minRpm" /> 的夹紧值 ⚠️
	/// —— ⭐ ⛔ 调用方**必须**先判过空 ⚠️（⭐ 别把"⭐ 没有曲线"⭐ 当成"⭐ 最低转速"⭐ ❌）；
	/// ⭐ <see cref="FanCurveDecider.Decide" /> 就是这么做的 ✅。</para>
	/// </summary>
	internal static int ResolveRpm(FanCurvePoint[] points, int tempC, int minRpm, int maxRpm)
	{
		int lo = Math.Min(minRpm, maxRpm);
		int hi = Math.Max(minRpm, maxRpm);
		if (points == null || points.Length == 0)
		{
			return ClampToProtocol(Math.Clamp(lo, 0, ProtocolMaxRpm));
		}
		if (points.Length == 1)
		{
			return ClampToProtocol(Math.Clamp(points[0].Rpm, lo, hi));
		}
		if (tempC <= points[0].TempC)
		{
			return ClampToProtocol(Math.Clamp(points[0].Rpm, lo, hi));
		}
		int last = points.Length - 1;
		if (tempC >= points[last].TempC)
		{
			return ClampToProtocol(Math.Clamp(points[last].Rpm, lo, hi));
		}
		for (int i = 0; i < last; i++)
		{
			FanCurvePoint a = points[i];
			FanCurvePoint b = points[i + 1];
			if (tempC < a.TempC || tempC > b.TempC)
			{
				continue;
			}
			if (b.TempC == a.TempC)
			{
				// ⚠️ 手改出来的"⭐ 两点同温"⚠️ ⇒ ⭐ 取**后**一点，⭐ 且**绝不做除零** ✅
				return ClampToProtocol(Math.Clamp(b.Rpm, lo, hi));
			}
			int rpmA = Math.Clamp(a.Rpm, lo, hi);
			int rpmB = Math.Clamp(b.Rpm, lo, hi);
			int value = rpmA + (int)Math.Round((double)(tempC - a.TempC) * (rpmB - rpmA) / (b.TempC - a.TempC));
			return ClampToProtocol(Math.Clamp(value, lo, hi));
		}
		// ⭐ 走不到（⭐ 上面已经覆盖了所有区间 ✅）—— ⭐ 留一个**确定**的兜底 ⛔ 不返回 0 之外的猜值 ✅
		return ClampToProtocol(Math.Clamp(points[last].Rpm, lo, hi));
	}

	/// <summary>
	/// ⭐ RPM → ⭐ 协议档位（⭐ `RPM/100` ✅），⭐ 夹紧到 <c>0..<see cref="ProtocolMaxUnits100Rpm" /></c> ✅。
	/// <para>⭐ 这是**纯换算** ✅ —— ⭐ ⛔ 本方法**不下发**任何东西 ⚠️（⭐ 下发在第 3 步 ✅）。</para>
	/// <para>⚠️ 取整用**向下**除（⭐ 与既有 `rpm/100` 写法一致 ✅）⇒ ⭐ 1999 RPM 会给出档位 19 ✅。</para>
	/// </summary>
	internal static int ToUnits100Rpm(int rpm)
	{
		if (rpm <= 0)
		{
			return 0;
		}
		return Math.Clamp(rpm / 100, 0, ProtocolMaxUnits100Rpm);
	}

	/// <summary>⭐ 夹到协议上限 ✅ —— ⭐ ⛔ 不夹下界（⭐ 下界由调用方的 minRpm 决定 ✅）。</summary>
	private static int ClampToProtocol(int rpm)
	{
		return Math.Clamp(rpm, 0, ProtocolMaxRpm);
	}
}

/// <summary>
/// ⭐⭐ 风扇曲线的**决策器**（⭐ 回差防抖 ✅）—— ⭐ 只有内存状态，⛔ **不碰硬件、⭐ 不写任何寄存器、⭐ 不发任何命令** ✅
/// <para>⭐ 典型用法：⭐ 每 tick 调一次 <see cref="Decide" /> ✅
/// —— ⭐ 输入当前温度 ⇒ ⭐ 输出"⭐ 这次要不要写、⭐ 写多少 RPM"✅（⭐ <c>null</c> = ⭐ 本次不需要写 ✅）。</para>
/// <para>⭐⭐ <b>回差语义</b>（⭐ 取自斗战者 `ShouldWrite` 的「⭐ 升温即写、⭐ 降温需过 3℃」✅）：</para>
/// <list type="bullet">
/// <item>⭐ 第一次调用 ⇒ ⭐ **一定**写 ✅</item>
/// <item>⭐ 目标转速**升高** ⇒ ⭐ **立刻**写 ✅（⭐ 散热优先 ⚠️）</item>
/// <item>⭐ 目标转速**降低** ⇒ ⭐ 只有温度**比上次落笔时低了 ≥ 回差**才写 ✅（⭐ 防抖、⭐ 护噪音 ✅）</item>
/// <item>⭐ 目标转速**不变** ⇒ ⭐ **不写** ✅（⭐ 蛟龙「⭐ 只在变化时写」✅）</item>
/// </list>
/// <para>⚠️ <b>本类刻意不做的事</b>（⭐ 归第 4 步"⭐ 生命周期"⚠️）：</para>
/// <list type="bullet">
/// <item>⭐ 斗战者是**每 tick 都刷**⚠️（⭐ 其注释：⭐ 持续声明手动控制权，⭐ 防 EC 固件定时器退回自动 ✅）
/// —— ⭐ 那是**生命周期**的事 ✅ ⇒ ⭐ 本类把 <see cref="HasLast" /> / ⭐ <see cref="LastRpm" /> /
/// <see cref="LastTempC" /> 暴露出去，⭐ 让那一层自己决定要不要周期性重申 ✅。</item>
/// <item>⭐ 启动清理 / ⭐ 退出恢复 / ⭐ 睡眠恢复 / ⭐ Tick 防重入 ⇒ ⭐ 全部**不在本类** ✅。</item>
/// </list>
/// <para>⚠️ 本类**不是**线程安全的（⭐ 只有几个 int ⚠️）⇒ ⭐ 调用方自己保证单线程 tick ✅。</para>
/// </summary>
internal sealed class FanCurveDecider
{
	private bool _hasLast;
	private int _lastRpm;
	private int _lastTempC;

	/// <summary>⭐ 是否已经落过笔（⭐ false ⇒ ⭐ 下一次 <see cref="Decide" /> 一定写 ✅）。</summary>
	internal bool HasLast => _hasLast;

	/// <summary>⭐ 上次**落笔**时的目标转速（RPM ✅）—— ⭐ 没落过笔时是 0 ⚠️。</summary>
	internal int LastRpm => _lastRpm;

	/// <summary>⭐ 上次**落笔**时的温度（℃ ✅）—— ⭐ 回差就是拿它跟当前温度比 ✅。</summary>
	internal int LastTempC => _lastTempC;

	/// <summary>
	/// ⭐ 决策一步：⭐ 返回**要写的目标转速**（RPM ✅），⭐ 或 <c>null</c>（⭐ 本次不需要写 ✅）。
	/// <para>⚠️ 曲线为空 / ⭐ null ⇒ ⭐ 返回 <c>null</c> ✅（⭐ ⛔ **绝不**"⭐ 没有曲线就用最低转速"⭐ ❌）。</para>
	/// <para>⭐ <paramref name="hysteresisC" /> ≤ 0 ⇒ ⭐ 关闭回差（⭐ 只要转速变了就写 ✅）—— ⭐ 便于测试与"⭐ 关防抖"⭐ ✅。</para>
	/// </summary>
	internal int? Decide(FanCurvePoint[] points, int tempC, int minRpm, int maxRpm, int hysteresisC)
	{
		if (points == null || points.Length == 0)
		{
			return null;
		}
		int target = FanCurveEngine.ResolveRpm(points, tempC, minRpm, maxRpm);
		if (!_hasLast)
		{
			Accept(target, tempC);
			return target;
		}
		if (target == _lastRpm)
		{
			return null;
		}
		if (target > _lastRpm)
		{
			// ⭐ 升温这一侧**不需要**过回差 ✅（⭐ 散热优先 ⚠️）
			Accept(target, tempC);
			return target;
		}
		// ⭐ 只有"⭐ 降温"⭐ 这一侧才需要过回差 ✅
		if (hysteresisC <= 0 || _lastTempC - tempC >= hysteresisC)
		{
			Accept(target, tempC);
			return target;
		}
		return null;
	}

	/// <summary>
	/// ⭐ 清零内部状态（⭐ 下次 <see cref="Decide" /> 一定写 ✅）。
	/// <para>⭐ 用途：⭐ 睡眠恢复 / ⭐ 曲线被编辑过 / ⭐ 手动切回后强制重发一次 ✅（⭐ 斗战者的 `RecoverAfterSleep` 同款 ✅）。</para>
	/// </summary>
	internal void Reset()
	{
		_hasLast = false;
		_lastRpm = 0;
		_lastTempC = 0;
	}

	private void Accept(int rpm, int tempC)
	{
		_hasLast = true;
		_lastRpm = rpm;
		_lastTempC = tempC;
	}
}
