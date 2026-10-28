# InpOut32/64 (Highresolution Enterprises) — 参考存档

- 上游：https://www.highrez.co.uk/Downloads/InpOut32 （InpOutBinaries_1501 / Inpout32_dll_source_1501）
- 版本：DLL 1.5.0.0 · 驱动 1.2 x64
- 许可：MIT 式（见同目录 `license.txt`：允许复制/分发/再许可/销售，保留版权声明即可）
- 用途：无界（WUJIE14XA）风扇 I2EC 写入（端口 0x681/0x682/0x683），以及无界电池电压修复的端口 I/O。

## 目录内容

| 文件 | 说明 |
|---|---|
| `inpout32drv.cpp` / `.h` / `.def` / `.rc` / `.vcproj` / `.sln` | 用户态 DLL 源码（VS2008 工程，Win32/x64 共用） |
| `inpout32.sys` / `inpoutx64.sys` | 内核驱动二进制（已签名，原样保留） |
| `InstallDriver/` | 驱动安装器源码 |
| `InpOut32.Net/` | C#/VB.NET 调用示例（已排除各 `bin` / `obj` 下重复的二进制） |
| `license.txt` / `ReadMe.txt` | 许可与说明 |

## 签名

`inpoutx64.sys` SHA256 = `F8965FDCE668692C3785AFA3559159F9A18287BC0D53ABB21902895A8ECF221B`；
Authenticode = Valid（签名者 Red Fox UK Limited，证书 2009 年过期，但带 VeriSign 时间戳）；
在 Windows 11 + Secure Boot 开启的实机上已确认可加载（开发机 2026-10-22 验证）。

## LM 内的接入

- 安装包携带 `inpoutx64.dll`（`{app}`）与 `inpoutx64.sys`（`{app}\inpoutx64\`）；
- `Install-InpOut.ps1` 只在无界机型上注册/启动 `inpoutx64` 服务（幂等；失败不阻断安装）；
- `source\MechrevoLite.Hardware\I2ecInpOut.cs` 从 `{app}` 加载 DLL，用于 I2EC 读写。