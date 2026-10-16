using System;
using System.Collections.Generic;

namespace MechrevoLite;

internal static class ModelScopedConfig
{
	public const string ScopeMarkerKey = "model_config_scope";

	private const string ScopePrefix = "model.";

	// 2026-10-05：「核显模式显示策略」新增 igpu_only_ui / igpu_only_asked ——
	// 机器作用域，否则在 A 机器上关掉后换到 B 机器（支持核显模式）也会是关的。
	// 2026-10-05 percore-product：新增每核 CO 的持久化键 —— 这些值只对**装这套设置的这台机器**有意义
	// （Vermeer 桌面机的每核偏移搬到 APU 笔记本上既不可用也危险），所以同样是机器作用域：
	//   * cpu_co_core0..cpu_co_core7 —— 每核 CO 值（8 核桌面如 5700X）；
	//   * cpu_co_all                —— 全核输入框那一份值；
	//   * cpu_co_auto_apply         —— 开机自动应用开关（默认关）。
	// 2026-10-05 uifix（用户要求④）：新增"上次是否正常关机"保底机制的 4 个键，同样是**按机型**作用域
	// （判定状态跟着这台机器的 CO 设置走，不能被别的机型的判定污染）：
	//   * cpu_co_clean_exit         —— 正常退出时写的「干净退出」标记（启动读取后立即清除）；
	//   * cpu_co_guard_armed        —— 保底机制是否已"武装"过（区分首次安装/首次运行）；
	//   * cpu_co_guard_boot         —— 做出本次判定的开机身份；
	//   * cpu_co_guard_verdict      —— 本次开机的判定结论（clean / first_run / abnormal）。
	// 另见下面 IsMachineScopedKey 里的 `cpu_co_` 前缀规则：12/16 核的 Vermeer（如 5900X/5950X）
	// 会用到 cpu_co_core8..15，前缀规则保证任意核号都被覆盖，不必再逐个补进这个数组。
	// 2026-10-06 D-1：新增「试过就记住」相关的 3 个键，同样是**按机型**作用域 ——
	// "这台机器切不切得过去"是硬件事实，跟着机器走，不能被别的机型的结论污染：
	//   * igpu_only_answer     —— 用户对「有没有集显模式这一档」的回答态度（yes / no / unknown）；
	//   * igpu_route_verified  —— 试过一次的结论（1 = 切得过去 / 0 = 切不过去；缺省 = 没试过）；
	//   * igpu_attempt_pending —— 「点了一次核显模式、等下次开机回读」的临时标记。
	// 2026-10-09（igpugate）：igpu_only_ui 的值域扩成 auto/show/hide，并新增一个**同作用域**的迁移标记
	// igpu_only_ui_v2（1 = 这台机器上的 igpu_only_ui 已是新值域；老装机的 show 会先被迁成 auto）。
	// 标记必须与它标记的键同作用域，否则换台机器会读到别的机器留下的标记。
	// 2026-10-09（lightmerge）：新增 light_extra_ui（灯光区「额外汇」三态：auto/show/hide）——
	// 同样是**按机型**作用域："这台机器上灯带行/Logo 行要不要收掉"是硬件事实（有没有真灯带/Logo），
	// 不能被别的机型的结论污染。
	// 2026-10-09（lightsplit，用户原话「额外灯光项应该细分」）：上一条细分**按行两把键** ——
	//   * light_strip_ui —— 「灯带行显示」三态；
	//   * light_logo_ui  —— 「Logo 灯行显示」三态；
	//   * light_extra_ui_v2 —— 上面两把键的**一次性迁移标记**（1 = 旧键 light_extra_ui 已迁移过）。
	// 三把键都跟着机器走（迁移标记同理：换台机器要重新迁一次它自己那份旧值）。
	// 旧键 light_extra_ui 保留在数组里（仍按机型作用域，只读不写、不删用户数据）。
	// 2026-10-10 fmax-persist：「保存 / 读取」+「重置恢复默认」+「开机自动应用」新增的 FMax 两把键，
	// 同样是**按机型**作用域 —— FMax 原值与用户存下的 FMax 都只对**这台机器**有意义（APU 的
	// 邮箱命令组、桌面组的地址与含义都不同），换机器绝不能把 A 机器记下的原值当成 B 机器的默认值
	// 写回去（那正是"拿一个猜的值写硬件"）。两把键都进下面的精确名单：
	//   * cpu_fmax_baseline —— 第一次成功读到的 FMax 原值（「重置」写回它）；
	//   * cpu_fmax_saved    —— 「保存」存下的 FMax 值（「读取」/开机自动应用重放它）。
	private static readonly string[] ExactMachineScopedKeys = new string[29]
	{
		"charge_limit", "gpu_mode", "ec_charge_limit", "igpu_only_ui", "igpu_only_asked",
		"igpu_only_answer", "igpu_route_verified", "igpu_attempt_pending", "igpu_only_ui_v2",
		"light_extra_ui", "light_strip_ui", "light_logo_ui", "light_extra_ui_v2",
		"cpu_co_core0", "cpu_co_core1", "cpu_co_core2", "cpu_co_core3",
		"cpu_co_core4", "cpu_co_core5", "cpu_co_core6", "cpu_co_core7",
		"cpu_co_all", "cpu_co_auto_apply",
		"cpu_co_clean_exit", "cpu_co_guard_armed", "cpu_co_guard_boot", "cpu_co_guard_verdict",
		"cpu_fmax_baseline", "cpu_fmax_saved"
	};

