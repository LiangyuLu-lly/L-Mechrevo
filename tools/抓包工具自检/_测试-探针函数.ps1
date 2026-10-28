# ⭐ 单元测试：把 `Invoke-MifsGet` 单独抽出来，用**模拟对象**验证三条路径
#    （⭐ 起因：真机上 6 个功能号全挂 ⇒ ⭐ 发机主前必须自己先证明"能通" ✅）
$ErrorActionPreference = 'Stop'
$f = 'D:\openLM-baolongda\抓包工具-宝龙达\运行抓包.ps1'

# ── ① 用 AST 精确抽出函数定义（⛔ 不执行整个脚本）──
$tokens = $null; $errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tokens, [ref]$errs)
if ($errs) { throw ("脚本语法错误 " + $errs.Count + " 个") }
$fnAst = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-MifsGet' }, $true)
if (-not $fnAst -or $fnAst.Count -eq 0) { throw '没找到 Invoke-MifsGet 函数' }
Invoke-Expression $fnAst[0].Extent.Text
'✅ 已抽出 Invoke-MifsGet（{0} 字符）' -f $fnAst[0].Extent.Text.Length
''

# ── ② 造模拟对象 ──
#   $op 同时支持两种读法：
#     A 写法：$op.Properties['OutData'].Value
#     B 写法：$op['OutData']            （⭐ hashtable 下标 ✅）
function New-MockTarget {
	param([string]$Mode)   # 'A-ok' | 'B-ok' | 'both-fail' | 'A-throw'

	$payload = [byte[]]@(0xAA, 0xFA, 0x00, 0x08, 22, 0x11, 0x22, 0x33, 0x44,
	                     0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD)

	$target = New-Object psobject
	$target | Add-Member -MemberType NoteProperty -Name Mode -Value $Mode

	# GetMethodParameters：每次返回**新的** in-params 对象
	$target | Add-Member -MemberType ScriptMethod -Name GetMethodParameters -Value {
		param([string]$m)
		$ip = @{ Properties = @{ InData = ( New-Object psobject ) } }
		$ip.Properties['InData'] | Add-Member -MemberType NoteProperty -Name Value -Value $null
		$ip | Add-Member -MemberType ScriptMethod -Name SetPropertyValue -Value {
			param($n, $v)
			$this.Properties[$n].Value = $v
		}
		return $ip
	}

	# InvokeMethod：按 Mode 决定"输出对象长什么样"
	$target | Add-Member -MemberType ScriptMethod -Name InvokeMethod -Value {
		param([string]$m, $ip, $o)

		# ⭐ 校验：入口参数真的被写进去了吗？（⭐ 这正是原来失败的地方 ✅）
		$inBytes = $ip.Properties['InData'].Value
		if (-not $inBytes) { throw 'InData 没有被写入（模拟真实的失败原因）' }
		if ($inBytes.Length -ne 32) { throw ('InData 长度不对: ' + $inBytes.Length) }
		if ($inBytes[1] -ne 250) { throw ('InData[1] 应为 250，实际 ' + $inBytes[1]) }
		if ($inBytes[3] -ne $this.ExpectedFunc) { throw ('InData[3] 应为 ' + $this.ExpectedFunc) }

		$bytes = $this.Payload
		$op = @{}
		if ($this.Mode -eq 'A-ok') {
			$op['Properties'] = @{ OutData = ([pscustomobject]@{ Value = $bytes }) }
			$op['OutData'] = $bytes
		}
		elseif ($this.Mode -eq 'A-throw') {
			# A 读法炸掉（⭐ 模拟真机那个 PSObject→Array 异常 ✅），B 读法正常
			$op['Properties'] = @{ OutData = ([pscustomobject]@{ Value = $null }) }
			$op['OutData'] = $bytes
		}
		elseif ($this.Mode -eq 'B-ok') {
			# A 读法拿不到（⭐ 没有 Properties 键 ✅），B 读法正常
			$op['OutData'] = $bytes
		}
		elseif ($this.Mode -eq 'both-fail') {
			# 两种都拿不到
			$op['Properties'] = $null
		}
		return $op
	}

	$target | Add-Member -MemberType NoteProperty -Name Payload -Value $payload
	$target | Add-Member -MemberType NoteProperty -Name ExpectedFunc -Value 0
	return $target
}

