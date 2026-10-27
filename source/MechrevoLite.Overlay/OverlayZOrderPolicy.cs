namespace MechrevoLite.Overlay;

internal static class OverlayZOrderPolicy
{
	/// <summary>
	/// ⭐⭐ 2026-10-18（机主反馈「悬浮窗没有置顶、会被其他窗口盖住」）：⭐ <b>每帧都重申置顶</b> ✅
	/// <para>⚠️ 旧实现返回 <c>!currentlyTopmost</c> —— 而 <c>WS_EX_TOPMOST</c> 位在窗口创建时
	/// 就写进了 <c>CreateParams.ExStyle</c> ⚠️ ⇒ 那个位**一直是 1** ⇒ 判据恒假 ⇒
	/// ⭐ 调用点的 <c>SetWindowPos(HWND_TOPMOST, …)</c> **一次都没执行过** ❌（= 机主看到的"没置顶"）。</para>
	/// <para>⭐ 现在恒为 <c>true</c> ⇒ 悬浮窗 1 s 的 Tick 每次都 <c>SetWindowPos(HWND_TOPMOST,
	/// SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE)</c> —— ⭐ 开销可忽略、⛔ 不抢焦点、
	/// ⭐ 被别的置顶窗口盖住后 1 s 内自动回到最上层 ✅。</para>
	/// <para>⚠️ <paramref name="currentlyTopmost" /> 保留只为"可观测/可回退"，⛔ 已不参与判据。</para>
	/// </summary>
	internal static bool ShouldReassertTopmost(bool currentlyTopmost)
	{
		return true;
	}
}
