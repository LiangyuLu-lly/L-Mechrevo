# ==============================================================================================
# 2026-10-06 D-1「核显模式按钮收尾」本机只读取证 runner
#
#   覆盖 7 个场景（能力位 fixture x 配置预置），逐场景跑一次 D1IgpuShot：
#     s1 判据不可靠 + 未表态(ask/show)                 -> 提示显示、按钮显示
#     s2 判据不可靠 + 用户明确答"有"(answer=yes)        -> 不提示（不再猜）
#     s3 能力位可靠(IgpuOnly=true，和本机 GCU 当前上报一致) -> 不提示（可靠分支）
#     s4 判据不可靠 + 试过一次且成功(verified=1)        -> 一直显示、不提示
#     s5 判据不可靠 + 试过一次且失败(verified=0)        -> 不再显示（隐藏）、不提示
#     s6 用户明确关掉(ui=hide)                         -> 隐藏（既有行为未改）
#     s7 只挂了 attempt_pending(还没重启回读)           -> 照旧显示 + 提示
#
#   安全性：全部用 **临时配置**（LMECHREVO_CONFIG_FILE 指到证据目录），
#           绝不碰 %APPDATA%\MechrevoLite\config.json；不发显卡指令、不写 NVRAM、不点任何按钮。
# ==============================================================================================
$ErrorActionPreference = 'Continue'
$dir = 'D:\openLM\diagnostics\d1-20261006-igpuhint'
$exe = 'D:\openLM\archive\tools\tools-20261005\d1shot\bin\Release\net10.0-windows10.0.19041.0\D1IgpuShot.exe'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$transcript = Join-Path $dir '_run_d1_evidence.txt'
Start-Transcript -Path $transcript -Force | Out-Null

function Write-Config([string]$path, [hashtable]$pairs) {
	$ordered = [ordered]@{ 'performance_mode' = 0 }
	foreach ($k in ($pairs.Keys | Sort-Object)) { $ordered[$k] = $pairs[$k] }
	($ordered | ConvertTo-Json -Depth 3) | Set-Content -Encoding UTF8 $path
}

$scenarios = @(
	@{ name = 's1-noigpu-ask';        fixture = 'noigpu'; offer = 'True';  guess = 'True';  button = 'True';  hint = 'yes'; switch = 'True';  cfg = @{ igpu_only_ui = 'show'; igpu_only_asked = 1 } },
	@{ name = 's2-noigpu-answer-yes'; fixture = 'noigpu'; offer = 'True';  guess = 'False'; button = 'True';  hint = 'no';  switch = 'True';  cfg = @{ igpu_only_ui = 'show'; igpu_only_asked = 1; igpu_only_answer = 'yes' } },
	@{ name = 's3-igpu-reliable';     fixture = 'igpu';   offer = 'True';  guess = 'False'; button = 'True';  hint = 'no';  switch = 'True';  cfg = @{ igpu_only_ui = 'show'; igpu_only_asked = 1 } },
	@{ name = 's4-noigpu-tried-ok';   fixture = 'noigpu'; offer = 'True';  guess = 'False'; button = 'True';  hint = 'no';  switch = 'True';  cfg = @{ igpu_only_ui = 'show'; igpu_only_answer = 'unknown'; igpu_route_verified = 1 } },
	@{ name = 's5-noigpu-tried-fail'; fixture = 'noigpu'; offer = 'False'; guess = 'False'; button = 'False'; hint = 'no';  switch = 'False'; cfg = @{ igpu_only_ui = 'show'; igpu_only_answer = 'unknown'; igpu_route_verified = 0 } },
	@{ name = 's6-noigpu-hidden';     fixture = 'noigpu'; offer = 'False'; guess = 'False'; button = 'False'; hint = 'no';  switch = 'False'; cfg = @{ igpu_only_ui = 'hide'; igpu_only_answer = 'no' } },
	@{ name = 's7-noigpu-pending';    fixture = 'noigpu'; offer = 'True';  guess = 'True';  button = 'True';  hint = 'yes'; switch = 'True';  cfg = @{ igpu_only_ui = 'show'; igpu_only_answer = 'unknown'; igpu_attempt_pending = 1 } }
)

