using System.Threading;
using MechrevoLite.Hardware;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐⭐ 共享后端句柄（服务层唯一可变槽位）✅
/// <para>⭐ 设计：连接仍由窗口/App 发起（<see cref="BackendProbe.ConnectAsync" />），
/// 服务层**不负责建连、不负责 Dispose**；这里只共享**同一个** <see cref="BackendProbe" /> 实例，
/// 避免宽屏/迷你窗各连一次后端。</para>
/// <para>⚠️ 窗口在连接成功后调用 <see cref="Attach" />；服务通过 <see cref="Probe" />
/// （<c>Volatile</c> 读写）读取。⚠️ 连接线程与 UI 线程都会访问 ⇒ 引用字段用 <see cref="Volatile" /> 保护
/// （单次引用读写本身就是原子操作，这里只是防重排/可见性）。</para>
/// <para>⛔ 本类不持有任何窗口、控件或 Dispatcher 引用 ⇒ 可以安全地在两个窗口间共享。</para>
/// </summary>
internal sealed class BackendAccessor
{
    private BackendProbe? _probe;

    /// <summary>当前后端探针；<c>null</c> = 尚未建连（服务方法必须按“未连接”降级）。</summary>
    internal BackendProbe? Probe
    {
        get => Volatile.Read(ref _probe);
        set => Volatile.Write(ref _probe, value);
    }

    /// <summary>后端是否已建连（<see cref="BackendProbe.Connected" />）。</summary>
    internal bool IsAvailable => Probe is { Connected: true };

    /// <summary>硬件层入口（未建连时 <c>null</c>）。</summary>
    internal MechrevoHw? Hw => Probe?.Hw;

    /// <summary>业务层入口（未建连时 <c>null</c>）。</summary>
    internal MechrevoService? Service => Probe?.Service;

    /// <summary>是否宝龙达机型（服务层做 GCU/本机通道分派时的共用判据）。</summary>
    internal bool IsBaolongda => Probe?.IsBaolongda == true;

    /// <summary>窗口在 <c>ConnectAsync</c> 成功后挂上探针（同一个实例只挂一次）。</summary>
    internal void Attach(BackendProbe? probe)
    {
        Probe = probe;
    }

    /// <summary>窗口关闭/重连前摘掉探针；⛔ 不 Dispose 探针（所有权在窗口）。</summary>
    internal void Detach()
    {
        Probe = null;
    }
}
