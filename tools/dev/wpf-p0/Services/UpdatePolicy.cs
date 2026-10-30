namespace L_Mechrevo.WpfP0.Services;

/// <summary>WPF 侧更新开关（不再依赖旧 WinForms UpdateForm 的 UI 常量）。</summary>
internal static class UpdatePolicy
{
    /// <summary>与旧 UI 保持一致的暂时隐藏更新入口策略；后续由设置页/远端开关接管。</summary>
    internal const bool TemporarilyHidden = false;
}
