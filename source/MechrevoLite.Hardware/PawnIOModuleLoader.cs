using System;
using System.IO;
using MechrevoLite;

namespace MechrevoLite.Hardware;

/// <summary>
/// PawnIO 模块从安装目录 PawnIO\ 读取，不再内嵌进 MechrevoLite.Core.dll。
/// 这样 Core 不再包含“托管程序集里藏驱动模块”的形态。
/// </summary>
internal static class PawnIOModuleLoader
{
    internal static Stream? Open(string fileName)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string path = Path.Combine(baseDir, "PawnIO", fileName);
            if (File.Exists(path))
            {
                return File.OpenRead(path);
            }
            string alt = Path.Combine(baseDir, fileName);
            if (File.Exists(alt))
            {
                return File.OpenRead(alt);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("[pawnio] 读取外部模块失败：" + fileName + " · " + ex.Message);
        }
        return null;
    }
}
