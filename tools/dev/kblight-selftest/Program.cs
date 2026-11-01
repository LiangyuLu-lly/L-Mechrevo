using System;
using System.Collections.Generic;
using MechrevoLite.Hardware;

/// <summary>
/// 2026-10-12 离线夹具：断言键盘灯「门控真值表」。
///
/// <para>被测代码 = 产品源码 <c>source\MechrevoLite.Hardware\LightingState.cs</c> 的
/// <see cref="LightingState.ShouldWriteKeyboardLighting" /> / <see cref="LightingState.ResolveEffectiveKbPower" />
/// / <see cref="LightingState.ShouldClearExplicitOffOnHardwareOn" /> /
/// <see cref="LightingState.DecideKeyboardLighting" />（⭐ 直接 Link 编译，⛔ 不是复制一份逻辑）。</para>
///
/// <para>用户拍板的要求三条（本夹具必须全绿）：
/// ① <c>KbPowerOn=false + hwPower=true ⇒ 应当发</c>；
/// ② <c>false + false ⇒ 不发</c>；
/// ③ <c>true ⇒ 发</c>；
/// 外加反例保护：用户明确关过灯（explicitOff=true）**且硬件也报 Off** 时，⛔ 不发。</para>
///
/// <para>2026-10-12 第二轮（用户原话「键盘灯光也修复」）追加：<c>explicitOff</c> 这条标记
/// **只在硬件也报 Off 时才算数** —— 陈旧标记（硬件报 On）+ 硬件 On ⇒ <b>清标记 + 发帧</b>；
/// explicit-off + 硬件 Off ⇒ 不发（静默）；explicit-off + 硬件 On ⇒ 清标记 + 发帧；
/// 并且**只清一次**（防抖振：硬件 Off/On 振荡时不重复刷帧）。</para>
///
/// <para>用法：<c>dotnet run -c Release --project tools\dev\kblight-selftest</c>（退出码 0 = 全绿）。</para>
/// </summary>
internal static class Program
{
	private static int _fail;

	private static int _total;

	private static void Check(string name, bool actual, bool expected)
	{
		_total++;
		string mark = (actual == expected) ? "PASS" : "FAIL";
		if (actual != expected)
		{
			_fail++;
		}
		Console.WriteLine($"[{mark}] {name,-62} actual={actual,-5} expected={expected}");
	}

	private static void CheckInt(string name, int actual, int expected)
	{
		Check(name, actual == expected, true);
	}

