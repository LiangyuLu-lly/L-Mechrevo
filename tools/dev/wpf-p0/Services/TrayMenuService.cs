using System.Collections.Generic;
using System.Linq;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 托盘菜单配置（目录 / 读取 / 保存；两窗共享）✅
/// <para>⭐ 解决的原问题：<see cref="TrayMenuConfig" /> 与 <c>SettingsWindow.BuildTrayMenuOptions()</c>
/// （221-245 行）各直接读写 config；磁贴/迷你窗若也要改托盘菜单会再复制一份。</para>
/// <para>⚠️ 迁移点：托盘菜单的**运行时状态**（<c>TrayMenuState</c>、重建菜单）仍在
/// <c>TrayIconService</c>；本服务只服务“配置项”，不碰 WinForms <c>ContextMenuStrip</c>。</para>
/// </summary>
internal interface ITrayMenuService
{
    /// <summary>可配置的托盘菜单项（顺序 = 菜单里的固定顺序）。</summary>
    IReadOnlyList<TrayMenuItem> Catalog { get; }

    /// <summary>读取启用集合（缺省 = 全开，与 <see cref="TrayMenuConfig.Load" /> 一致）。</summary>
    HashSet<string> LoadEnabled();

    /// <summary>单项是否启用。</summary>
    bool IsEnabled(string key);

    /// <summary>保存启用集合（按 <see cref="Catalog" /> 顺序归一化后落盘）。</summary>
    ServiceResult SaveEnabled(IEnumerable<string> keys);
}

/// <inheritdoc />
internal sealed class TrayMenuService : ITrayMenuService
{
    public IReadOnlyList<TrayMenuItem> Catalog =>
        TrayMenuConfig.Catalog.Select(static item => new TrayMenuItem(item.Key, item.Label)).ToArray();

    public HashSet<string> LoadEnabled()
    {
        try
        {
            return TrayMenuConfig.Load();
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsEnabled(string key)
    {
        try
        {
            return TrayMenuConfig.IsEnabled(key);
        }
        catch
        {
            return false;
        }
    }

    public ServiceResult SaveEnabled(IEnumerable<string> keys)
    {
        try
        {
            TrayMenuConfig.Save(keys);
            return ServiceResult.Success(WpfStrings.L("托盘菜单已保存", "Tray menu saved"));
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(WpfStrings.L("托盘菜单保存异常：", "Failed to save tray menu: ") + ex.GetType().Name);
        }
    }
}