$results = New-Object System.Collections.ArrayList
foreach ($s in $scenarios) {
	$name = $s.name
	$cfg = Join-Path $dir ("tmp-cfg-" + $name + ".json")
	$logDir = Join-Path $dir ("logs-" + $name)
	$evidence = Join-Path $dir ("d1-evidence-" + $name + ".txt")
	Remove-Item $evidence -Force -ErrorAction SilentlyContinue
	Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue
	New-Item -ItemType Directory -Force -Path $logDir | Out-Null
	Write-Config $cfg $s.cfg
	$env:LMECHREVO_CONFIG_FILE = $cfg
	$env:LMECHREVO_LOG_DIR = $logDir
	$env:LMECHREVO_LOG_LEVEL = 'All'
	Write-Host ("--- {0} (fixture={1}) ---" -f $name, $s.fixture)
	Get-Content $cfg | ForEach-Object { "      cfg: $_" }
	$p = Start-Process -FilePath $exe -ArgumentList @($dir, $name, $s.fixture) -PassThru
	$deadline = (Get-Date).AddSeconds(90)
	while (-not (Test-Path $evidence) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 700 }
	Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
	Get-Process -Name 'D1IgpuShot' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
	if (-not (Test-Path $evidence)) {
		Write-Host "      NO EVIDENCE FILE"
		[void]$results.Add([pscustomobject]@{ Scenario = $name; Offer = 'MISSING'; Guess = 'MISSING'; Hint = 'MISSING'; Switch = 'MISSING'; Verdict = 'FAIL(no file)' })
		continue
	}
	$txt = Get-Content $evidence -Encoding UTF8
	function FieldValue([string]$pattern) {
		$m = $txt | Select-String -Pattern $pattern | Select-Object -First 1
		if ($m -eq $null) { return '' }
		$v = ($m.Line -replace '^\s*[^=]+=\s*', '').Trim()
		# 证据行后面跟的中文注释（<== ...）不参与比对
		return ($v -replace '\s*<==.*$', '').Trim()
	}
	$offer = FieldValue 'CanOfferIgpuOnly\s*='
	$guess = FieldValue 'IgpuOnlyUiIsGuess\s*='
	$button = FieldValue 'buttonEco.Visible\s*='
	$hintLine = ($txt | Select-String -Pattern 'labelGPUFan.Text\s*=' | Select-Object -First 1).Line
	$hint = if ($hintLine -match '<== D-1') { ($hintLine -replace '^\s*labelGPUFan.Text\s*=\s*', '') -replace '\s*<==.*$', '' } else { '' }
	$hint = $hint.Trim()
	$switch = FieldValue 'checkShowIgpuOnlyMode.Checked\s*='
	$hasHint = if ($hint.Length -gt 0) { 'yes' } else { 'no' }
	$ok = ($offer -eq $s.offer) -and ($guess -eq $s.guess) -and ($hasHint -eq $s.hint) -and ($switch -eq $s.switch) -and ($button -eq $s.button)
	Write-Host ("      CanOfferIgpuOnly={0} IgpuOnlyUiIsGuess={1} buttonEco.Visible={2} hint='{3}' switch={4}" -f $offer, $guess, $button, $hint, $switch)
	[void]$results.Add([pscustomobject]@{
		Scenario = $name
		Offer = $offer; Guess = $guess; Button = $button; Hint = $hint; Switch = $switch
		Verdict = if ($ok) { 'PASS' } else { ('FAIL(exp offer={0} guess={1} button={2} hint={3} switch={4})' -f $s.offer, $s.guess, $s.button, $s.hint, $s.switch) }
	})
}

Write-Host ''
Write-Host '=== summary ==='
$results | Format-Table -AutoSize | Out-String -Width 200
$results | Export-Csv -Encoding UTF8 -NoTypeInformation (Join-Path $dir 'd1-scenario-summary.csv')

$phase0 = Select-String -Path (Join-Path $dir 'd1-evidence-s1-noigpu-ask.txt') -Pattern '\[(PASS|FAIL)\]' -Encoding UTF8
Write-Host '=== phase 0 (RecordBootVerdict self-check) ==='
$phase0 | ForEach-Object { $_.Line }

Remove-Item Env:LMECHREVO_CONFIG_FILE -ErrorAction SilentlyContinue
Remove-Item Env:LMECHREVO_LOG_DIR -ErrorAction SilentlyContinue
Remove-Item Env:LMECHREVO_LOG_LEVEL -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '=== done ==='
Stop-Transcript | Out-Null
