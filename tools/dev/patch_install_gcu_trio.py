# -*- coding: utf-8 -*-
import io

p = r'D:\openLM\installer-src\files\GCU\Install-Gcu.ps1'
s = io.open(p, encoding='utf-8-sig').read()

# 1) trio 源提升到 AiStoneService 根（整树，含根目录运行时/GCUBridge）
old1 = "$trioServiceSource = Join-Path (Join-Path (Join-Path $StagingRoot $selection.StagedDir) $selection.ServiceDir) 'MyControlCenter'"
new1 = "$trioServiceSource = Join-Path (Join-Path $StagingRoot $selection.StagedDir) $selection.ServiceDir"
assert s.count(old1) == 1, 'old1 count=%d' % s.count(old1)
s = s.replace(old1, new1)

# 2) 校验 GCUService.exe 的位置（统一在 MyControlCenter 下）
old2 = "if (-not (Test-Path -LiteralPath (Join-Path $trioServiceSource 'GCUService.exe'))) {"
new2 = "if (-not (Test-Path -LiteralPath (Join-Path $trioServiceSource 'MyControlCenter\\GCUService.exe'))) {"
assert s.count(old2) == 1, 'old2 count=%d' % s.count(old2)
s = s.replace(old2, new2)

# 3) 复制目标改为 serviceTarget 整树（trio 根文件覆盖 base 的同名根文件，如 30 系 GCUBridge/运行时）
old3 = "Copy-Tree -Source $trioServiceSource -Destination (Join-Path $serviceTarget 'MyControlCenter')"
new3 = "Copy-Tree -Source $trioServiceSource -Destination $serviceTarget"
assert s.count(old3) == 1, 'old3 count=%d' % s.count(old3)
s = s.replace(old3, new3)

io.open(p, 'w', encoding='utf-8-sig').write(s)
print('OK, 3 replacements done')
