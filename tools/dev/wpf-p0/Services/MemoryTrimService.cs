using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace L_Mechrevo.WpfP0.Services;

/// <summary>
/// 后台内存回收：窗口全部隐藏/最小化且没有传感器消费者时，把工作集交还系统。
/// <para>只回收工作集（可再次缺页调回），不改变进程私有提交；前台界面显示时不调用。</para>
/// </summary>
internal static class MemoryTrimService
{
    private static long _lastTrimTick;

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    internal static void TrimIfIdle(bool idle)
    {
        if (!idle)
        {
            return;
        }
        long now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastTrimTick) < 30000)
        {
            return;
        }
        if (Interlocked.Exchange(ref _lastTrimTick, now) == now)
        {
            return;
        }
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch
        {
        }
    }
}
