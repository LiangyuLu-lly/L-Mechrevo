using MechrevoLite.Hardware;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ 后端连接/能力状态（宽屏与迷你窗共享同一份事实）✅
/// <para>⭐ 解决的原问题：窗口各自读 <c>_backendProbe</c> 私有字段并各自拼“已连接/未连接”文案
/// ⇒ 同一个后端状态在两处可能不一致。</para>
/// </summary>
internal interface IBackendStatusService
{
    /// <summary>本次连接是否成功（<see cref="BackendProbe.Connected" />）。</summary>
    bool Connected { get; }

    /// <summary>本进程是否曾经连上过后端（用于“重连中”与首连失败区分）。</summary>
    bool EverConnected { get; }

    /// <summary>是否宝龙达机型（无 GCU 的本机 WMI 后端）。</summary>
    bool IsBaolongda { get; }

    /// <summary>GCU 是否已连接。</summary>
    bool IsGcuConnected { get; }

    /// <summary>是否正在重连。</summary>
    bool IsReconnecting { get; }

    /// <summary>是否非 GCU 后端（宝龙达/老接口）。</summary>
    bool IsNonGcuBackend { get; }

    /// <summary>当前后端类型。</summary>
    MechrevoBackendKind ActiveBackend { get; }

    /// <summary>能力位快照（未连接时 <c>null</c>）。</summary>
    MechrevoDeviceCapabilities? Capabilities { get; }

    /// <summary>最近一次连接错误（空串 = 无错误）。</summary>
    string LastError { get; }

    /// <summary>一行诊断文本（原 <c>BackendLine.Text</c> 的数据源，⛔ 不含控件）。</summary>
    string SnapshotText();
}

/// <inheritdoc />
internal sealed class BackendStatusService : IBackendStatusService
{
    private readonly BackendAccessor _backend;

    internal BackendStatusService(BackendAccessor backend)
    {
        _backend = backend;
    }

    public bool Connected => _backend.Probe?.Connected == true;

    public bool EverConnected => _backend.Probe?.EverConnected == true;

    public bool IsBaolongda => _backend.Probe?.IsBaolongda == true;

    public bool IsGcuConnected => _backend.Probe?.IsGcuConnected == true;

    public bool IsReconnecting => _backend.Probe?.IsReconnecting == true;

    public bool IsNonGcuBackend => _backend.Probe?.IsNonGcuBackend == true;

    public MechrevoBackendKind ActiveBackend => _backend.Probe?.ActiveBackend ?? MechrevoBackendKind.None;

    public MechrevoDeviceCapabilities? Capabilities => _backend.Probe?.Capabilities;

    public string LastError => _backend.Probe?.LastError ?? "";

    public string SnapshotText()
    {
        BackendProbe? probe = _backend.Probe;
        if (probe == null)
        {
            return WpfStrings.L("未连接", "Not connected");
        }
        try
        {
            return probe.Snapshot();
        }
        catch
        {
            return WpfStrings.L("后端状态读取异常", "Failed to read backend status");
        }
    }
}