	private static int Main()
	{
		Console.WriteLine("== 键盘灯门控真值表（LightingState.ShouldWriteKeyboardLighting）==");
		Console.WriteLine("缓存=KbPowerOn / 硬件=MechrevoHw.KeyboardPower / 用户明确关过=KbPowerExplicitOff / 开关=KeyboardCtrlPublishSwitch");
		Console.WriteLine();

		// ① 用户拍板：KbPowerOn=false + 硬件报 On ⇒ 应当发（旧代码在这里静默不发）
		Check("cache=false, hw=true,  explicitOff=false, sw=false", LightingState.ShouldWriteKeyboardLighting(false, true, false, false), true);
		// ② false + false ⇒ 不发
		Check("cache=false, hw=false, explicitOff=false, sw=false", LightingState.ShouldWriteKeyboardLighting(false, false, false, false), false);
		// ③ true ⇒ 发
		Check("cache=true,  hw=false, explicitOff=false, sw=false", LightingState.ShouldWriteKeyboardLighting(true, false, false, false), true);
		Check("cache=true,  hw=true,  explicitOff=false, sw=false", LightingState.ShouldWriteKeyboardLighting(true, true, false, false), true);

		Console.WriteLine();
		Console.WriteLine("-- 反例保护：用户明确关灯（且硬件也报 Off）⇒ ⛔不许自动开 --");
		Check("cache=false, hw=true,  explicitOff=TRUE,  sw=false", LightingState.ShouldWriteKeyboardLighting(false, true, true, false), false);
		Check("cache=false, hw=false, explicitOff=TRUE,  sw=false", LightingState.ShouldWriteKeyboardLighting(false, false, true, false), false);
		Check("cache=true,  hw=true,  explicitOff=TRUE,  sw=false", LightingState.ShouldWriteKeyboardLighting(true, true, true, false), false);
		Check("cache=true,  hw=false, explicitOff=TRUE,  sw=false", LightingState.ShouldWriteKeyboardLighting(true, false, true, false), false);

		Console.WriteLine();
		Console.WriteLine("-- KeyboardCtrlPublishSwitch.Enabled 显式放行（默认 OFF 的既有开关）--");
		Check("cache=false, hw=false, explicitOff=false, sw=TRUE", LightingState.ShouldWriteKeyboardLighting(false, false, false, true), true);
		Check("cache=false, hw=false, explicitOff=TRUE,  sw=TRUE", LightingState.ShouldWriteKeyboardLighting(false, false, true, true), true);

		Console.WriteLine();
		Console.WriteLine("-- ResolveEffectiveKbPower：有效电源（UI 开关 / 恢复 / 离电熄灯共用）--");
		Check("cache=false, hw=true,  explicitOff=false => 有效=On", LightingState.ResolveEffectiveKbPower(false, true, false), true);
		Check("cache=false, hw=false, explicitOff=false => 有效=Off", LightingState.ResolveEffectiveKbPower(false, false, false), false);
		Check("cache=true,  hw=false, explicitOff=false => 有效=On", LightingState.ResolveEffectiveKbPower(true, false, false), true);
		Check("cache=false, hw=true,  explicitOff=TRUE  => 有效=Off", LightingState.ResolveEffectiveKbPower(false, true, true), false);

		Console.WriteLine();
		Console.WriteLine("-- 回归：既有 ShouldRestoreKeyboardPower 语义未被改动 --");
		Check("restore(user=true,  tempOff=false, cached=false)", LightingState.ShouldRestoreKeyboardPower(true, false, false), true);
		Check("restore(user=true,  tempOff=false, cached=true )", LightingState.ShouldRestoreKeyboardPower(true, false, true), false);
		Check("restore(user=true,  tempOff=true,  cached=true )", LightingState.ShouldRestoreKeyboardPower(true, true, true), true);
		Check("restore(user=false, tempOff=false, cached=true )", LightingState.ShouldRestoreKeyboardPower(false, false, true), false);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-12 第二轮（「键盘灯光也修复」）：陈旧标记 + 硬件报 On ⇒ 清标记并放行 ==");
		Console.WriteLine("判据：explicitOff 只在【硬件也报 Off】时才算数（LightingState.DecideKeyboardLighting）");

		// ① 陈旧标记（硬件报 On、缓存 false）⇒ Write=true 且 Clear=true（清标记 + 回写缓存 + 发帧）
		KeyboardLightingPlan staleOn = LightingState.DecideKeyboardLighting(true, true, false, true, false);
		Check("① 陈旧标记 cache=false, hw=ON,  explicitOff=TRUE  => Write", staleOn.Write, true);
		Check("① 陈旧标记 cache=false, hw=ON,  explicitOff=TRUE  => ClearExplicitOff", staleOn.ClearExplicitOff, true);

		// ② explicit-off + 硬件也报 Off ⇒ ⛔ 不发、⛔ 不清（静默：这正是用户要的"关"）
		KeyboardLightingPlan explicitOffHwOff = LightingState.DecideKeyboardLighting(true, false, false, true, false);
		Check("② explicitOff + hw=OFF => Write(静默不发)", explicitOffHwOff.Write, false);
		Check("② explicitOff + hw=OFF => ClearExplicitOff(不清)", explicitOffHwOff.ClearExplicitOff, false);

		// ③ explicit-off + 硬件报 On ⇒ 清标记 + 发帧（与 ① 同一格的另一半：缓存已经是 On）
		KeyboardLightingPlan explicitOffHwOn = LightingState.DecideKeyboardLighting(true, true, true, true, false);
		Check("③ explicitOff + hw=ON  => Write(发帧)", explicitOffHwOn.Write, true);
		Check("③ explicitOff + hw=ON  => ClearExplicitOff(清标记)", explicitOffHwOn.ClearExplicitOff, true);

		// ④ 防抖振：标记清掉之后（explicitOff 已按"清过的值"传入）⇒ 同一格不再要求清第二次
		KeyboardLightingPlan afterClear = LightingState.DecideKeyboardLighting(true, true, true, false, false);
		Check("④ 清过之后 cache=true, hw=ON, explicitOff=FALSE => Write", afterClear.Write, true);
		Check("④ 清过之后 cache=true, hw=ON, explicitOff=FALSE => ClearExplicitOff(幂等不再清)", afterClear.ClearExplicitOff, false);

		// ⑤ 未连接时**不采信**硬件读数：硬件字段缺省 true 也⛔不许放行（退回 LM 缓存 false）
		KeyboardLightingPlan offline = LightingState.DecideKeyboardLighting(false, true, false, false, false);
		Check("⑤ hw未连接 cache=false, hw=true => Write(不采信硬件)", offline.Write, false);
		Check("⑤ hw未连接 hw=true, explicitOff=TRUE => ClearExplicitOff(不清)", offline.ClearExplicitOff, false);

		// ⑥ 写入口径一致性：决定"清标记"的那一格，Write 也必须是 true（⛔ 不许出现"清了但不发"）
		Check("⑥ 陈旧标记那格 Write == ClearExplicitOff", staleOn.Write == staleOn.ClearExplicitOff, true);
		Check("⑥ ClearExplicitOff 只在(已连接 & 硬件On & 标记在)时为真",
			LightingState.ShouldClearExplicitOffOnHardwareOn(true, true, true) &&
			!LightingState.ShouldClearExplicitOffOnHardwareOn(true, false, true) &&
			!LightingState.ShouldClearExplicitOffOnHardwareOn(false, true, true) &&
			!LightingState.ShouldClearExplicitOffOnHardwareOn(true, true, false), true);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-12 第三轮（机主「亮度是0，所以打开关闭没有变化」）：亮度 0 归一化 ==");
		Console.WriteLine("判据：先判有效电源（explicitOff 只在硬件也报 Off 时算数），再抬亮度（LightingState.NormalizeKeyboardBrightness）");

		// ① 电源 On + 亮度 0 + 有历史非 0 亮度 ⇒ 抬回**用户自己上次的值**（65 是机主说的那个数）
		CheckInt("① 电源On, 亮度0, 历史65   => 抬到 65", LightingState.NormalizeKeyboardBrightness(true, 0, 65, false), 65);
		// ② 电源 On + 亮度 0 + ⛔ 没有历史 ⇒ 抬到默认 50%
		CheckInt("② 电源On, 亮度0, 历史无    => 抬到 50", LightingState.NormalizeKeyboardBrightness(true, 0, LightingState.NoKeyboardBrightnessHistory, false), LightingState.DefaultKeyboardBrightness);
		// ③ 电源 Off（explicitOff 且硬件也报 Off）⇒ ⛔ 一个字节都不动（别与 explicitOff 修复打架）
		CheckInt("③ 电源Off, 亮度0, 历史65   => 保持 0", LightingState.NormalizeKeyboardBrightness(false, 0, 65, false), 0);
		// ④ 单分区机型（KeyboardType∈{1,2}）：亮度 0 本来就 = 关 ⇒ ⛔ 不动
		CheckInt("④ 单分区, 电源On, 亮度0, 历史65 => 保持 0", LightingState.NormalizeKeyboardBrightness(true, 0, 65, true), 0);
		// ⑤ 亮度 ≥ 1 ⇒ 原样保留（用户自己调的值绝不覆盖）
		CheckInt("⑤ 电源On, 亮度65           => 保持 65", LightingState.NormalizeKeyboardBrightness(true, 65, 100, false), 65);
		CheckInt("⑤ 电源On, 亮度1（很暗但有意）=> 保持 1", LightingState.NormalizeKeyboardBrightness(true, 1, 100, false), 1);
		// ⑥ 亮度未知(-1) ⇒ 不动（没有可信读数就不猜）
		CheckInt("⑥ 电源On, 亮度-1(未知)     => 保持 -1", LightingState.NormalizeKeyboardBrightness(true, LightingState.KeyboardBrightnessUnknown, 65, false), -1);
		// ⑦ latch 规则：只有 1..100 才算"上一次非 0 的亮度"
		Check("⑦ latch: 0 不算", LightingState.ShouldLatchLastNonZeroBrightness(0), false);
		Check("⑦ latch: -1 不算", LightingState.ShouldLatchLastNonZeroBrightness(-1), false);
		Check("⑦ latch: 65 算", LightingState.ShouldLatchLastNonZeroBrightness(65), true);
		Check("⑦ latch: 100 算", LightingState.ShouldLatchLastNonZeroBrightness(100), true);
		Check("⑦ latch: 101 不算", LightingState.ShouldLatchLastNonZeroBrightness(101), false);
		// ⑧ 幂等（防抖振）：抬过一次之后再算，值不变 ⇒ 不会再写配置、不会再刷帧
		int once = LightingState.NormalizeKeyboardBrightness(true, 0, 65, false);
		Check("⑧ 归一是幂等的（第二次传入同一个值）", LightingState.NormalizeKeyboardBrightness(true, once, 65, false) == once, true);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-12 第四轮（用户澄清「我加的部分是为非单键RGB键盘的分流」）：分流只许落在非单键机型 ==");
		Console.WriteLine("判据：LightingState.ShouldUseGcuKeyboardPowerChannel（KeyboardType∈{1,2} 才是 GCU 分流族）");

		// 单键 RGB（耀世 type 7 = 非 {1,2}）⇒ ⛔ 任何状态下都不产生 Keyboard/Ctrl 下发
		Check("① 单键机型(非GCU族) + GCU 可用   => 不发", LightingState.ShouldUseGcuKeyboardPowerChannel(false, true), false);
		Check("① 单键机型(非GCU族) + GCU 不可用 => 不发", LightingState.ShouldUseGcuKeyboardPowerChannel(false, false), false);
		// 非单键（type 1/2 = GCU 族）⇒ 分流照设计发生（还要求 GCU 通道可用）
		Check("② 非单键机型(GCU族) + GCU 可用   => 发", LightingState.ShouldUseGcuKeyboardPowerChannel(true, true), true);
		Check("② 非单键机型(GCU族) + GCU 不可用 => 不发", LightingState.ShouldUseGcuKeyboardPowerChannel(true, false), false);
		// 「是否 GCU 分流族」这一位与「GCU 通道是否可用」解耦：单键机型结果恒 false
		Check("③ 单键机型结果与 GCU 通道可用性无关",
			LightingState.ShouldUseGcuKeyboardPowerChannel(false, true) == LightingState.ShouldUseGcuKeyboardPowerChannel(false, false), true);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-12 第五轮（窄化 GCU 分流 gate）：开灯恢复方向按既有语义发 ==");
		Console.WriteLine("单键机型（HID 通道）= 固件电源报 Off 才补发 on（作者 UI 同一句语义）；非单键机型逐格走既有 ShouldRestoreKeyboardPower");

		// ① 单键 + 固件报 Off + 用户要开 ⇒ 补发 on（★ 与 ArmKeyboardEffectProtect() 同一个判据，arm 与发 on 同进同出）
		Check("① 单键, 固件Off, 要开              => 发on", LightingState.ShouldIssueKeyboardPowerOn(false, true, false, false), true);
		// ② 单键 + 固件报 On ⇒ ⛔ 不补发（只 HID）：作者 UI 那句 !hw.KeyboardPower 的反面
		Check("② 单键, 固件On,  要开              => 不发on", LightingState.ShouldIssueKeyboardPowerOn(false, true, false, true), false);
		// ②' 单键 + 刚临时熄过灯：判据不看这位（关灯方向没走 GCU，电源轨根本没被切）
		Check("②' 单键, 固件On, 临时熄过灯, 要开   => 不发on", LightingState.ShouldIssueKeyboardPowerOn(false, true, true, true), false);
		// ③ 非单键（GCU 分流族）：与改动前逐格一致
		Check("③ 非单键, 固件Off, 要开            => 发on", LightingState.ShouldIssueKeyboardPowerOn(true, true, false, false), true);
		Check("③ 非单键, 固件On,  要开, 未临时熄   => 不发on", LightingState.ShouldIssueKeyboardPowerOn(true, true, false, true), false);
		Check("③ 非单键, 固件On,  要开, 临时熄过   => 发on", LightingState.ShouldIssueKeyboardPowerOn(true, true, true, true), true);
		// ④ 用户不想开 ⇒ 两个通道都不发
		Check("④ 单键,   要关                     => 不发on", LightingState.ShouldIssueKeyboardPowerOn(false, false, false, false), false);
		Check("④ 非单键, 要关                     => 不发on", LightingState.ShouldIssueKeyboardPowerOn(true, false, false, false), false);
		// ⑤ 两个入口不许分叉：非单键那几格必须与既有 ShouldRestoreKeyboardPower 逐格相等
		Check("⑤ 非单键新入口 == 既有 ShouldRestoreKeyboardPower（4 格）",
			LightingState.ShouldIssueKeyboardPowerOn(true, true, false, false) == LightingState.ShouldRestoreKeyboardPower(true, false, false)
			&& LightingState.ShouldIssueKeyboardPowerOn(true, true, false, true) == LightingState.ShouldRestoreKeyboardPower(true, false, true)
			&& LightingState.ShouldIssueKeyboardPowerOn(true, true, true, false) == LightingState.ShouldRestoreKeyboardPower(true, true, false)
			&& LightingState.ShouldIssueKeyboardPowerOn(true, true, true, true) == LightingState.ShouldRestoreKeyboardPower(true, true, true), true);
		// ⑥ 「保护被 arm」= 上面这条判据为 true 的那几格：ArmKeyboardEffectProtect() 在 Program.cs 里
		//    与 IssueLightPower(on:true) **同一个 if 分支**（同进同出，⛔ 没有第二种 arm 条件）⇒
		//    断言"发 on"的那几格为 true，就是断言"arm 会发生"。
		//    ⚠️ 3 秒窗口那段（ShouldReassertProtectedKeyboardCustomMode）在 Program.cs 里，本夹具只 Link
		//    LightingState 一族、⛔ 不复制一份逻辑 ⇒ 不在离线夹具里重复断言它的细节。
		Check("⑥ arm/发 on 的那几格恰好是这几格（单键固件Off、非单键固件Off、非单键临时熄过）",
			LightingState.ShouldIssueKeyboardPowerOn(false, true, false, false)
			&& LightingState.ShouldIssueKeyboardPowerOn(true, true, false, false)
			&& LightingState.ShouldIssueKeyboardPowerOn(true, true, true, true)
			&& !LightingState.ShouldIssueKeyboardPowerOn(false, true, false, true), true);

		Console.WriteLine();
		Console.WriteLine("== 第五轮（续）：临时熄灯标记按「这次是谁把灯弄灭的」记 ==");

		// ① 非单键（GCU 分流）：只有 off 真的下发成功才置标记（与改动前逐格一致）
		Check("① 非单键 + GCU off 成功             => 置标记", LightingState.ShouldMarkKeyboardTemporarilySuspended(true, true, true), true);
		Check("① 非单键 + GCU off 失败             => 不置", LightingState.ShouldMarkKeyboardTemporarilySuspended(true, false, true), false);
		// ② 单键（HID 通道）：看黑帧是否真的出去了（帧流在 = 512 字节全 0 帧确实已下发）
		Check("② 单键 + HID 帧流在                 => 置标记", LightingState.ShouldMarkKeyboardTemporarilySuspended(false, false, true), true);
		Check("② 单键 + HID 帧流不在（发不出去）    => 不置", LightingState.ShouldMarkKeyboardTemporarilySuspended(false, false, false), false);
		// ③ 单键这一格与「GCU off 是否成功」无关（那条电源它本来就不发）
		Check("③ 单键的标记与 GCU off 结果无关",
			LightingState.ShouldMarkKeyboardTemporarilySuspended(false, true, true) == LightingState.ShouldMarkKeyboardTemporarilySuspended(false, false, true), true);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-12 第六轮（关灯方向）：HID 控制器不可用时才退回 GCU 关灯 ==");
		Console.WriteLine("判据：LightingState.ShouldUseGcuKeyboardOffChannel（= 既有 GCU 分流通道 || !HID 可用）");

		// ① ⭐ 本轮新增的那一格：单键机型 + HID 帧流不在 ⇒ 仍发 GCU off
		//    （帧流不在时 StopCurrentEffect 的全 0 帧发不出去 ⇒ 灯熄不掉 ⇒ 必须借 GCU 切电源轨）
		Check("① 单键 + GCU可用 + HID不可用 => 仍发 GCU off", LightingState.ShouldUseGcuKeyboardOffChannel(false, true, false), true);
		// ② ⛔ 反例保护（不许退）：单键机型 + HID 正常 ⇒ 一条 GCU off 都不发（与第四/五轮口径一致）
		Check("② 单键 + GCU可用 + HID正常   => 不发 GCU off", LightingState.ShouldUseGcuKeyboardOffChannel(false, true, true), false);
		// ③ 非单键（GCU 分流族）+ GCU 可用 ⇒ 照旧走 GCU（开与关同一族，HID 状态不改变它）
		Check("③ 非单键 + GCU可用 + HID正常   => 发 GCU off", LightingState.ShouldUseGcuKeyboardOffChannel(true, true, true), true);
		Check("③ 非单键 + GCU可用 + HID不可用 => 发 GCU off", LightingState.ShouldUseGcuKeyboardOffChannel(true, true, false), true);
		// ④ GCU 通道不可用 + HID 也不可用 ⇒ 判据仍为 true（= "退回 GCU 试一次"），
		//    ⚠️ 真下发的守卫在调用方 Program：`mechrevoHw == null || !IsConnected || service == null ⇒ return false`
		//    ⇒ service 不在时**不发**、并如实返回 false（⛔ 不假成功：StopCurrentEffect 也没发出去）。
		Check("④ 单键 + GCU不可用 + HID不可用 => true（回退尝试，真发由调用方守卫）", LightingState.ShouldUseGcuKeyboardOffChannel(false, false, false), true);
		Check("④ 非单键 + GCU不可用 + HID不可用 => true（同上）", LightingState.ShouldUseGcuKeyboardOffChannel(true, false, false), true);
		// ⑤ 解耦：GCU 通道可用时，发不发只由 HID 状态决定（单键机型那两格）
		Check("⑤ 单键 + GCU可用 => 结果 == !HID可用",
			LightingState.ShouldUseGcuKeyboardOffChannel(false, true, true) == false
			&& LightingState.ShouldUseGcuKeyboardOffChannel(false, true, false) == true, true);
		// ⑥ 非单键机型：**只在 GCU 通道可用时**逐格等于既有判据；GCU 不可用 + HID 不可用时本判据更宽
		//    （= 本轮的取舍：关灯方向宁可多试一次 GCU，也不静默"假关"）
		Check("⑥ 非单键 + GCU可用 逐格 == 既有 ShouldUseGcuKeyboardPowerChannel（2 格）",
			LightingState.ShouldUseGcuKeyboardOffChannel(true, true, true) == LightingState.ShouldUseGcuKeyboardPowerChannel(true, true)
			&& LightingState.ShouldUseGcuKeyboardOffChannel(true, true, false) == LightingState.ShouldUseGcuKeyboardPowerChannel(true, true), true);
		Check("⑥ 非单键 + GCU不可用 => 本判据 = !HID可用（比既有宽一格）",
			LightingState.ShouldUseGcuKeyboardOffChannel(true, false, true) == false
			&& LightingState.ShouldUseGcuKeyboardOffChannel(true, false, false) == true
			&& LightingState.ShouldUseGcuKeyboardPowerChannel(true, false) == false, true);
		// ⑦ 幂等：同一输入连算两次结果相同（无隐藏状态）
		Check("⑦ 判据是纯函数（幂等）",
			LightingState.ShouldUseGcuKeyboardOffChannel(false, true, false) == LightingState.ShouldUseGcuKeyboardOffChannel(false, true, false), true);

		Console.WriteLine();
		Console.WriteLine("== 2026-10-19 键盘灯运行时自校准（KeyboardLightingCalibration 纯函数）==");
		Console.WriteLine("机主口径：40 系亮度只有 0/1/2/3（最大 3）；彩虹已改为软件刷色（与本校准无关）");

		// ① 探测到 3 档（与机主口径一致）⇒ 上限 3
		KeyboardLightingCalibration full = KeyboardLightingCalibration.FromProbe(3, 3, "t");
		Check("亮度探测成功(3) => BrightnessProbed", full.BrightnessProbed, true);
		CheckInt("亮度探测成功(3) => 上限 3", full.BrightnessMax, 3);
		// ② 只能写到 2（写 3 被夹回）⇒ 上限取 2（界面不再给"拖到没用"的档）
		KeyboardLightingCalibration clamped = KeyboardLightingCalibration.FromProbe(3, 2, "t");
		CheckInt("写 3 被夹回 2 => 上限 2", clamped.BrightnessMax, 2);
		// ③ 探测失败 ⇒ 亮度退回声明值（保守）
		KeyboardLightingCalibration failed = KeyboardLightingCalibration.FromProbe(3, 0, "t");
		Check("亮度探测失败 => BrightnessProbed=false", failed.BrightnessProbed, false);
		CheckInt("亮度探测失败 => 退回声明值 3", failed.BrightnessMax, 3);
		// ④ 探测值不得高于声明值（防"被夹回/越界读数"把上限抬高）
		KeyboardLightingCalibration over = KeyboardLightingCalibration.FromProbe(3, 5, "t");
		CheckInt("探测值 5 > 声明 3 => 上限仍 3（不抬高）", over.BrightnessMax, 3);
		// ⑤ 保守回退（非宝龙达 / 实例不可用）
		KeyboardLightingCalibration cons = KeyboardLightingCalibration.Conservative(3, "非宝龙达");
		Check("保守回退 => 未探测", cons.BrightnessProbed, false);
		CheckInt("保守回退 => 上限=声明值 3", cons.BrightnessMax, 3);
		CheckInt("声明值 0 => 保守回退下限 1", KeyboardLightingCalibration.Conservative(0, "x").BrightnessMax, 1);
		// ⑥ 只能写到 1 档也如实（不拿声明值冒充实测值）
		KeyboardLightingCalibration onlyOne = KeyboardLightingCalibration.FromProbe(3, 1, "t");
		Check("只能写到 1 => BrightnessProbed", onlyOne.BrightnessProbed, true);
		CheckInt("只能写到 1 => 上限 1", onlyOne.BrightnessMax, 1);

		Console.WriteLine();
		Console.WriteLine(_fail == 0 ? $"== 全绿：键盘灯自检 {_total}/{_total} ==" : $"== 失败 {_fail}/{_total} 项 ==");
		return (_fail == 0) ? 0 : 1;
	}
}
