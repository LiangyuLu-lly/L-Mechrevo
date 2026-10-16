namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-12（用户拍板「键盘灯光也修复」· 第一次真机验证结论）：「键盘灯要不要写」的完整判定结果。
/// <para>由 <see cref="LightingState.DecideKeyboardLighting"/> 产出 —— 与离线夹具
/// <c>tools\dev\kblight-selftest</c> 断言的同一份真值表。</para>
/// </summary>
/// <param name="Write">本次键盘灯写入（HID 帧流 / GCU 回退的 <c>Keyboard/Ctrl</c>）是否放行。</param>
/// <param name="ClearExplicitOff">
/// LM 侧那条「用户明确关过」标记（<c>KeyboardRgb.KbPowerExplicitOff</c>）是否应当**清除并回写成 On**。
/// 只在「标记还在 **且** 硬件/GCU 报 On」时为 true —— 两个条件同时成立就意味着那个标记是**陈旧**的
/// （用户后来用 Fn 把灯点亮了，或者硬件自己开了），必须让硬件赢。
/// </param>
internal readonly record struct KeyboardLightingPlan(bool Write, bool ClearExplicitOff);
