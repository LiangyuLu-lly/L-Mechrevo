// 2026-10-16（0.291.9.4 · 风扇曲线自检 · ⭐ Logger 替身）：
// ⭐⭐ 为什么需要它 ⚠️：⭐ 生产代码里的 `Logger` 是**全局命名空间**的（⭐ `source\Logger.cs` 里没有 namespace ✅），
//    ⚠️ 但它**依赖 `AppConfig` 与 `MechrevoLite.Helpers`** ⚠️ ⇒ ⭐ 直接 link 进来会拖出一大串传递依赖 ❌
//    ⇒ ⭐ ⭐ 所以自检工程里给一个**最小替身** ✅ —— ⭐ 只实现被 `BaolongdaFanCurveStore` 用到的那三个成员 ✅
//
// ⭐ 顺带的好处：⭐ 替身会把日志**攒起来** ✅ ⇒ ⭐ 自检可以断言
//    「⭐ 读到旧存档时**确实**打了一条【默认级别可见】的日志」✅
//    —— ⭐ 而不只是断言"⭐ 没启用"⚠️（⭐ 那条才是第 1 步的验收点 ✅）。
//
// ⚠️ 它**不是**生产代码 ⚠️：⭐ 只活在 `tools\dev\fancurve-selftest` 里 ✅、⛔ 不进发布产物 ✅。

using System;
using System.Collections.Generic;

/// <summary>⭐ 自检用的 `Logger` 替身（⭐ 全局命名空间，⭐ 与生产同名同签名 ✅）。</summary>
public static class Logger
{
	/// <summary>⭐ `WriteError` 收到过的消息（⭐ 默认级别可见那一类 ✅）。</summary>
	internal static readonly List<string> Errors = new List<string>();

	/// <summary>⭐ `WriteLine` 收到过的消息 ✅。</summary>
	internal static readonly List<string> Lines = new List<string>();

	private static readonly Dictionary<string, string> LastByKey = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>⭐ 与生产同签名 ✅。</summary>
	public static void WriteLine(string logMessage)
	{
		Lines.Add(logMessage ?? string.Empty);
	}

	/// <summary>⭐ 与生产同签名 ✅。</summary>
	public static void WriteError(string logMessage)
	{
		Errors.Add(logMessage ?? string.Empty);
	}

	/// <summary>⭐ 与生产同签名（⭐ 返回"⭐ 状态变了没"✅）✅。</summary>
	internal static bool TryMarkChanged(string key, string logMessage)
	{
		string k = key ?? string.Empty;
		string v = logMessage ?? string.Empty;
		if (LastByKey.TryGetValue(k, out string previous) && previous == v)
		{
			return false;
		}
		LastByKey[k] = v;
		return true;
	}

	/// <summary>⭐ 清空攒下的日志（⭐ 每个用例前调一次 ✅）。</summary>
	internal static void Reset()
	{
		Errors.Clear();
		Lines.Clear();
		LastByKey.Clear();
	}
}
