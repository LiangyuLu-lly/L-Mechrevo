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

# ⭐⭐ 工具版本（⭐ 每次改脚本都要升，⭐ 并写进 06-采集环境.txt ⇒ ⭐ 以后一眼就能看出机主用的是哪一版）
$script:KitVersion  = '2026-10-16.6'
$script:KitFeatures = 'MIFS-Probe,PNP0C14-Filter,AccessDenied-Hint,PerMode-Interactive,Fixed-WmiArray,ReadOnly'
#   2026-10-16.1  首版（六节 + 强/弱相关筛选）
#   2026-10-16.2  ＋ MIFS-Probe：MICommonInterface 实例枚举与只读试调用
#   2026-10-16.3  ＋ 修设备树过滤（漏掉 ACPI\PNP0C14\…）+ 修 06 节一处引号 bug + 版本标记
#   2026-10-16.4  ＋ AccessDenied 单独识别 + 明确提示「请用管理员重跑」（真机实测：非管理员会 AccessDenied）
#   2026-10-16.5  ＋ ⭐ **可选的「逐模式实测」**（第 7 节：你切档位、脚本在每档读一次）+
#                    收尾说明改写（⭐ 起因：机主反馈「脚本一下就跑完了，我还没调完」⇒ 说明写得不清楚 ✅）

