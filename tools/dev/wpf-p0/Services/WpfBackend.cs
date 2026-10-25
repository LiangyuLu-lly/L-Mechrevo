using System;
using MechrevoLite;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ WPF 侧唯一后端宿主（P0-1/P0-2）：窗口与旧程序集之间的唯一静态桥。
/// <para>旧 WinForms 业务程序集内部仍读 <c>Program.hw/service</c>；本类在赋值时同步写回，
/// 保证 WPF 与旧逻辑共享同一后端实例。</para>
/// <para>⛔ 新 UI 不再直接读写 <c>Program.hw/service</c>；只允许经由本类。</para>
/// </summary>
internal static class WpfBackend
{
    private static MechrevoHw? _hw;
    private static MechrevoService? _service;

    internal static MechrevoHw? Hw
    {
        get => _hw;
        set
        {
            _hw = value;
            MechrevoLite.Program.hw = value;
        }
    }

    internal static MechrevoService? Service
    {
        get => _service;
        set
        {
            _service = value;
            MechrevoLite.Program.service = value;
        }
    }

    internal static bool IsAttached => _hw != null && _service != null;

    internal static void Attach(MechrevoHw? hw, MechrevoService? service)
    {
        Hw = hw;
        Service = service;
    }
}
