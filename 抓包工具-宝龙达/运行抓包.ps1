# ============================================================================
#  宝龙达（Baolongda）机型 —— 独立抓包脚本
# ============================================================================
#  用途：给「机械革命 蛟龙16Pro（2022 / 2023 / 2024 年款）」这类**宝龙达模具**机型
#        采集适配所需的证据。⭐ **不需要装 L-Mechrevo**，本脚本自己把该读的都读出来。
#
#  怎么用：⭐ 右键此文件 → 「使用 PowerShell 运行」
#          （若被拦，见同目录《怎么运行.txt》）
#
#  ⛔ 安全承诺（脚本里也确实什么都没写）：
#      · ⛔ 不写任何硬件寄存器   · ⛔ 不写注册表   · ⛔ 不改任何设置
#      · ⛔ 不装任何驱动         · ⛔ 不联网上传   · ✅ 只**读取**信息，写进一个文件夹
#
#  产物：同目录下 `宝龙达抓包-<日期时间>\` 文件夹，压缩成 zip 发回即可。
# ============================================================================

$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$outRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ("宝龙达抓包-" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

function Write-Section {
	param([string]$FileName, [string]$Title, [scriptblock]$Body)
	$path = Join-Path $outRoot $FileName
	$lines = New-Object System.Collections.Generic.List[string]
	$lines.Add("=== $Title ===")
	$lines.Add("采集时间: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
	$lines.Add("")
	try {
		& $Body $lines
	} catch {
		$lines.Add("")
		$lines.Add("⚠️ 本节采集失败（已跳过，不影响其它小节）: " + $_.Exception.Message)
	}
	[System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($true)))
	Write-Host ("  ✅ " + $FileName) -ForegroundColor Green
}

function Add-Line { param($Lines, [string]$Text) $Lines.Add([string]$Text) }

Write-Host ""
Write-Host "宝龙达机型抓包 —— 开始" -ForegroundColor Cyan
Write-Host "输出到: $outRoot" -ForegroundColor DarkGray
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 1. 机器身份（⭐ 最重要：我们要知道这是哪台机、什么模具）
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '01-机器信息.txt' '机器身份与型号' {
	param($L)
	Add-Line $L "── 系统 ──"
	Add-Line $L ("计算机名        : " + $env:COMPUTERNAME)
	Add-Line $L ("Windows 版本    : " + (Get-CimInstance Win32_OperatingSystem).Caption)
	Add-Line $L ("Windows 内部版本: " + (Get-CimInstance Win32_OperatingSystem).Version)
	Add-Line $L ("系统架构        : " + $env:PROCESSOR_ARCHITECTURE)
	Add-Line $L ("当前用户是否管理员: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
	Add-Line $L ""

	Add-Line $L "── 整机（WMI Win32_ComputerSystem / BaseBoard / BIOS）──"
	foreach ($q in @(
		@('Win32_ComputerSystem', 'Manufacturer,SystemFamily,Model,SystemSKU,PCSystemType'),
		@('Win32_BaseBoard',      'Manufacturer,Product,Version,SerialNumber'),
		@('Win32_BIOS',           'Manufacturer,SMBIOSBIOSVersion,Version,ReleaseDate'),
		@('Win32_ComputerSystemProduct', 'Vendor,Name,Version,IdentifyingNumber')
	)) {
		try {
			$o = Get-CimInstance -ClassName $q[0] -ErrorAction Stop
			Add-Line $L ("  [" + $q[0] + "]")
			foreach ($p in ($q[1] -split ',')) { Add-Line $L ("    " + $p.Trim() + " = " + ($o.($p.Trim()))) }
		} catch { Add-Line $L ("  [" + $q[0] + "] 读取失败: " + $_.Exception.Message) }
	}
	Add-Line $L ""

	Add-Line $L "── CPU / GPU ──"
	try { Get-CimInstance Win32_Processor | ForEach-Object { Add-Line $L ("  CPU: " + $_.Name + "  (" + $_.NumberOfCores + "核/" + $_.NumberOfLogicalProcessors + "线程)") } } catch {}
	try { Get-CimInstance Win32_VideoController | ForEach-Object { Add-Line $L ("  GPU: " + $_.Name + "  driver=" + $_.DriverVersion + "  PNP=" + $_.PNPDeviceID) } } catch {}
	Add-Line $L ""

	Add-Line $L "── 平台/项目码线索（⭐ 关键：宝龙达机型的识别依据）──"
	foreach ($k in @(
		'HKLM:\HARDWARE\DESCRIPTION\System\BIOS',
		'HKLM:\SYSTEM\CurrentControlSet\Control\SystemInformation'
	)) {
		Add-Line $L ("  [" + $k + "]")
		try {
			$item = Get-ItemProperty -Path $k -ErrorAction Stop
			$item.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object { Add-Line $L ("    " + $_.Name + " = " + $_.Value) }
		} catch { Add-Line $L "    （读不到）" }
	}
	Add-Line $L ""
	Add-Line $L "── 厂商控制台注册表（看它认哪套接口）──"
	foreach ($k in @(
		'HKLM:\SOFTWARE\OEM\GamingCenter2',
		'HKLM:\SOFTWARE\OEM\GamingCenter',
		'HKLM:\SOFTWARE\OEM\ControlCenter',
		'HKLM:\SOFTWARE\OEM\UniwillConsole',
		'HKLM:\SOFTWARE\OEM'
	)) {
		Add-Line $L ("  [" + $k + "] " + $(if (Test-Path $k) { "存在" } else { "不存在" }))
		if (Test-Path $k) {
			try { Get-ChildItem $k -ErrorAction SilentlyContinue | ForEach-Object { Add-Line $L ("    子键: " + $_.PSChildName) } } catch {}
		}
	}
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. ACPI / WMI 接口探测（⭐ 宝龙达适配的核心：那套接口长什么样）
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '02-ACPI-WMI接口.txt' 'ACPI / WMI 接口探测（本机有哪些厂商接口）' {
	param($L)
	Add-Line $L "── root\WMI 下所有类（⭐ 这是找宝龙达接口的主战场）──"
	try {
		$classes = Get-CimClass -Namespace 'root\WMI' -ErrorAction Stop | Sort-Object CimClassName
		Add-Line $L ("  类总数 = " + @($classes).Count)
		foreach ($c in $classes) { Add-Line $L ("    " + $c.CimClassName) }
	} catch { Add-Line $L ("  读取失败: " + $_.Exception.Message) }
	Add-Line $L ""

	Add-Line $L "── ⭐ 可疑项的详情（含 GUID / 方法 / 属性）──"
	# ⚠️ 关键词收紧：强相关 = 厂商/OEM 专属味道；弱相关 = 常见同类（仅供参考），避免把一堆 Windows 通用事件类当重点
	$strong = @('MICommon','MiInterface','MIFS','AISTONE','AiStone','Uniwill','Mechrevo','BAOLONG','Baolong','BLD_','Oem','OEM','GamingCenter','ControlCenter','EC_','EcCtrl','MyEc','AcpiCtrl','WMIACPI')
	$weak   = @('Fan','Battery','Thermal','Power','Brightness','Keyboard','Led','RGB')
	try {
		$all = Get-CimClass -Namespace 'root\WMI' -ErrorAction Stop
		$strongHits = 0
		$weakHits = 0
		foreach ($c in $all) {
			$name = $c.CimClassName
			$isStrong = $false; $isWeak = $false
			foreach ($kw in $strong) { if ($name -like ("*" + $kw + "*")) { $isStrong = $true; break } }
			if (-not $isStrong) { foreach ($kw in $weak) { if ($name -like ("*" + $kw + "*")) { $isWeak = $true; break } } }
			if (-not ($isStrong -or $isWeak)) { continue }
			if ($isStrong) { $strongHits++ } else { $weakHits++ }
			Add-Line $L ("  " + $(if ($isStrong) { "⭐⭐" } else { "  ⭐" }) + " " + $name + $(if ($isStrong) { "   <-- 强相关" } else { "   <-- 弱相关（同类里常见，仅供参考）" }))
			try {
				$guid = $c.CimClassQualifiers['Guid']
				if ($guid) { Add-Line $L ("      GUID = " + $guid.Value) }
			} catch {}
			try { foreach ($m in $c.CimClassMethods) { Add-Line $L ("      方法: " + $m.Name + "(" + (($m.Parameters | ForEach-Object { $_.Name }) -join ', ') + ")") } } catch {}
			try { foreach ($p in $c.CimClassProperties) { Add-Line $L ("      属性: " + $p.Name + " : " + $p.CimType) } } catch {}
		}
		Add-Line $L ""
		Add-Line $L ("  ⭐ 统计：强相关 " + $strongHits + " 个 · 弱相关 " + $weakHits + " 个")
		Add-Line $L "  ⚠️ 若**强相关 = 0**：说明 root\WMI 里没有厂商专属类 ==> 那套接口可能在**别的命名空间**，"
		Add-Line $L "     或在**纯 ACPI 方法**里。请把本文件整份发回（我们要看完整类名列表与下面的设备树）。"
	} catch { Add-Line $L ("  读取失败: " + $_.Exception.Message) }
	Add-Line $L ""

	Add-Line $L "── root\WMI 之外可能相关的命名空间/类 ──"
	foreach ($ns in @('root\CIMV2', 'root\WMI', 'root\DEFAULT', 'root\OEM')) {
		try {
			$cs = Get-CimClass -Namespace $ns -ErrorAction Stop | Where-Object { $_.CimClassName -match 'Mechrevo|AiStone|Uniwill|Gaming|Control|Fan|Battery|EC_|OEM|Baolong|BLD' }
			Add-Line $L ("  [" + $ns + "] 命中 " + @($cs).Count + " 个:")
			foreach ($c in $cs) { Add-Line $L ("    " + $c.CimClassName) }
		} catch { Add-Line $L ("  [" + $ns + "] 读不到: " + $_.Exception.Message) }
	}
	Add-Line $L ""

	Add-Line $L "── ⭐⭐ MICommonInterface 实例枚举与试调用（⭐ 本工具最关键的一节）──"
	Add-Line $L "  ⭐ 背景：宝龙达模具那套接口（⭐ 三份开源实现一致确认）是 root\WMI 的 MICommonInterface，"
	Add-Line $L "     实例名形如 ACPI\PNP0C14\MIFS_0，方法 MiInterface（InData[32] => OutData）。"
	Add-Line $L "  ⭐ 我们要确认三件事：① 你这台机的实例名 ② 类 GUID ③ 各功能号能不能调通。"
	Add-Line $L "  ⚠️ 本节**只读**（只用 Get=250），⛔ 一个 Set 都不发、⛔ 不改任何设置。"
	Add-Line $L ""
	try {
		$mc = New-Object System.Management.ManagementClass('root\WMI:MICommonInterface')
		$mc.Get() | Out-Null
		Add-Line $L "  ✅ 类 root\WMI:MICommonInterface 存在"
		try {
			$q = $mc.Qualifiers['Guid']
			if ($q) { Add-Line $L ("     ⭐ 类 GUID = " + $q.Value + "   <-- ⭐ 请重点核对这个值") } else { Add-Line $L "     ⚠️ 该类没有 GUID 限定符（也请告诉我们）" }
		} catch {}
		Add-Line $L ""
		$insts = $mc.GetInstances()
		$n = 0
		foreach ($inst in $insts) {
			$n++
			Add-Line $L ("  ⭐ 实例 #" + $n + " : " + $inst.Path.RelativePath)
			try { foreach ($p in $inst.Properties) { if ($p.Name -notmatch '^__') { Add-Line $L ("      属性 " + $p.Name + " = " + ($p.Value)) } } } catch {}
			$probes = @(
				@{ No = 8;  Name = 'SystemPerMode（性能模式）';     Hint = '参考口径：OutData[4] = 0平衡 / 1增强 / 2静音' },
				@{ No = 13; Name = 'CPUGPUFanSpeed（风扇转速）';    Hint = '参考口径：16 位小端；大扇1=[4..5] 大扇2=[6..7] 小扇=[10..11]' },
				@{ No = 22; Name = 'CPUThermometer（CPU温度）';     Hint = '参考口径：OutData[4] = 摄氏度数' },
				@{ No = 20; Name = 'MaxFanSpeedSwitch（风扇手动开关）'; Hint = '参考口径：OutData[4] = 0 关 / 1 开' },
				@{ No = 15; Name = 'Ambientlight（LOGO灯）';        Hint = '参考口径：OutData[4] = 0 关 / 1 开' },
				@{ No = 23; Name = 'CPUPower（功耗墙/温度墙）';      Hint = '参考口径：OutData[4] = 1 表示自定义已开启' }
			)
			foreach ($pr in $probes) {
				try {
					$ip = $inst.GetMethodParameters('MiInterface')
					$buf = New-Object byte[] 32
					$buf[1] = 250
					$buf[3] = [byte]$pr.No
					$ip['InData'] = $buf
					$op = $inst.InvokeMethod('MiInterface', $ip, $null)
					$out = $op['OutData']
					if ($out) {
						$hex = ($out | ForEach-Object { $_.ToString('X2') }) -join ' '
						Add-Line $L ("      ⭐ 功能号 " + $pr.No + " · " + $pr.Name + " 调用成功")
						Add-Line $L ("         OutData = " + $hex)
						Add-Line $L ("         " + $pr.Hint)
					} else {
						Add-Line $L ("      ⚠️ 功能号 " + $pr.No + " · " + $pr.Name + " 返回空")
					}
				} catch {
					Add-Line $L ("      ⚠️ 功能号 " + $pr.No + " · " + $pr.Name + " 调用失败: " + $_.Exception.Message)
				}
			}
			Add-Line $L ""
		}
		Add-Line $L ("  ⭐ 实例总数 = " + $n)
		if ($n -eq 0) {
			Add-Line $L "  ⚠️ 一个实例都没有 ==> 这台机可能没有这套接口，或厂商驱动/服务还没起来。"
			Add-Line $L "     请确认：① 官方控制台装了吗？ ② 装了的话**先把官方控制台打开**再重跑本脚本。"
		}
	} catch {
		Add-Line $L ("  ⚠️ 类 root\WMI:MICommonInterface 不可用: " + $_.Exception.Message)
		Add-Line $L "     ==> 这台机可能不是宝龙达模具，或厂商接口没装/没起来。"
		Add-Line $L "     ==> 无论如何请把本文件整份发回（我们要看它到底有哪些类）。"
	}
	Add-Line $L ""
	Add-Line $L "── 设备树里的 ACPI 设备（⭐ 宝龙达那套驱动的落点）──"
	try {
		Get-PnpDevice -ErrorAction Stop | Where-Object { $_.InstanceId -match 'ACPI' } | Sort-Object InstanceId | ForEach-Object {
			$friendly = $_.FriendlyName
			if ($friendly -match 'Mechrevo|AiStone|Uniwill|Gaming|Control|Fan|Battery|EC|OEM|机械革命' -or $_.InstanceId -match 'ACPI\\[A-Z]{4}') {
				Add-Line $L ("  " + $_.InstanceId + "  |  " + $friendly + "  |  " + $_.Status + "  |  class=" + $_.Class)
			}
		}
	} catch { Add-Line $L ("  读取失败: " + $_.Exception.Message) }
	Add-Line $L ""

	Add-Line $L "── 已装的厂商相关驱动/服务 ──"
	try {
		Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services' -ErrorAction Stop |
			Where-Object { $_.PSChildName -match 'Mechrevo|AiStone|Uniwill|Gaming|Control|ACPI|EC|OEM|UWP|Baolong|BLD' } |
			ForEach-Object {
				$ip = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
				Add-Line $L ("  [服务] " + $_.PSChildName + "  ImagePath=" + $ip.ImagePath + "  Start=" + $ip.Start)
			}
	} catch {}
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. 厂商控制台软件与文件（看它用什么接口、有没有可供参考的实现）
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '03-厂商控制台.txt' '厂商控制台软件与文件清单' {
	param($L)
	Add-Line $L "── 已安装程序里的厂商控制台 ──"
	foreach ($root in @(
		'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
		'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
	)) {
		try {
			Get-ChildItem $root -ErrorAction Stop | ForEach-Object {
				$p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
				if ($p.DisplayName -match 'Mechrevo|机械革命|Control|Gaming|Console|Uniwill|AiStone') {
					Add-Line $L ("  名称: " + $p.DisplayName)
					Add-Line $L ("    版本: " + $p.DisplayVersion + "  发布者: " + $p.Publisher)
					Add-Line $L ("    安装位置: " + $p.InstallLocation)
					Add-Line $L ("    卸载串: " + $p.UninstallString)
					Add-Line $L ("    产品ID: " + $_.PSChildName)
				}
			}
		} catch {}
	}
	Add-Line $L ""

	Add-Line $L "── 控制台实际文件清单 + 版本（⭐ 我们要知道它调哪个接口）──"
	$guessDirs = @(
		'C:\Program Files\Mechrevo', 'C:\Program Files (x86)\Mechrevo',
		'C:\Program Files\OEM', 'C:\Program Files (x86)\OEM',
		'C:\Program Files\UniwillService', 'C:\Program Files (x86)\UniwillService',
		'C:\Program Files\AiStoneService', 'C:\Program Files\ControlCenter',
		'C:\Program Files\MyControlCenter'
	)
	foreach ($d in $guessDirs) {
		if (-not (Test-Path $d)) { continue }
		Add-Line $L ("  ⭐ [" + $d + "]")
		try {
			Get-ChildItem $d -Recurse -File -Include '*.exe','*.dll' -ErrorAction SilentlyContinue |
				Sort-Object Length -Descending | Select-Object -First 40 | ForEach-Object {
					$vi = $_.VersionInfo
					Add-Line $L ("    " + $_.FullName.Replace($d, '') + "  " + $_.Length + " B  版本=" + $vi.FileVersion + "  公司=" + $vi.CompanyName)
				}
		} catch {}
	}
	Add-Line $L ""

	Add-Line $L "── 正在运行的厂商进程（含命令行，⭐ 能看出它调什么）──"
	try {
		Get-CimInstance Win32_Process -ErrorAction Stop |
			Where-Object { $_.Name -match 'Control|Gaming|Mechrevo|Uniwill|AiStone|OEM|OSD|tray|Fan|EC' } |
			ForEach-Object { Add-Line $L ("  PID " + $_.ProcessId + "  " + $_.Name + "  |  " + $_.ExecutablePath + "  |  cmd=" + $_.CommandLine) }
	} catch {}
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. 电源/性能/风扇 的现状（适配要对上这些语义）
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '04-性能与风扇现状.txt' '电源、性能档位、风扇现状' {
	param($L)
	Add-Line $L "── 电池与供电 ──"
	try { Get-CimInstance Win32_Battery | ForEach-Object { Add-Line $L ("  " + $_.Name + "  电量=" + $_.EstimatedChargeRemaining + "%  状态=" + $_.BatteryStatus + "  设计/满充=" + $_.DesignVoltage + "/" + $_.FullChargeCapacity) } } catch {}
	try { Add-Line $L ("  当前是否插电: " + $(if ((Get-CimInstance -ClassName BatteryStatus -Namespace root\WMI -ErrorAction SilentlyContinue).PowerOnline) { "是" } else { "否/未知" })) } catch {}
	Add-Line $L ""

	Add-Line $L "── 电源计划 ──"
	try { powercfg /getactivescheme | ForEach-Object { Add-Line $L ("  " + $_) } } catch {}
	try { powercfg /list | ForEach-Object { Add-Line $L ("  " + $_) } } catch {}
	Add-Line $L ""

	Add-Line $L "── 风扇转速（如果有）──"
	try {
		Get-CimInstance Win32_Fan -ErrorAction Stop | ForEach-Object { Add-Line $L ("  " + $_.Name + "  转速=" + $_.DesiredSpeed + "  状态=" + $_.Status + "  ActiveCooling=" + $_.ActiveCooling) }
	} catch { Add-Line $L "  Win32_Fan 读不到" }
	try {
		Get-CimInstance Win32_TemperatureProbe -ErrorAction Stop | ForEach-Object { Add-Line $L ("  温度: " + $_.Name + " = " + $_.CurrentReading) }
	} catch { Add-Line $L "  Win32_TemperatureProbe 读不到（多数笔记本如此，正常）" }
	Add-Line $L ""

	Add-Line $L "── CPU 现状 ──"
	try { Get-CimInstance Win32_Processor | ForEach-Object { Add-Line $L ("  " + $_.Name + "  当前频率=" + $_.CurrentClockSpeed + "MHz  最大=" + $_.MaxClockSpeed + "MHz  负载=" + $_.LoadPercentage + "%") } } catch {}
	Add-Line $L ""

	Add-Line $L "── 显示/刷新率 ──"
	try { Get-CimInstance Win32_VideoController | ForEach-Object { Add-Line $L ("  " + $_.Name + "  当前=" + $_.CurrentHorizontalResolution + "x" + $_.CurrentVerticalResolution + "@" + $_.CurrentRefreshRate + "Hz") } } catch {}
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. 关键日志位置清单（⭐ 说明里会讲哪些要手动取）
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '05-日志位置清单.txt' '厂商控制台的日志/配置在哪（只列位置，不复制内容）' {
	param($L)
	Add-Line $L "⭐ 下面这些路径**只列清单**，内容请你按《要抓什么.txt》第 4 节手动取需要的几个。"
	Add-Line $L ""
	$cands = @(
		"$env:ProgramData\Mechrevo", "$env:ProgramData\ControlCenter", "$env:ProgramData\MyControlCenter",
		"$env:ProgramData\UniwillService", "$env:ProgramData\AiStoneService", "$env:ProgramData\OEM",
		"$env:LOCALAPPDATA\Mechrevo", "$env:APPDATA\Mechrevo",
		"$env:LOCALAPPDATA\ControlCenter", "$env:APPDATA\ControlCenter",
		"$env:LOCALAPPDATA\UniwillService", "$env:APPDATA\UniwillService"
	)
	foreach ($d in $cands) {
		if (-not (Test-Path $d)) { continue }
		Add-Line $L ("⭐ [" + $d + "]")
		try {
			Get-ChildItem $d -Recurse -File -ErrorAction SilentlyContinue |
				Where-Object { $_.Extension -match '\.(log|txt|json|xml|ini|cfg|db|sqlite)' } |
				Sort-Object LastWriteTime -Descending | Select-Object -First 40 | ForEach-Object {
					Add-Line $L ("    " + $_.FullName.Replace($d, '') + "  " + $_.Length + " B  改于 " + $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
				}
		} catch {}
	}
	Add-Line $L ""
	Add-Line $L "── 事件查看器里与厂商软件相关的近期错误（⭐ 崩溃线索）──"
	try {
		Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 1, 2; StartTime = (Get-Date).AddDays(-14) } -MaxEvents 60 -ErrorAction Stop |
			Where-Object { $_.Message -match 'Mechrevo|Control|Gaming|Uniwill|AiStone|OEM' } |
			ForEach-Object { Add-Line $L ("  " + $_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + "  [" + $_.ProviderName + "] id=" + $_.Id + "  " + ($_.Message -split "`n")[0]) }
	} catch { Add-Line $L "  （读不到事件日志，可能需要在管理员下运行）" }
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. 环境清单
# ─────────────────────────────────────────────────────────────────────────────
Write-Section '06-采集环境.txt' '本次采集的环境（便于我们复现）' {
	param($L)
	Add-Line $L ("PowerShell 版本 : " + $PSVersionTable.PSVersion)
	Add-Line $L ("是否管理员      : " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
	Add-Line $L ("OS              : " + [System.Environment]::OSVersion.VersionString)
	Add-Line $L ("时区            : " + (Get-TimeZone).Id)
	Add-Line $L ("日期            : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
	Add-Line $L ""
	Add-Line $L "── 本机是"什么模具"的判据汇总 ──"
	Add-Line $L "  ⭐ 请把「01-机器信息.txt」里的 Manufacturer / Model / SystemFamily / BaseBoard 抄给作者"
	Add-Line $L "  ⭐ 并说明：这台机器**能不能装官方控制台**、装了哪个版本"
}

Write-Host ""
Write-Host "采集完成 ✅" -ForegroundColor Green
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 打包成 zip（⭐ 方便直接发回）
# ─────────────────────────────────────────────────────────────────────────────
$zip = $outRoot + '.zip'
try {
	if (Test-Path $zip) { Remove-Item $zip -Force }
	Compress-Archive -Path (Join-Path $outRoot '*') -DestinationPath $zip -Force
	Write-Host ("已打包: " + $zip) -ForegroundColor Cyan
	Write-Host ""
	Write-Host "⭐ 请把上面这个 zip 发给作者。" -ForegroundColor Yellow
	Write-Host ("   （原始文件夹也留着: " + $outRoot + "）") -ForegroundColor DarkGray
} catch {
	Write-Host ("⚠️ 自动打包失败（不影响采集）: " + $_.Exception.Message) -ForegroundColor Yellow
	Write-Host ("   ⭐ 请手动右键压缩这个文件夹: " + $outRoot) -ForegroundColor Yellow
}

Write-Host ""
Write-Host "⭐ 另外还有两件事请照《要抓什么.txt》做（脚本抓不到）：" -ForegroundColor Yellow
Write-Host "   1) 用厂商控制台**依次切换性能档位/风扇/灯效**，各停 3 秒（我们要看它发了什么）" -ForegroundColor Yellow
Write-Host "   2) 若是灯效问题，请录一段短视频" -ForegroundColor Yellow
Write-Host ""
Read-Host "按回车关闭"