$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$outRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ("宝龙达抓包-" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

# ═════════════════════════════════════════════════════════════════════════════
# ⭐⭐ 读一次 MiInterface（⭐ 只读 Get=250）—— ⭐ **带两种写法自动回退** ✅
#
# ⚠️ 起因（2026-10-16.6 · 真机报错）：原来只用下标写法
#    `$ip['InData'] = $buf` ＋ `$out = $op['OutData']`
#    ⇒ 真机上抛「无法将类型为 PSObject 的对象强制转换为类型 System.Array」❌
#    ⇒ 6 个功能号**全部失败** ⚠️
# ⇒ 现在 ⭐ **先试 A 写法（显式 byte[] + Properties[...].Value，⭐ 社区通用稳妥写法）**，
#    ⭐ 失败了再试 **B 写法（下标 + SetPropertyValue）** ⇒ ⭐ 只要有一种通就能拿到值 ✅
#    ⭐ 并把"用的哪种写法"也记进结果里，⭐ 便于我们判断到底哪种对 ✅
# ⭐ 返回哈希表：⭐ Ok / Hex / Bytes / Raw / Error / Style
# ═════════════════════════════════════════════════════════════════════════════
function Invoke-MifsGet {
	param([System.Management.ManagementObject]$Target, [int]$FunctionNo)
	$r = @{ Ok = $false; Hex = ''; Bytes = $null; Raw = -1; Error = ''; Style = '' }

	# ── 写法 A：⭐ 显式 byte[] ＋ ⭐ Properties[...].Value ──
	try {
		$ip = $Target.GetMethodParameters('MiInterface')
		[byte[]]$buf = New-Object byte[] 32
		$buf[1] = 250
		$buf[3] = [byte]$FunctionNo
		$ip.Properties['InData'].Value = $buf
		$op = $Target.InvokeMethod('MiInterface', $ip, $null)
		[byte[]]$out = $op.Properties['OutData'].Value
		if ($out -and $out.Length -gt 0) {
			$r.Ok = $true; $r.Bytes = $out; $r.Style = 'A'
			$r.Hex = (($out | ForEach-Object { $_.ToString('X2') }) -join ' ')
			if ($out.Length -gt 4) { $r.Raw = [int]$out[4] }
			return $r
		}
		$r.Error = 'A 写法返回空'
	} catch {
		$r.Error = 'A 写法: ' + $_.Exception.Message
	}

	# ── 写法 B：⭐ 下标 ＋ ⭐ SetPropertyValue（⭐ 回退）──
	try {
		$ip2 = $Target.GetMethodParameters('MiInterface')
		[byte[]]$buf2 = New-Object byte[] 32
		$buf2[1] = 250
		$buf2[3] = [byte]$FunctionNo
		$ip2.SetPropertyValue('InData', $buf2)
		$op2 = $Target.InvokeMethod('MiInterface', $ip2, $null)
		$raw = $op2['OutData']
		[byte[]]$out2 = $raw
		if ($out2 -and $out2.Length -gt 0) {
			$r.Ok = $true; $r.Bytes = $out2; $r.Style = 'B'; $r.Error = ''
			$r.Hex = (($out2 | ForEach-Object { $_.ToString('X2') }) -join ' ')
			if ($out2.Length -gt 4) { $r.Raw = [int]$out2[4] }
			return $r
		}
		$r.Error = ($r.Error + ' ／ B 写法返回空')
	} catch {
		$r.Error = ($r.Error + ' ／ B 写法: ' + $_.Exception.Message)
	}
	return $r
}


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
	Add-Line $L "  ⭐ 背景：宝龙达模具那套接口是 root\WMI 的 MICommonInterface，方法 MiInterface（InData[32] => OutData）。"
	Add-Line $L "  ⭐ 实例名形如 ACPI\PNP0C14\MIFS_0（⭐ 蛟龙实测是 ACPI\PNP0C14\MIFS ⇒ ⭐ 两种都试）"
	Add-Line $L "  ⭐ 我们要确认三件事：① 你这台机的实例名 ② 类 GUID ③ 各功能号能不能调通。"
	Add-Line $L "  ⚠️ 本节**只读**（只用 Get=250），⛔ 一个 Set 都不发、⛔ 不改任何设置。"
	$isAdminNow = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
	Add-Line $L ("  ⭐ 当前是否管理员：⭐ " + $isAdminNow + "（⚠️ 这一项很关键，见下）")
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
		# ⭐⭐ 2026-10-16 真机实测：非管理员下 GetInstances() 会抛 AccessDenied ⚠️
		#     ⇒ ⭐ 必须**单独捕获**并明确提示"请用管理员重跑"，
		#        ⛔ 不能混在"类不可用"里（⭐ 那会把"权限不够"误报成"没有接口"❌）
		$insts = $null
		try {
			$insts = $mc.GetInstances()
		} catch {
			$msg = $_.Exception.Message
			if ($msg -match 'Access|拒绝|denied') {
				Add-Line $L "  ⚠️⚠️ **访问被拒（Access denied）** —— ⭐ 类存在，但当前身份读不到实例 ⚠️"
				Add-Line $L "     ⇒ ⭐ 这**不代表**「这台机没有接口」，⭐ 而是**权限不够** ✅"
				Add-Line $L "     ⇒ ⭐⭐ **请务必用【管理员身份】重新运行本脚本**（见《怎么运行.txt》方法三）✅"
				Add-Line $L "     ⇒ ⭐ 重跑后这一节才会出现实例名与各功能号的实测值 ✅"
				Add-Line $L ""
				Add-Line $L ("     原始错误：⭐ " + $msg)
				Add-Line $L ("     当前是否管理员：⭐ " + $isAdminNow)
				Add-Line $L ""
				Add-Line $L "  ⭐ 补充证据（设备树里已经看到的）：⭐ 若下面是 ACPI\\PNP0C14\\MIFS，"
				Add-Line $L "     那就**基本可以确定这台机就是宝龙达模具**（⭐ 只差管理员权限把值读出来）✅"
				$insts = $null
			} else {
				Add-Line $L ("  ⚠️ 枚举实例失败：⭐ " + $msg)
				$insts = $null
			}
		}
		$n = 0
		if ($insts) {
		foreach ($inst in $insts) {
			$n++
			Add-Line $L ("  ⭐ 实例 #" + $n + " : " + $inst.Path.RelativePath)
			# ⭐ 记下实例路径 ⇒ 后面的「逐模式实测」（第 7 节）直接复用，⛔ 不重复发现
			if (-not $script:MifsInstancePath) { $script:MifsInstancePath = $inst.Path.RelativePath }
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
					# ⭐ 用共享的 Invoke-MifsGet（⭐ 内含 A/B 两种写法自动回退 ✅，⭐ 见文件开头 ✅）
					$res = Invoke-MifsGet -Target $inst -FunctionNo $pr.No
					if ($res.Ok) {
						Add-Line $L ("      ⭐ 功能号 " + $pr.No + " · " + $pr.Name + " 调用成功（写法 " + $res.Style + "）")
						Add-Line $L ("         OutData = " + $res.Hex)
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
		}  # ← if ($insts)
		Add-Line $L ("  ⭐ 实例总数 = " + $n)
		if ($n -eq 0 -and $insts) {
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
	Add-Line $L "  ⭐ 重点看：⭐ 有没有 `ACPI\PNP0C14\...`（⭐ 这是 WMI 映射的 ACPI 设备，"
	Add-Line $L "     厂商实例名形如 MIFS_0 / MIFS_1）。⭐ 请把这几行发回。"
	Add-Line $L "  ⭐ 另外列出所有 `ACPI\xxxx....` 形态的设备（⭐ 厂商设备常是 4 位字母 + 数字）。"
	try {
		Get-PnpDevice -ErrorAction Stop | Where-Object { $_.InstanceId -match '^ACPI\\' } | Sort-Object InstanceId | ForEach-Object {
			$friendly = $_.FriendlyName
			$devId = ($_.InstanceId -split '\\')[1]
			# ⚠️ 2026-10-16 修正：原条件 `ACPI\\[A-Z]{4}` 要求"4 个大写字母"，
			#    而 MIFS 设备是 `ACPI\PNP0C14\…`（PNP 后面有数字）⇒ ⭐ 正好被漏掉 ❌
			#    ⇒ 改为：⭐ ① 厂商关键词命中  或  ⭐ ② PNP 开头（ACPI 硬件 ID 惯例）或
			#              ⭐ ③ 4 位字母数字的短 ID（厂商自定义设备）✅
			$isInteresting = ($friendly -match 'Mechrevo|AiStone|Uniwill|Gaming|Control|Fan|Battery|EC|OEM|机械革命') -or
				($devId -match '^PNP[0-9A-F]{4}$') -or
				($devId -match '^[A-Z]{4}[0-9A-F]*$')
			if ($isInteresting) {
				Add-Line $L ("  " + $_.InstanceId + "  |  " + $friendly + "  |  " + $_.Status + "  |  class=" + $_.Class)
			}
		}
	} catch { Add-Line $L ("  读取失败: " + $_.Exception.Message) }
	Add-Line $L ""
	Add-Line $L "── ⭐ 专门找 MIFS / WMI-ACPI 相关设备（⭐ 直接给实例名）──"
	try {
		$found = $false
		Get-PnpDevice -ErrorAction Stop | Where-Object { $_.InstanceId -match 'PNP0C14|MIFS|WMI' } | Sort-Object InstanceId | ForEach-Object {
			$found = $true
			Add-Line $L ("  ⭐ " + $_.InstanceId + "  |  " + $_.FriendlyName + "  |  " + $_.Status + "  |  class=" + $_.Class)
		}
		if (-not $found) {
			Add-Line $L "  （没找到 PNP0C14 / MIFS / WMI 相关设备 —— ⭐ 但 WMI 类可能仍然存在，见上一节）"
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
	Add-Line $L "── ⭐ 抓包工具版本（⭐ 请连同这一行一起发回）──"
	Add-Line $L ("  ⭐⭐ 版本 = " + $script:KitVersion + "   ·   ⭐ 特征 = " + $script:KitFeatures)
	Add-Line $L ("  ⭐ 若「特征」里【没有】MIFS-Probe ⇒ 说明用的是**旧版**工具，请换新版重跑（我们需要 MICommonInterface 的实例名与功能号实测那一节）")
	Add-Line $L ""
	Add-Line $L "── ⭐ 本机模具判据汇总（⭐ 请连同下面这行一起抄给作者）──"
	Add-Line $L "  ⭐ 请把「01-机器信息.txt」里的 Manufacturer / Model / SystemFamily / BaseBoard 抄给作者"
	Add-Line $L "  ⭐ 并说明：这台机器**能不能装官方控制台**、装了哪个版本"
}

Write-Host ""
Write-Host "采集完成 ✅" -ForegroundColor Green
Write-Host ""


# ─────────────────────────────────────────────────────────────────────────────
# 7. ⭐ 逐模式实测（**可选 · 交互式 · 需要管理员**）
# ⭐ 2026-10-16.5 新增 —— 起因：机主反馈「脚本一下就跑完了，我还没调完」
#    ⇒ ⭐ 说明我们的**说明写得不清楚**（脚本是全自动的、一分钟就完 ✅），
#       而且**缺少一个"让你慢慢切档位、脚本在每档读一次"的环节** ✅
#    ⇒ 本节补上：⭐ 你在每个档位上停一下，脚本读一次「模式原始值 / 风扇转速 / CPU温度 / 风扇开关 / LOGO灯」
#       ⭐ 这样我们就能知道「静音 / 平衡 / 狂暴 各对应什么风扇与温度」✅
#    ⚠️ 需要管理员权限（否则读不到实例 ⇒ 本节会明确告诉你）✅
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "──────────────────────────────────────────────" -ForegroundColor Cyan
Write-Host " ⭐ 可选的「逐模式实测」（⭐ 建议做，但不是必须）" -ForegroundColor Cyan
Write-Host "──────────────────────────────────────────────" -ForegroundColor Cyan
Write-Host " 这一节会让你**亲自切档位**，脚本在每个档位读一次硬件值。" -ForegroundColor Gray
Write-Host " 这样我们能看出「静音 / 平衡 / 狂暴 各对应什么风扇与温度」。" -ForegroundColor Gray
Write-Host " ⚠️ 需要**管理员权限**；⛔ 不做也完全没关系（上面 6 节已是主要材料）" -ForegroundColor Gray
Write-Host " ⭐ 不做的话：直接按回车跳过 ✅" -ForegroundColor Gray
Write-Host ""

$isAdminNow = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# ⭐ 定义一个"读某个功能号"的小工具（⭐ 只读；⭐ 第 7 节与第 2 节同源口径 ✅）
function Read-MifsFunction {
	param([string]$InstancePath, [int]$FunctionNo)
	# ⭐ 委托给共享的 Invoke-MifsGet（⭐ 内含 A/B 两种写法自动回退 ✅，⭐ 见文件开头 ✅）
	#    ⇒ ⭐ 这样两处用的是**同一套逻辑**，⛔ 不会一处修好一处还坏 ✅
	$result = @{ Ok = $false; Hex = ''; Raw = -1; Error = ''; Style = '' }
	try {
		$mo = New-Object System.Management.ManagementObject('root\WMI', $InstancePath, $null)
		$res = Invoke-MifsGet -Target $mo -FunctionNo $FunctionNo
		$result.Ok = $res.Ok
		$result.Hex = $res.Hex
		$result.Raw = $res.Raw
		$result.Error = $res.Error
		$result.Style = $res.Style
	} catch {
		$result.Error = $_.Exception.Message
	}
	return $result
}

$doInteractive = $false
if (-not $isAdminNow) {
	Write-Host " ⚠️ 当前**不是管理员** ⇒ 逐模式实测**读不到硬件值**（会报 Access denied）⚠️" -ForegroundColor Yellow
	Write-Host "    ⇒ ⭐ 想要这一节，请**关掉本窗口**、用【管理员身份】重新运行本脚本 ✅" -ForegroundColor Yellow
} else {
	try {
		$ans = Read-Host " 要做逐模式实测吗？(输入 y 做 / 直接回车跳过)"
		if ($ans -and $ans.Trim().ToLower().StartsWith('y')) { $doInteractive = $true }
	} catch {}
}

if ($doInteractive) {
	# ⚠️⚠️ 2026-10-16.6 修正（⭐ 真机报错）：⭐ 原来写的是 `WriteSection`（⭐ **少了连字符**）❌
	#    ⇒ ⭐ 真机上抛 ⭐「无法将"WriteSection"项识别为 cmdlet…」⇒ ⭐ **整节没跑** ⚠️
	#    ⇒ ⭐ 函数名是 ⭐ `Write-Section`（⭐ 带连字符 ✅）—— 已改 ✅
	#    ⭐ 并且**整节包进 try/catch**：⭐ 万一再出错，⛔ 不能把红字喷给机主，
	#       ⭐ 而应写进文件 + ⭐ 继续打包 ✅
	try {
	Write-Section '07-逐模式实测.txt' '逐模式实测（⭐ 每个档位各读一次）' {
		param($L)
		Add-Line $L "⭐ 本节的读法：⭐ 只读（Get=250），⛔ 一个 Set 都不发 ✅"
		Add-Line $L ("⭐ 使用实例：⭐ " + $script:MifsInstancePath)
		Add-Line $L ""

		# ⭐ 档位定义（⭐ 参考口径：⭐ MIFS 原始值 0=平衡 · 1=增强 · 2=静音 · 3=疯狂/游戏）
		$modes = @(
			@{ Key = 'bal'; Label = '平衡 / 均衡（Balance）'; RawExpect = '0' },
			@{ Key = 'perf'; Label = '增强 / 性能（Performance）'; RawExpect = '1' },
			@{ Key = 'quiet'; Label = '静音 / 办公（Quiet）'; RawExpect = '2' },
			@{ Key = 'game'; Label = '疯狂 / 游戏（Fullspeed，若你的机器有第 4 档）'; RawExpect = '3' }
		)
		foreach ($m in $modes) {
			Add-Line $L ("── ⭐ 请切到【" + $m.Label + "】（参考原始值 " + $m.RawExpect + "）──")
			Write-Host ""
			Write-Host (" ⭐ 请把机器切到【" + $m.Label + "】" ) -ForegroundColor Yellow
			Write-Host "    ⭐ 怎么切：⭐ 官方控制台里选它；" -ForegroundColor Gray
			Write-Host "       或 ⭐ 用键盘热键切换（机械革命常见是 ⭐ Fn + Q，按几次循环换档）" -ForegroundColor Gray
			Write-Host "    ⭐ 切好之后**等 3 秒**（让风扇稳定），再回来按回车 ✅" -ForegroundColor Gray
			if ($m.Key -eq 'game') {
				Read-Host "    若没有第 4 档，直接按回车跳过这一档"
			} else {
				Read-Host "    切好并等 3 秒后，按回车继续"
			}

			$r8 = Read-MifsFunction $script:MifsInstancePath 8
			$r13 = Read-MifsFunction $script:MifsInstancePath 13
			$r22 = Read-MifsFunction $script:MifsInstancePath 22
			$r20 = Read-MifsFunction $script:MifsInstancePath 20
			$r15 = Read-MifsFunction $script:MifsInstancePath 15

			Add-Line $L ("    ⭐ 功能号 8  性能模式   : Raw=" + $r8.Raw + "   OutData = " + $r8.Hex)
			Add-Line $L ("    ⭐ 功能号 13 风扇转速   : OutData = " + $r13.Hex)
			if ($r13.Hex.Length -ge 17) {
				$parts = $r13.Hex -split ' '
				$f1 = [Convert]::ToInt32($parts[5] + $parts[4], 16)
				$f2 = [Convert]::ToInt32($parts[7] + $parts[6], 16)
				Add-Line $L ("       ⇒ ⭐ 第1个风扇 ≈ " + $f1 + " RPM · ⭐ 第2个风扇 ≈ " + $f2 + " RPM")
				if ($parts.Length -ge 12) {
					$f3 = [Convert]::ToInt32($parts[11] + $parts[10], 16)
					Add-Line $L ("       ⇒ ⭐ 第3个风扇（若有）≈ " + $f3 + " RPM")
				}
			}
			Add-Line $L ("    ⭐ 功能号 22 CPU温度   : Raw=" + $r22.Raw + "  （⭐ 0/255 视为无效）   OutData = " + $r22.Hex)
			Add-Line $L ("    ⭐ 功能号 20 风扇手动开关: Raw=" + $r20.Raw + "   OutData = " + $r20.Hex)
			Add-Line $L ("    ⭐ 功能号 15 LOGO灯    : Raw=" + $r15.Raw + "   OutData = " + $r15.Hex)
			if ($r8.Error) { Add-Line $L ("    ⚠️ 读取时有错误：⭐ " + $r8.Error) }
			Add-Line $L ""
		}
		Add-Line $L "⭐ 说明：⭐ 若某个档位读到的 功能号 8 原始值与上面「参考原始值」不一致，"
		Add-Line $L "   ⇒ ⭐ 那正是我们要的信息（⭐ 说明该机的档位编号与我们预期不同）✅ 请照实保留 ✅"
	}
	} catch {
		# ⚠️ 整节失败也不能让机主看到红字、更不能中断打包 ✅
		Write-Host (" ⚠️ 逐模式实测这一节出错了（⭐ 不影响前 6 节）: " + $_.Exception.Message) -ForegroundColor Yellow
		try {
			$errPath = Join-Path $outRoot '07-逐模式实测-出错.txt'
			[System.IO.File]::WriteAllText($errPath,
				"=== 逐模式实测出错 ===`r`n" + $_.Exception.ToString() + "`r`n`r`n（前 6 节仍然有效 ✅）",
				(New-Object System.Text.UTF8Encoding($true)))
			Write-Host ("    ⭐ 已把错误写进: " + $errPath) -ForegroundColor Gray
		} catch {}
	}
}
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
Write-Host "⭐⭐ 说明（⭐ 请读一下，⭐ 免得误会）：" -ForegroundColor Cyan
Write-Host "   ① ⭐ 上面 6 节 + 可选的逐模式实测，⭐ 就是我们要的**主要材料** ✅" -ForegroundColor Gray
Write-Host "   ② ⭐ 脚本本身**一分钟就跑完**，⭐ 这是**正常的** —— 它不需要你慢慢操作 ✅" -ForegroundColor Gray
Write-Host "   ③ ⚠️ 「切档位 / 切灯光」那类操作**不是必须的**；" -ForegroundColor Gray
Write-Host "      真正需要你操作的只有 ⭐ **那个可选的「逐模式实测」**（⭐ 你切、它读）✅" -ForegroundColor Gray
Write-Host "   ④ ⭐ 若你愿意多做一点（可选）：⭐ 用**官方控制台**切一遍档位与灯光，" -ForegroundColor Gray
Write-Host "      再把 `05-日志位置清单.txt` 里列出的**厂商日志文件夹**复制进来 ✅" -ForegroundColor Gray
Write-Host "      （⚠️ 没装官方控制台就跳过这一步 ✅ 不影响主线 ✅）" -ForegroundColor Gray
Write-Host ""
Read-Host "按回车关闭"
