GCU 独立安装包（机械革命 ControlCenterX 服务端中继）
=====================================================

重要：本包安装的是【后台服务】，没有任何界面、图标或可运行的程序。
安装成功后直接运行 L-Mechrevo.exe 使用即可（控制中心功能即服务已就绪）。

安装位置
--------
安装脚本会先校验厂商签名，再把服务复制到：
  %ProgramFiles%\L-Mechrevo\GCU
服务不再从下载目录、桌面或 OneDrive 目录直接运行。

组件（自足，不依赖官方 UI）
----------------------------
  AiStoneService\GCUBridge.exe      MQTT broker + 服务宿主（Windows 服务）
  AiStoneService\MyControlCenter\   GCUService.exe 硬件执行体及全部依赖
  UWACPIDriver\                     EC 访问内核驱动（ACPI\INOU0000, KMDF）

=====================================================
完整替换流程（推荐顺序）
=====================================================
1. 解压本包到任意临时目录
2. 右键 install.bat → 以管理员身份运行
   （验签 + 安装驱动 + 注册服务；若已有 GCUBridge 服务，脚本会安全更新路径）
3. 验证：运行 L-Mechrevo，任意切换一次性能模式，观察风扇/模式生效
4. 确认 L-Mechrevo 功能正常后，卸载官方控制台：
     设置 → 应用 → 机械革命控制中心 → 卸载
   或运行本包内的「清理官方控制台.bat」（保留 GCU 删其余）
5. 官方卸载后再次运行本包 install.bat（卸载器可能移除服务/驱动，重装一次即可）
6. 再次验证 L-Mechrevo 完整功能

安装失败排查
------------
  - 脚本会输出具体报错，截图反馈
  - 检查杀毒软件是否拦截 GCUBridge.exe

注意
----
  - 本包不包含原版 CCUWinUI 界面，仅服务端中继。
  - 安装时会创建入站阻止规则，禁止局域网访问本机 MQTT 端口 13688。
  - 厂商服务和驱动不属于 L-Mechrevo 的 GPL 源码，分发前须确认授权。
  - 卸载本包：uninstall.bat（停/删服务；驱动保留无害）。


补充说明：本基础包不含 GCUService.exe / UEFI_Firmware.dll / NVControlSetting.dll（由 payload\40 或 payload\50 按机型先覆盖提供）。