# ── ③ 跑三个用例 ──
$pass = 0; $fail = 0
function Check {
	param([string]$Name, [bool]$Cond, [string]$Detail)
	if ($Cond) { $script:pass++; '  ✅ ' + $Name + '   ' + $Detail }
	else { $script:fail++; '  ❌ ' + $Name + '   ' + $Detail }
}

'=== 用例 1：A 写法正常 ⇒ 应返回 OK + Style=A + 解析对 ==='
$t = New-MockTarget -Mode 'A-ok'; $t.ExpectedFunc = 22
$r = Invoke-MifsGet -Target $t -FunctionNo 22
Check 'Ok'    ($r.Ok -eq $true)                  ('Ok=' + $r.Ok)
Check 'Style' ($r.Style -eq 'A')                 ('Style=' + $r.Style)
Check 'Raw'   ($r.Raw -eq 22)                    ('Raw=' + $r.Raw + '（应为 22）')
Check 'Hex'   ($r.Hex -like 'AA FA 00 08 16*')   ('Hex=' + $r.Hex.Substring(0, [Math]::Min(26, $r.Hex.Length)))
Check '无错误' ([string]::IsNullOrEmpty($r.Error)) ('Error="' + $r.Error + '"')
''

'=== 用例 2：A 写法炸（⭐ 模拟真机那个异常）⇒ 应自动回退 B 并成功 ==='
$t2 = New-MockTarget -Mode 'A-throw'; $t2.ExpectedFunc = 13
$r2 = Invoke-MifsGet -Target $t2 -FunctionNo 13
Check 'Ok'    ($r2.Ok -eq $true)   ('Ok=' + $r2.Ok)
Check 'Style' ($r2.Style -eq 'B')  ('Style=' + $r2.Style + '（应为 B ⇒ ⭐ 回退生效 ✅）')
Check 'Raw'   ($r2.Raw -eq 22)     ('Raw=' + $r2.Raw)
Check 'B 成功时清空错误' ([string]::IsNullOrEmpty($r2.Error)) ('Error 为空（⭐ Style=B 已表明 A 失败 ✅）')
Check 'Hex 正常' ($r2.Hex.Length -gt 30) ('Hex 长度=' + $r2.Hex.Length)
''

'=== 用例 3：两种都拿不到 ⇒ 应 Ok=false 且记录两种失败原因 ==='
$t3 = New-MockTarget -Mode 'both-fail'; $t3.ExpectedFunc = 8
$r3 = Invoke-MifsGet -Target $t3 -FunctionNo 8
Check 'Ok=false' ($r3.Ok -eq $false)     ('Ok=' + $r3.Ok)
Check 'Style 为空' ($r3.Style -eq '')    ('Style="' + $r3.Style + '"')
Check '两种原因都记' (($r3.Error -match 'A 写法') -and ($r3.Error -match 'B 写法')) ('Error=' + $r3.Error)
''

'=== 用例 4：InData 没写进去（⭐ 模拟真实的原始故障）⇒ 不应谎报成功 ==='
$t4 = New-MockTarget -Mode 'A-ok'; $t4.ExpectedFunc = 77   # ⭐ 与下面传入的 99 不符 ⇒ ⭐ 触发模拟的写入失败
$r4 = Invoke-MifsGet -Target $t4 -FunctionNo 99
Check 'Ok=false（⛔ 不谎报）' ($r4.Ok -eq $false)  ('Ok=' + $r4.Ok)
Check '记录了错误' (-not [string]::IsNullOrEmpty($r4.Error)) ('Error 非空 ✅')
''

'=== 用例 5：FunctionNo 类型（⭐ int / byte 都要能进）==='
$t5 = New-MockTarget -Mode 'A-ok'; $t5.ExpectedFunc = 23
$r5 = Invoke-MifsGet -Target $t5 -FunctionNo ([byte]23)
Check 'byte 入参可用' ($r5.Ok -eq $true) ('Ok=' + $r5.Ok + ' · Style=' + $r5.Style)
''

'═════════════════════════════'
'结果：⭐ 通过 {0} · ⭐ 失败 {1}' -f $pass, $fail
if ($fail -gt 0) { exit 1 }
