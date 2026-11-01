using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-29（A5 · 真 bug：Logo/灯带选「混合(Mix)」被固件直接关灯）：
/// 灯带 / Logo / 铰链 / 同步四个**非键盘**灯效通道的 <c>color.isCircular</c> 口径 —— **逐通道 × 逐效果**一张表。
///
/// <para><b>为什么必须逐效果</b>（协议依据 <c>docs\notes\GCU灯效协议-按反编译校正.md</c> §2/§3/§3b）：
/// <c>isCircular</c> 决定 GCU 下发给 HID 的**颜色模式字节 b2</c>：</para>
/// <list type="bullet">
/// <item><c>ColorBlocks == 0</c> ⇒ <c>b2 = 0</c>（不发颜色，用固件自带色）</item>
/// <item><c>ColorBlocks &gt; 0 &amp;&amp; isCircular == true</c> ⇒ <c>b2 = 8</c>（逐段下发，多段循环）</item>
/// <item><c>ColorBlocks &gt; 0 &amp;&amp; isCircular == false</c> ⇒ <c>b2 = 1</c>（**单色**，只取 <c>ColorBuffer[0]</c>）</item>
/// </list>
///
/// <para><b>本类的取值口径</b>：厂商四个通道类（<c>HIDLightbar4_logo</c> / <c>HIDLightbar4_hinge</c> /
/// <c>HIDLightbar4_sync</c> / <c>HIDLightbar</c>）都是**先 <c>isCircular = false</c>**，再只在极少数效果分支里
/// 改成 <c>true</c>；⭐ <c>Mix(19)</c> **不在任何一个分支里** ⇒ 必须保持 <c>false</c>。
/// 旧实现却在 <c>Mix</c> 时发「7 段七彩 + <c>isCircular=true</c>」⇒ 固件按多段组合处理 Mix ⇒ **整条灯被关掉**。
/// 本类就是那张"到底哪些效果才配 <c>true</c>"的唯一事实来源。</para>
///
/// <para>⛔ 纯判据：不读配置、不发帧、不碰控件；<c>Keyboard/Ctrl</c> **不归本类管**（键盘恒 <c>true</c>，
/// owner 明确要求键盘行为不动）。</para>
/// </summary>
internal static class LightbarColorPolicy
{
	/// <summary>非键盘灯效通道族（与 MQTT 主题一一对应）。</summary>
	internal enum Channel
	{
		/// <summary>不是非键盘灯效通道（键盘 / 未知主题）。</summary>
		None,
		/// <summary><c>HidLightbar/Ctrl</c>（灯带，含 HIDLightbar2/3/4）。</summary>
		Lightbar,
		/// <summary><c>HidLightbar_Logo/Ctrl</c>。</summary>
		Logo,
		/// <summary><c>HidLightbar_Hinge/Ctrl</c>。</summary>
		Hinge,
		/// <summary><c>HidLightbar_Sync/Ctrl</c>。</summary>
		Sync
	}

	/// <summary>
	/// 厂商自带七色板（<c>HIDLightbar.cs</c> 第 121–128 行，Rainbow 时厂商自己塞的那 7 色）：
	/// 红 / 橙 / 黄 / 绿 / 青 / 蓝 / 紫 —— ⚠️ 第 7 色是 <c>(139,0,255)</c> 紫，**不是**品红 <c>(255,0,255)</c>。
	/// <para>⭐ 这份七彩**只给 <c>Rainbow(5)</c> 用**（见 <see cref="UsesVendorRainbowPalette"/>）。</para>
	/// </summary>
	internal static readonly int[] VendorRainbowPalette = new int[7]
	{
		unchecked((int)0xFFFF0000),
		unchecked((int)0xFFFFA500),
		unchecked((int)0xFFFFFF00),
		unchecked((int)0xFF00FF00),
		unchecked((int)0xFF00FFFF),
		unchecked((int)0xFF0000FF),
		unchecked((int)0xFF8B00FF)
	};

	/// <summary>主题 → 通道族（⛔ 大小写敏感，与 MQTT 主题字面量一致）。</summary>
	internal static Channel ResolveChannel(string topic)
	{
		if (string.IsNullOrEmpty(topic))
		{
			return Channel.None;
		}
		return topic switch
		{
			"HidLightbar/Ctrl" => Channel.Lightbar,
			"HidLightbar_Logo/Ctrl" => Channel.Logo,
			"HidLightbar_Hinge/Ctrl" => Channel.Hinge,
			"HidLightbar_Sync/Ctrl" => Channel.Sync,
			_ => Channel.None
		};
	}

	/// <summary>是否属于本类管理的四个非键盘灯效通道。</summary>
	internal static bool IsLightbarChannel(string topic)
	{
		return ResolveChannel(topic) != Channel.None;
	}

