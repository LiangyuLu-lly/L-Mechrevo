# ==============================================================================================
# L-Mechrevo — PawnIO 驱动安装脚本（本仓库自有脚本，非第三方代码）。
#
# 2026-10-10 合规：PawnIO 内核驱动本身是 GPL-2.0（https://github.com/namazso/PawnIO）。
#   我们**不随程序分发**该驱动：本脚本在用户显式点击「下载 PawnIO 驱动」后，从上游官方发布
#   自行下载，校验 SHA-256 与 Authenticode 签名后才静默安装。因此 L-Mechrevo 不承担驱动
#   的再分发义务。许可文本（若需要）：LICENSES\GPL-2.0.txt（未随包提供，仅备注）。
#   已按要求重新核对：namazso/PawnIO.Setup 最新发布仍为 2.2.0（GitHub API，2026-03-15 发布），
#   其资产 sha256 摘要 = 本脚本 $expectedHash，两者一致 —— 固定值未过期。
#
# 恢复来源：_upstream\0.290.6\extract\{app}\Install-PawnIO.ps1（我们自己的上一个发布版本），
#   原文 2339 B，仅在此处追加本注释块，其余内容逐字节保留。
#   installer-src\install_script.iss 现在会把它发到 {app}\，因为
#   source\MechrevoLite\CpuTuningForm.cs 仍以 {app}\Install-PawnIO.ps1 启动它。
# ==============================================================================================
[CmdletBinding()]
param([string]$InstallerPath, [switch]$ValidateOnly)

$ErrorActionPreference = 'Stop'
$expectedHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
try {
    if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
        $downloadDir = Join-Path ([IO.Path]::GetTempPath()) ('L-Mechrevo-PawnIO-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $downloadDir | Out-Null
        $InstallerPath = Join-Path $downloadDir 'PawnIO_setup.exe'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $InstallerPath -TimeoutSec 120
    }
    $InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
    if ((Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'PawnIO 2.2.0 SHA256 mismatch; installation refused.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $InstallerPath
    if ($signature.Status -ne 'Valid') { throw ('PawnIO digital signature is not valid: ' + $signature.Status) }
    if ($ValidateOnly) { Write-Output 'PawnIO installer hash and signature verified.'; exit 0 }
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator privileges required.' }
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'"
    if ($driver -and $driver.State -eq 'Running') { Write-Output 'PawnIO is already running.'; exit 0 }
    $process = Start-Process -FilePath $InstallerPath -ArgumentList '-install','-silent' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -eq 3010) { Write-Output 'PawnIO installed; restart required.'; exit 3010 }
    if ($process.ExitCode -ne 0) { throw ('PawnIO installer exit code: ' + $process.ExitCode) }
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'"
    if (-not $driver -or $driver.State -ne 'Running') { throw 'PawnIO installation returned success, but the driver is not running.' }
    Write-Output 'PawnIO installed and running.'
    exit 0
} catch { Write-Error $_; exit 1 }
