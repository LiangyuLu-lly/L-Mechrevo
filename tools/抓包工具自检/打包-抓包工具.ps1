# ⭐⭐ 抓包工具的「打包前自检 + 打包」脚本 —— ⭐ 每次改完工具都必须跑这个再发出去 ✅
#
# ⚠️ 起因（2026-10-16）：⭐ 连续踩同一个坑 ——
#    `edit` 工具在改带中文的 .ps1/.txt 时**会去掉 UTF-8 BOM** ⇒
#    ⇒ ⭐ 机主的 **PowerShell 5.1 会把文件按 GBK 读** ⇒
#       ⭐ 中文行的 UTF-8 字节被当成双字节字符 ⇒ ⭐ **吃掉后面的 ASCII 引号/花括号** ⚠️
#       ⇒ ⭐ 语法错误几十个 ⇒ ⭐ **脚本直接跑不起来** ❌
#    ⇒ ⭐ 所以**每次编辑后都必须补 BOM**，⭐ 而且必须在 **PowerShell 5.1** 下验证语法 ✅
#
# 本脚本做 5 件事（⭐ 任一步失败就**不打包**，⭐ 并明确报错 ✅）：
#   ① 给 4 个文件补 BOM + 统一 CRLF
#   ② 在 **PowerShell 5.1** 下解析 运行抓包.ps1（⭐ 机主用的就是 5.1 ✅）⇒ 必须 0 错误
#   ③ 检查 ASCII 双引号配对（⭐ 防"字符串里写 ASCII 引号"的老坑 ✅）
#   ④ 跑探针函数的单元测试（⭐ 模拟对象，⭐ 验证 A/B/C 三种写法与回退 ✅）
#   ⑤ 全部通过后才压缩成 交付-宝龙达抓包工具.zip

$ErrorActionPreference = 'Stop'
$Kit = 'D:\openLM-baolongda\抓包工具-宝龙达'
$Zip = 'D:\openLM-baolongda\交付-宝龙达抓包工具.zip'
$Ut  = 'D:\openLM-baolongda\_测试-探针函数.ps1'
$fail = 0

function Step([string]$n) { Write-Host ''; Write-Host ('──── ' + $n + ' ────') -ForegroundColor Cyan }
function Ok([string]$m)   { Write-Host ('  ✅ ' + $m) -ForegroundColor Green }
function Bad([string]$m)  { Write-Host ('  ❌ ' + $m) -ForegroundColor Red; $script:fail++ }
function Warn([string]$m) { Write-Host ('  ⚠️ ' + $m) -ForegroundColor Yellow }

# ── ① 补 BOM + CRLF ────────────────────────────────────────────────────────
Step '① 补 BOM + CRLF'
$names = @('运行抓包.ps1', '要抓什么.txt', '怎么运行.txt', 'README-先看这个.txt')
foreach ($n in $names) {
	$p = Join-Path $Kit $n
	$t = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
	$t = [regex]::Replace($t, "(?<!`r)`n", "`r`n")
	[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($true)))
	$b = [System.IO.File]::ReadAllBytes($p)
	$hasBom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
	if ($hasBom) { Ok ($n + '  BOM ✅') } else { Bad ($n + '  BOM 仍然缺失') }
}
# 自检脚本本身也要有 BOM ✅
foreach ($p in @($Ut, $PSCommandPath)) {
	if (Test-Path $p) {
		$t = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
		$t = [regex]::Replace($t, "(?<!`r)`n", "`r`n")
		[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($true)))
	}
}