	/// <summary>
	/// <c>color.isCircular</c> 的**逐通道 × 逐效果**取值（true ⇒ b2=8 多段循环；false ⇒ b2=1 单色）。
	/// <para>证据逐条对应 <c>docs\notes\GCU灯效协议-按反编译校正.md</c>：</para>
	/// <list type="bullet">
	/// <item><b>灯带</b>（§3b 表第一行）：<c>1 Single</c> ⇒ false（并把整条刷成 <c>ColorBuffer[0]</c>）；
	/// <c>5 Rainbow</c> ⇒ **true**（厂商自塞 7 色 + <c>ColorBlocks=7</c>）；
	/// <c>2 Breathing / 19 Mix / 51 UserMode</c> ⇒ 直接 <c>base.RunEffct</c>（不动颜色）⇒ 默认 false 起步，
	/// 其中 **Mix 保持 false**（§3「不在任何分支」）。</item>
	/// <item><b>Logo</b>（§3）：<c>1</c>⇒false、<c>2</c>⇒**true**、<c>4/13</c>⇒归一成 4 且保持默认 false、
	/// <c>5/33</c>⇒归一成 5 且保持默认 false、<c>6/10</c>⇒false、**其余（含 19 Mix）⇒ false**。</item>
	/// <item><b>铰链</b>（§3b 表第二行）：<c>1</c>⇒false、<c>2</c>⇒**true**、<c>4/13</c>⇒**true** 并归 4、
	/// <c>5/33</c>⇒false 并归 5、<c>6/10</c>⇒false 并归 6。</item>
	/// <item><b>同步</b>（§3b 表第三行）：⭐ 默认 false，且 <c>1/2/3</c> ⇒ **false**（这三个效果是
	/// 「按 <c>ControlBytes</c> 逐个 control 各跑一遍」，本来就是单色多段同步，⛔ 不能再叠 b2=8）。</item>
	/// </list>
	/// <para>⚠️ 协议表没覆盖到的效果（Wave/Marquee/Aurora/Dawn/Thinking…）一律 <c>false</c> ——
	/// 这不是"猜"，而是照四份厂商实现的**默认值**（每个 <c>RunEffct</c> 第一句都是 <c>isCircular = false</c>）。</para>
	/// </summary>
	internal static bool IsCircular(string topic, string effect)
	{
		Channel channel = ResolveChannel(topic);
		if (channel == Channel.None || string.IsNullOrWhiteSpace(effect))
		{
			return false;
		}
		// ⭐ 同步通道：协议明确 1/2/3 都必须是 false（逐 control 多段同步，不吃 b2=8），其余也 false。
		if (channel == Channel.Sync)
		{
			return false;
		}
		return effect switch
		{
			// 呼吸：Logo / 灯带 / 铰链 ⇒ 多段循环（厂商显式 true）
			"Breathing" => true,
			// 彩虹：⭐ 只有灯带通道厂商会自塞 7 色并置 true（Logo/铰链/同步上厂商保持 false）
			"Rainbow" => channel == Channel.Lightbar,
			// 反应 / 冲击：只有铰链通道厂商显式 true（Logo 上保持默认 false）
			"Reactive" => channel == Channel.Hinge,
			"Impact" => channel == Channel.Hinge,
			// ⭐ Mix(19)：不在任何厂商分支 ⇒ 恒 false（单色，只吃 ColorBuffer[0]）—— 本次真 bug 的修复点
			"Mix" => false,
			_ => false
		};
	}

	/// <summary>
	/// 该效果**要不要带用户颜色**（1 段 <c>ColorBuffer[0]</c> ⇒ b2=1 单色）。
	/// <para>与 <see cref="KeyboardFirmwareEffects.IsSingleColorEffect"/> 的差别只有一条：
	/// 本判据是**非键盘通道**用的，且把 <c>Mix</c> 明确归入"单色"（协议 §6：Mix ⇒ 1 段 + 用户颜色）。</para>
	/// </summary>
	internal static bool WantsUserColor(string effect)
	{
		return KeyboardFirmwareEffects.IsSingleColorEffect(effect);
	}

	/// <summary>
	/// 该效果是否使用 <b>厂商七色板</b>（7 段 + 多段循环）。⭐ **只有 Rainbow(5)** ——
	/// 旧实现把这份七彩当成"没给颜色时的兜底"，才导致"呼吸变红 / 混合关灯"。
	/// </summary>
	internal static bool UsesVendorRainbowPalette(string effect)
	{
		return string.Equals(effect, "Rainbow", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Gen40「官方标志灯效」路径的颜色模式（<c>MechrevoService.SetLightEffect</c> 里
	/// <c>applyOfficialLogo</c> 那一块用），1=单段用户色 / 7=七色循环 / 0=不发色。
	/// <para>⭐ 与 <c>LightForm.LightEffectColorMode</c> 的唯一差别：<c>Mix</c> 归 **1（单色）** 而不是 0。
	/// 依据：协议 §6「Mix 厂商口径默认 false ⇒ 单色 ⇒ 1 段 + 用户颜色」；旧口径把 Mix 归 0（不发颜色），
	/// 真机上就是"选了混合什么都不亮 / 直接关灯"。</para>
	/// </summary>
	internal static int ResolveLogoColorMode(string effect)
	{
		if (UsesVendorRainbowPalette(effect))
		{
			return 7;
		}
		if (string.Equals(effect, "Mix", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		return LightForm.LightEffectColorMode(effect);
	}
}
