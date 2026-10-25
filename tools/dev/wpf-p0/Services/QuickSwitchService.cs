using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 「更多开关」/快捷磁贴的共享目录 + 状态 + 下发（两窗共用同一把尺子）✅
/// <para>⭐ 解决的原问题：分组/名称表在 <c>OriginalStyleWindow.WideMoreSwitches()</c>（530-575 行）里，
/// 迷你窗只能通过宽屏窗口拿；<c>WideToggleQuickAsync</c> 又直接摸 <c>_backendProbe</c>。</para>
/// <para>⭐ 现在：目录（<see cref="Groups" />）+ 能力判据（<see cref="Supports" />）+ 状态（<see cref="GetState" />）
/// + 下发（<see cref="SetAsync" />）都在服务层，窗口只渲染。</para>
/// <para>⚠️ 迁移点：目录里的中文文案目前仍硬编码在服务层（与原窗口一致）；后续应迁到资源/resx
/// 或由 ViewModel 注入。⛔ 本骨架不改变任何开关行为。</para>
/// </summary>
internal interface IQuickSwitchService
{
    /// <summary>「更多开关」分组目录（顺序 = 渲染顺序）。</summary>
    IReadOnlyList<QuickSwitchGroup> Groups { get; }

    /// <summary>全部开关项（扁平，去分组；供磁贴选择器等使用）。</summary>
    IReadOnlyList<QuickSwitchItem> AllItems { get; }

    /// <summary>本机是否支持该开关（与 WinForms 侧 StaticQuick 对齐）；未连接 ⇒ false。</summary>
    bool Supports(string key);

    /// <summary>读取开关状态；未连接/读不到 ⇒ false。</summary>
    bool GetState(string key);

    /// <summary>下发开关目标值；返回后端确认结果。</summary>
    Task<ServiceResult> SetAsync(string key, bool on);

    /// <summary>翻转（先读当前状态，⛔ 不猜）。</summary>
    Task<ServiceResult> ToggleAsync(string key);
}

/// <inheritdoc />
internal sealed class QuickSwitchService : IQuickSwitchService
{
    private static readonly QuickSwitchGroup[] GroupCatalog =
    {
        new("输入设备", new QuickSwitchItem[]
        {
            new("touchpad", "触摸板"),
            new("wifi", "WiFi"),
            new("bt", "蓝牙"),
            new("webcam", "摄像头"),
            new("micmute", "麦克风静音"),
            new("numpad", "小键盘锁"),
            new("touchpadtoggle", "触摸板切换键")
        }),
        new("键盘与热键", new QuickSwitchItem[]
        {
            new("winkey", "Win键锁"),
            new("fnkey", "Fn键锁"),
            new("capslock", "大小写锁定"),
            new("osd", "OSD提示"),
            new("copilot", "Copilot键锁")
        }),
        new("电源与系统", new QuickSwitchItem[]
        {
            new("usb", "关机USB供电"),
            new("acrecovery", "来电自启"),
            new("highperf", "高性能电源"),
            new("apuboost", "APU 防缩缸"),
            new("fanboost", "风扇强冷"),
            new("deepsleep", "深度睡眠"),
            new("gamewhitelist", "游戏白名单"),
            new("cpuadvperf", "解锁BIOS菜单"),
            new("taskbarautohide", "任务栏自动隐藏"),
            new("transparency", "系统透明效果"),
            new("darktheme", "系统深色主题"),
            new("monitoroff", "息屏（不睡眠）"),
            new("winupdate", "禁用Win更新")
        }),
        new("睡眠省电", new QuickSwitchItem[]
        {
            new("sleepnetoff", "待机断开网络"),
            new("sleepaway", "封杀离开模式"),
            new("sleepsysreq", "忽略系统策略"),
            new("sleeprtcwake", "禁用RTC唤醒"),
            new("sleepdeep", "ACPI深度睡眠"),
            new("sleephibernate", "30分钟转休眠")
        })
    };

    private static readonly QuickSwitchItem[] FlatCatalog =
        GroupCatalog.SelectMany(static group => group.Items).ToArray();

    private readonly BackendAccessor _backend;

    internal QuickSwitchService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public IReadOnlyList<QuickSwitchGroup> Groups => GroupCatalog;

    public IReadOnlyList<QuickSwitchItem> AllItems => FlatCatalog;

    public bool Supports(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return false;
        }
        return probe.SupportsQuick(key) || IsBaolongdaFanBoost(key);
    }

    public bool GetState(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return false;
        }
        // 宝龙达强冷状态在 MechrevoHw.FanBoostActive（本机通道），probe.QuickState 读的是 GCU 字段。
        if (IsBaolongdaFanBoost(key))
        {
            return MechrevoHw.FanBoostActive;
        }
        return probe.QuickState(key);
    }

    public async Task<ServiceResult> SetAsync(string key, bool on)
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return ServiceResult.Fail("后端未连接");
        }
        if (!Supports(key))
        {
            return ServiceResult.Fail("本机不支持该开关：" + key);
        }
        try
        {
            bool ok;
            if (IsBaolongdaFanBoost(key))
            {
                MechrevoHw? hw = _backend.Hw;
                if (hw == null)
                {
                    return ServiceResult.Fail("后端未连接");
                }
                bool handled = hw.TrySetFanBoostViaBackend(on, out bool confirmed);
                if (!handled)
                {
                    return ServiceResult.Fail("本机强冷通道不可用");
                }
                return confirmed
                    ? ServiceResult.Success(on ? "强冷已开启（回读确认）" : "强冷已关闭（已恢复原曲线）")
                    : ServiceResult.Fail(on ? "强冷已下发但回读未确认" : "强冷关闭未确认");
            }
            ok = await probe.SwitchQuickAsync(key, on).ConfigureAwait(false);
            return ok
                ? ServiceResult.Success(on ? "已开启" : "已关闭")
                : ServiceResult.Fail("下发未确认：" + key);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail("下发异常：" + ex.GetType().Name);
        }
    }

    public async Task<ServiceResult> ToggleAsync(string key)
    {
        return await SetAsync(key, !GetState(key)).ConfigureAwait(false);
    }

    private bool IsBaolongdaFanBoost(string key)
    {
        return string.Equals(key, "fanboost", StringComparison.OrdinalIgnoreCase) && _backend.IsBaolongda;
    }
}
