# ⭐ 单元测试 2：⭐ 验证「第 7 节那次报错的那个调用」现在能跑通
#    真机报错原文：⭐ `WriteSection : 无法将"WriteSection"项识别为 cmdlet…`
#    ⇒ ⭐ 这里抽出真正的 `Write-Section` 函数，⭐ 用**和第 7 节一模一样的调用形式**试一次 ✅
$ErrorActionPreference = 'Stop'
$f = 'D:\openLM-baolongda\抓包工具-宝龙达\运行抓包.ps1'

# ── 抽出需要的两个函数（⛔ 不执行整个脚本）──
$tk = $null; $er = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tk, [ref]$er)
if ($er) { throw ('脚本语法错误 ' + $er.Count + ' 个') }

foreach ($name in @('Write-Section', 'Add-Line', 'Invoke-MifsGet', 'Read-MifsFunction')) {
	$defs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
	if (-not $defs -or $defs.Count -eq 0) { throw ('没找到函数: ' + $name) }
	Invoke-Expression $defs[0].Extent.Text
	Write-Host ('  ✅ 已抽出 ' + $name)
}

# ── 造一个输出目录（⭐ 模仿脚本里的 $outRoot ✅）──
$outRoot = Join-Path $env:TEMP ('_ws7test_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
Write-Host ('  📁 $outRoot = ' + $outRoot)

$pass = 0; $fail = 0
function Check { param([string]$n, [bool]$c, [string]$d) if ($c) { $script:pass++; Write-Host ('  ✅ ' + $n + '   ' + $d) } else { $script:fail++; Write-Host ('  ❌ ' + $n + '   ' + $d) } }

Write-Host ''
Write-Host '=== 用例 A：⭐ 用第 7 节那个调用形式（Write-Section ＋ 中文文件名 ＋ 中文标题 ＋ scriptblock）==='
# ⭐ 这一行**逐字照抄**第 7 节的调用形式 ✅
Write-Section '07-逐模式实测.txt' '逐模式实测（⭐ 每个档位各读一次）' {
	param($L)
	Add-Line $L '⭐ 这一行来自模拟的第 7 节 ✅'
	Add-Line $L ('⭐ 模拟一个功能号结果：Raw=22 · OutData=AA FA 00 08 16')
}
$f7 = Join-Path $outRoot '07-逐模式实测.txt'
Check '07 文件已生成' (Test-Path $f7) ('路径存在 = ' + (Test-Path $f7))
if (Test-Path $f7) {
	$c = Get-Content $f7 -Encoding UTF8
	Check '标题写对' ($c[0] -like '*逐模式实测*') ('首行 = ' + $c[0])
	Check '正文写进去了' (($c -join "`n") -match '模拟的第 7 节') ('行数 = ' + $c.Count)
	Check '中文没乱码' (($c -join "`n") -match '每个档位各读一次') '中文正常 ✅'
}

Write-Host ''
Write-Host '=== 用例 B：⭐ 一个"不存在实例"的读调用 ⇒ 应优雅失败、⛔ 不抛异常 ==='
$r1 = Read-MifsFunction 'MICommonInterface.InstanceName="ACPI\\PNP0C14\\MIFS"' 8
Check 'Ok=false（⭐ 本机没这个类）' ($r1.Ok -eq $false) ('Ok=' + $r1.Ok)
Check '有错误信息' (-not [string]::IsNullOrEmpty($r1.Error)) ('Error 长度 = ' + ([string]$r1.Error).Length)
Check '返回结构完整' (($null -ne $r1.Raw) -and ($null -ne $r1.Hex) -and ($null -ne $r1.Style)) '字段齐全 ✅'

Write-Host ''
Write-Host '=== 用例 C：⭐ 第 7 节的 RPM 解析算式（⭐ 用真机 C 的 OutData 假数据验算）==='
# ⭐ 真机 C 报的 OutData 结构是 32 字节；⭐ 这里按参考口径造一组：
#    [4..5]=第1扇 [6..7]=第2扇 [10..11]=第3扇（⭐ 16 位小端 ✅）
$hex = 'AA FA 00 08 40 06 90 01 00 00 20 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00'
$parts = $hex -split ' '
$f1 = [Convert]::ToInt32($parts[5] + $parts[4], 16)
$f2 = [Convert]::ToInt32($parts[7] + $parts[6], 16)
$f3 = [Convert]::ToInt32($parts[11] + $parts[10], 16)
Check '第1扇 RPM' ($f1 -eq 1600) ('f1 = ' + $f1 + '（⭐ 0x0640 = 1600 ✅）')
Check '第2扇 RPM' ($f2 -eq 400)  ('f2 = ' + $f2 + '（⭐ 0x0190 = 400 ✅）')
Check '第3扇 RPM' ($f3 -eq 800)  ('f3 = ' + $f3 + '（⭐ 0x0320 = 800 ✅）')

Remove-Item $outRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '═════════════════════════════'
Write-Host ('结果：⭐ 通过 ' + $pass + ' · ⭐ 失败 ' + $fail)
if ($fail -gt 0) { exit 1 }
