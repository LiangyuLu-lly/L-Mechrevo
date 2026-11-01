# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 05:20] 打包完成 + 一条重要教训（字节搜索不可靠）
- **打包结果**：`installer-src\\Output\\LM-0.290.3-beta21.3-setup.exe`
  **49.69 MB**（旧版 18.3 = 63.58 MB -> **省 13.89 MB**）；版本资源 0.290.3 ✅
  ISCC 编译成功（14.97 秒），**所有 .iss 引用均已落地**（编译成功即证明）
- **NoGCU**：用户要求「NOGCU不打包」-> 已把误产出的
  `LM-0.290.3-beta21.3-NoGCU-setup.exe` 移到 `backup\\` ✅
  （两个 .iss 的版本号都已更新，留着以后要打时用）
- **zh-CN 文件夹解释**（用户问）：.NET **本地化卫星程序集**
  `zh-CN\\L-Mechrevo.resources.dll` + 第三方
  `Microsoft.Win32.TaskScheduler.resources.dll`；
  主 DLL 只含**中性（英文）**资源，中文靠卫星，运行时按 CurrentUICulture 选；
  本次 `ModeConfigTiny` 两个 resx 都改了 -> **两个必须一起发**（少一个中文界面会显示英文）
  核实：`C:\\Program Files\\L-Mechrevo\\zh-Hant` 是**空目录**残留，源码无 zh-Hant 资源 ✅
- ⚠️ **重要教训：不要用字节搜索判断 DLL 里有没有某个资源键**
  * 我先用 ASCII/UTF-16 搜 `ModeConfigTiny` -> 卫星里"没搜到"，
    于是在 resx / obj / csproj 上排了一长串假设（注释导致 DataSet 停读、编译中间产物陈旧…）
    甚至把两个 resx 里的注释删了（**这是无谓改动**）
  * 用 `System.Resources.ResourceReader` 枚举键名后真相大白：
    中间 .resources **和**最终 DLL/卫星**都一直是对的**，703 个键齐全
  * 之前"主 DLL 里有 ModeConfigTiny（ascii 命中）"其实是 **C# 属性名**，属**假阳性**；
    "卫星里没有"则是**假阴性**。**字节搜索两头都会骗人** ✅
  -> 以后核验资源：**用 ResourceReader 枚举键/取值**，不要搜字节
- 已做核验（反射枚举）：
  主 DLL `ModeConfigTiny='Cfg'`、`ModeConfigShort='Config'`（703 键）
  zh-CN 卫星 `ModeConfigTiny='配置'`、`ModeConfigShort='配置'`（703 键）✅
- 备份：backup\\Strings_20261005_pre_strip.resx、Strings.zh-CN_20261005_pre_strip.resx、
        LM-0.290.3-beta21.3-NoGCU-setup.exe
- **本次三项任务全部完成**：①版本 0.290.3 beta21.3 + 更新/修改日志
  ②打包目录冗余检查（清 12 个 + 修 28 条失效 .iss 引用）③合并并打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