# ── ② PowerShell 5.1 语法（⭐ 关键！）────────────────────────────────────────
Step '② PowerShell 5.1 语法（⭐ 机主的版本）'
$probe = Join-Path $env:TEMP ('_ps51check_' + [guid]::NewGuid().ToString('N') + '.ps1')
$probeBody = @'
$f = "D:\openLM-baolongda\抓包工具-宝龙达\运行抓包.ps1"
$tk = $null; $er = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$tk, [ref]$er)
Write-Output ("COUNT=" + $er.Count)
$er | Select-Object -First 6 | ForEach-Object { Write-Output ("ERR line " + $_.Extent.StartLineNumber + ": " + $_.Message) }
'@
[System.IO.File]::WriteAllText($probe, $probeBody, (New-Object System.Text.UTF8Encoding($true)))
$ps51 = & powershell -NoProfile -ExecutionPolicy Bypass -File $probe 2>&1
Remove-Item $probe -Force -ErrorAction SilentlyContinue
$cntLine = ($ps51 | Where-Object { $_ -like 'COUNT=*' } | Select-Object -First 1)
if ($cntLine) {
	$cnt = [int]($cntLine -replace 'COUNT=', '')
	if ($cnt -eq 0) { Ok 'PowerShell 5.1 解析：0 错误 ✅' }
	else {
		Bad ('PowerShell 5.1 解析：' + $cnt + ' 个错误（⚠️ 机主会跑不起来！）')
		$ps51 | Where-Object { $_ -like 'ERR line*' } | ForEach-Object { Write-Host ('      ' + $_) -ForegroundColor Red }
	}
} else { Bad 'PowerShell 5.1 自检没有返回结果' }

# ── ③ ASCII 双引号配对 ──────────────────────────────────────────────────────
Step '③ ASCII 双引号配对（⭐ 防老坑）'
$lines = [System.IO.File]::ReadAllLines((Join-Path $Kit '运行抓包.ps1'))
$odd = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
	if ($lines[$i] -match '^\s*#') { continue }
	if (([regex]::Matches($lines[$i], '"')).Count % 2 -ne 0) {
		$odd++
		Warn ('第 ' + ($i + 1) + ' 行引号数为奇数: ' + $lines[$i].Trim())
	}
}
if ($odd -eq 0) { Ok '没有引号不配对的行 ✅' } else { Bad ($odd.ToString() + ' 行引号不配对') }

# ── ④ 单元测试（⭐ 两个）────────────────────────────────────────────────────
$Ut2 = 'D:\openLM-baolongda\_测试-第7节.ps1'
Step '④ 单元测试 1/2：探针函数（⭐ A/B/C 三写法与回退）'
if (Test-Path $Ut) {
	$ut = & powershell -NoProfile -ExecutionPolicy Bypass -File $Ut 2>&1
	$ut | ForEach-Object { Write-Host ('  ' + $_) }
	if ($LASTEXITCODE -eq 0) { Ok '探针函数单元测试全通过 ✅' } else { Bad '探针函数单元测试有失败项' }
} else { Warn ('没找到单元测试脚本: ' + $Ut) }

Step '④ 单元测试 2/2：第 7 节的调用形式（⭐ 上次真机就栽在这）'
if (Test-Path $Ut2) {
	$ut2 = & powershell -NoProfile -ExecutionPolicy Bypass -File $Ut2 2>&1
	$ut2 | ForEach-Object { Write-Host ('  ' + $_) }
	if ($LASTEXITCODE -eq 0) { Ok '第 7 节调用形式测试全通过 ✅' } else { Bad '第 7 节调用形式测试有失败项' }
} else { Warn ('没找到单元测试脚本: ' + $Ut2) }

# ── ⑤ 打包 ──────────────────────────────────────────────────────────────────
Step '⑤ 打包'
if ($fail -gt 0) {
	Bad ('有 ' + $fail + ' 项没过 ⇒ ⛔ **不打包**（⭐ 修好再来）')
	exit 1
}
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Kit '*') -DestinationPath $Zip -Force
$h = (Get-FileHash $Zip -Algorithm SHA256).Hash
Ok ('已打包 ' + $Zip)
Write-Host ('     {0:N0} B · SHA256 = {1}' -f (Get-Item $Zip).Length, $h) -ForegroundColor Gray
$v = ([regex]::Match([System.IO.File]::ReadAllText((Join-Path $Kit '运行抓包.ps1'), [System.Text.Encoding]::UTF8), "KitVersion\s*=\s*'([^']+)'")).Groups[1].Value
Write-Host ('     ⭐ 版本 = ' + $v) -ForegroundColor Gray
Write-Host ''
Write-Host '✅ 全部通过，可以发机主了' -ForegroundColor Green
