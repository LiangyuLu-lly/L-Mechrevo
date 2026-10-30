using System;
using System.Threading.Tasks;
using L_Mechrevo.WpfP0;

using L_Mechrevo.WpfP0.Resources;
namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// ⭐ WPF 后端生命周期宿主（P0-1）：唯一负责 BackendProbe 的创建、连接超时、替换与释放。
/// <para>窗口只持有本宿主并订阅结果；不直接 new / Dispose BackendProbe。</para>
/// <para>⛔ 不重建上层 MechrevoHw/Service；连接成功后由 <see cref="WpfBackend"/> 统一发布。</para>
/// </summary>
internal sealed class BackendHost : IDisposable
{
    private bool _disposed;

    internal L_Mechrevo.WpfP0.BackendProbe? Probe { get; private set; }

    internal string LastError { get; private set; } = "";

    internal bool TimedOut { get; private set; }

    internal bool Connected => Probe?.Connected == true;

    /// <summary>创建并连接一个新的 BackendProbe；超时/失败会自动释放本轮探针。</summary>
    internal async Task<bool> ConnectAsync(TimeSpan timeout)
    {
        if (_disposed)
        {
            LastError = WpfStrings.L("BackendHost 已释放", "BackendHost disposed");
            return false;
        }
        DisposeProbe();
        TimedOut = false;
        LastError = "";
        BackendProbe probe = new();
        Probe = probe;
        try
        {
            Task<bool> connect = probe.ConnectAsync();
            Task completed = await Task.WhenAny(connect, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != connect)
            {
                TimedOut = true;
                LastError = WpfStrings.L("连接超时（", "Connection timed out (") + (int)timeout.TotalSeconds + WpfStrings.L(" 秒）", " s)");
                if (probe.TryEnableNucUmiLocalReady())
                {
                    WpfBackend.Attach(probe.Hw, probe.Service);
                    LastError = "";
                    return true;
                }
                DisposeProbe();
                return false;
            }
            bool ok = await connect.ConfigureAwait(false);
            if (!ok)
            {
                if (probe.TryEnableNucUmiLocalReady())
                {
                    WpfBackend.Attach(probe.Hw, probe.Service);
                    LastError = "";
                    return true;
                }
                LastError = probe.LastError;
                DisposeProbe();
                return false;
            }
            LastError = "";
            WpfBackend.Attach(probe.Hw, probe.Service);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            DisposeProbe();
            return false;
        }
    }

    private void DisposeProbe()
    {
        try
        {
            Probe?.Dispose();
        }
        catch
        {
        }
        Probe = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeProbe();
    }
}
