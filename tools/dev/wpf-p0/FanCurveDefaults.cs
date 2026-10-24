using System;
using System.Linq;

namespace L_Mechrevo.WpfP0;

/// <summary>GCU 自定义档的默认风扇曲线（M4T5 基准表）。</summary>
internal static class FanCurveDefaults
{
    internal static readonly byte[] CpuTemperatures =
    {
        0, 54, 59, 63, 67, 67, 70, 73, 76, 81, 83, 255, 255, 255, 255, 255
    };

    internal static readonly int[] CpuDuties =
    {
        0, 30, 30, 35, 45, 45, 50, 55, 70, 80, 90, 90, 90, 90, 90, 90
    };

    internal static readonly byte[] GpuTemperatures =
    {
        0, 54, 54, 58, 62, 62, 64, 67, 70, 73, 76, 255, 255, 255, 255, 255
    };

    internal static readonly int[] GpuDuties =
    {
        0, 0, 30, 35, 45, 45, 50, 55, 70, 80, 90, 90, 90, 90, 90, 90
    };

    internal static byte[] CpuDutyBytes => CpuDuties.Select(v => (byte)Math.Clamp(v, 0, 100)).ToArray();

    internal static byte[] GpuDutyBytes => GpuDuties.Select(v => (byte)Math.Clamp(v, 0, 100)).ToArray();
}
