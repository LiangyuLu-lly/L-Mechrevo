namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐⭐ WPF 共享业务服务层的**组合根**（宽屏 / 迷你窗共用同一实例）✅
/// <para>⭐ 用法（迁移目标，本骨架先把接口就位）：</para>
/// <code>
/// // 窗口构造：
/// _services = new AppServices();
/// // 后端连接成功后：
/// _services.AttachBackend(probe);      // 只挂同一个 BackendProbe，⛔ 不重建连接
/// // 之后窗口只依赖接口：
/// SensorSnapshot s = _services.Sensors.Read();
/// await _services.Performance.SetModeAsync(0);
/// </code>
/// <para>⚠️ **所有权**：<see cref="BackendProbe" /> 的生命周期仍归窗口/App（<c>ConnectAsync</c> 与
/// <c>Dispose</c> 都在窗口侧）；本类不 Dispose 探针，避免两个窗口同时持有时的双重释放。</para>
/// <para>⚠️ 线程：<see cref="BackendAccessor" /> 用 <c>Volatile</c> 发布探针；服务本身无状态/无锁，
/// 可在 UI 线程与后台线程调用（异步方法内部均已 <c>ConfigureAwait(false)</c>）。</para>
/// </summary>
internal sealed class AppServices
{
    private readonly BackendAccessor _backendAccessor;

    /// <param name="backend">已连接（或已创建）的后端探针；未知时可传 <c>null</c>，之后 <see cref="AttachBackend" />。</param>
    internal AppServices(BackendProbe? backend = null)
    {
        _backendAccessor = new BackendAccessor();
        if (backend != null)
        {
            _backendAccessor.Attach(backend);
        }
        BackendStatus = new BackendStatusService(_backendAccessor);
        Sensors = new SensorService(_backendAccessor);
        Performance = new PerformanceService(_backendAccessor);
        Gpu = new GpuService(_backendAccessor);
        QuickSwitches = new QuickSwitchService(_backendAccessor);
        Display = new DisplayService(_backendAccessor);
        Battery = new BatteryService(_backendAccessor);
        ChargeLimit = new ChargeLimitService(_backendAccessor);
        Lighting = new LightingService(_backendAccessor);
        TrayMenu = new TrayMenuService();
    }

    /// <summary>共享后端句柄（诊断用；⛔ 业务代码优先用下面各接口）。</summary>
    internal BackendAccessor Backend => _backendAccessor;

    internal IBackendStatusService BackendStatus { get; }

    internal ISensorService Sensors { get; }

    internal IPerformanceService Performance { get; }

    internal IGpuService Gpu { get; }

    internal IQuickSwitchService QuickSwitches { get; }

    internal IDisplayService Display { get; }

    internal IBatteryService Battery { get; }

    internal IChargeLimitService ChargeLimit { get; }

    internal ILightingService Lighting { get; }

    internal ITrayMenuService TrayMenu { get; }

    /// <summary>窗口连接成功后挂载后端（同一个实例；⛔ 不 CreateAsync/ConnectAsync）。</summary>
    internal void AttachBackend(BackendProbe? backend)
    {
        _backendAccessor.Attach(backend);
    }

    /// <summary>窗口关闭/重连前摘掉后端（⛔ 不 Dispose 探针）。</summary>
    internal void DetachBackend()
    {
        _backendAccessor.Detach();
    }
}