	public static bool IsMachineScopedKey(string key)
	{
		if (!ExactMachineScopedKeys.Contains(key, StringComparer.Ordinal) && !key.StartsWith("fan_profile", StringComparison.Ordinal))
		{
			return key.StartsWith("lc_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("cpu_co_", StringComparison.Ordinal);
		}
		return true;
	}

	public static string ScopedKey(string model, string key)
	{
		return "model." + model + "." + key;
	}

	public static ModelScopeMigration Plan(IReadOnlyDictionary<string, object> source, string model)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		if (source.ContainsKey("model_config_scope"))
		{
			return new ModelScopeMigration(Migrated: false, model, Array.Empty<string>(), Array.Empty<KeyValuePair<string, object>>());
		}
		List<string> list = new List<string>();
		List<KeyValuePair<string, object>> list2 = new List<KeyValuePair<string, object>>();
		foreach (KeyValuePair<string, object> item in source)
		{
			if (IsMachineScopedKey(item.Key))
			{
				list.Add(item.Key);
				list2.Add(new KeyValuePair<string, object>(ScopedKey(model, item.Key), item.Value));
			}
		}
		return new ModelScopeMigration(Migrated: true, model, list, list2);
	}

	public static ModelScopeMigration MigrateToModelScope(string model)
	{
		ArgumentNullException.ThrowIfNull(model, "model");
		ModelScopeMigration modelScopeMigration = Plan(AppConfig.Snapshot(), model);
		if (!modelScopeMigration.Migrated)
		{
			return modelScopeMigration;
		}
		foreach (string removedKey in modelScopeMigration.RemovedKeys)
		{
			AppConfig.Remove(removedKey);
		}
		foreach (KeyValuePair<string, object> scopedValue in modelScopeMigration.ScopedValues)
		{
			Write(scopedValue.Key, scopedValue.Value);
		}
		AppConfig.Set("model_config_scope", modelScopeMigration.Model);
		AppConfig.Flush();
		return modelScopeMigration;
	}

	private static void Write(string key, object? value)
	{
		if (value is int value2)
		{
			AppConfig.Set(key, value2);
		}
		else
		{
			AppConfig.Set(key, value?.ToString() ?? "");
		}
	}
}
