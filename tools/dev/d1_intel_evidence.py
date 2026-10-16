# -*- coding: utf-8 -*-
"""2026-10-06 D-1：Intel（⑦B / A-1）搁置证据（只读：源码引用 + 资源键 + 编译产物字符串）。"""
import io
import os

OUT = r'D:\openLM\diagnostics\d1-20261006-igpuhint\intel-shelved-evidence.txt'
SRC = r'D:\openLM\source'
RCT = os.path.join(SRC, r'MechrevoLite.Hardware\RuntimeCpuTuning.cs')
UAR = os.path.join(SRC, r'MechrevoLite.UI\UiAuditRunner.cs')
RESX = [os.path.join(SRC, 'MechrevoLite.Properties.Strings.resx'),
        os.path.join(SRC, 'MechrevoLite.Properties.Strings.zh-CN.resx')]
CSPROJ = os.path.join(SRC, 'L-Mechrevo.csproj')
DLL = r'C:\Program Files\L-Mechrevo\L-Mechrevo.dll'

L = []
w = L.append


def lines_of(path):
    return io.open(path, encoding='utf-8', errors='replace').read().split('\n')


def show(title, path, nums):
    w('### %s' % title)
    w('    file = %s' % path)
    src = lines_of(path)
    for n in nums:
        w('  %5d: %s' % (n, src[n - 1].rstrip('\r')))
    w('')


w('2026-10-06 D-1 附带：Intel（上游第 7 步 B / 待办 A-1）搁置 ——')
w('“不实现、保留上游代码、运行时路径仍是明确不支持 + 说明”的证据。')
w('（只读：源码引用 + 资源键 + 编译产物字符串。本机是 AMD，Intel 分支在本机不可达，故为源码级证据。）')
w('')

show('① 厂商识别：Intel 分支的入口（本机 CpuId 返回 AuthenticAMD，走不到这里）', RCT, [848, 849, 850, 851, 852, 853, 854, 855, 856, 857, 858])
show('② Probe()：Intel 时返回 CpuTuneUnsupported + 说明，不发任何寄存器操作', RCT, [269, 270, 271, 272, 273, 274, 275, 276, 277, 278])
show('③ Apply()：intel-* 选项一律拒绝', RCT, [306, 307, 308, 309])
show('④ Restore()：intel-* 选项一律拒绝（抛 NotSupportedException）', RCT, [344, 345, 346, 347])
show('⑤ 文件头注释：IntelOcMailbox + IntelMSR.bin 是第 7B 步，故意没进这份构建（所以不会碰 MSR 0x150）', RCT, [16, 17, 18])
w('### ⑥ 上游 Intel 相关资源键（两个 resx 各 3 个，键名原样保留、中英同步）')
for p in RESX:
    src = lines_of(p)
    w('    %s:' % os.path.basename(p))
    for i, line in enumerate(src):
        if '<data name="' in line and 'Intel' in line:
            w('      %s' % line.strip())
w('')
show('⑦ 上游 Intel 合成夹具仍在（截图审计用；它不是运行时能力）', UAR, [172, 173, 174, 175, 176, 177])
w('### ⑧ 编译产物字符串检查（部署态主程序集）')
raw = open(DLL, 'rb').read()
for needle in ['IntelMSR', 'IntelOcMailbox', 'MSR 0x150', 'GenuineIntel', 'Intel runtime tuning is not part of this build']:
    u8 = needle.encode('utf-8') in raw
    u16 = needle.encode('utf-16-le') in raw
    w('    %-52s utf8=%-6s utf16=%s' % (needle, u8, u16))
w('')
w('### ⑨ csproj 里与驱动/二进制模块有关的行（确认没有 IntelMSR.bin 之类被打进产物）')
cs = [l for l in lines_of(CSPROJ) if ('EmbeddedResource' in l or '.bin' in l or 'PawnIO' in l or 'Reference' in l)]
for l in cs:
    w('    %s' % l.strip())
if not cs:
    w('    （无匹配行）')
w('')
w('### ⑩ 结论')
w('    * Intel 相关实现/枚举/资源/夹具：全部保留在原处，本次一个字未删、未改；')
w('    * 运行时行为：Intel CPU 上 Probe() 返回 CpuTuneUnsupported（详情 "Intel runtime tuning is not part of')
w('      this build (step 7B pending)."），Apply/Restore 对 intel-* 一律拒绝 —— 不发任何寄存器/邮箱操作；')
w('    * 缺失的那个第三方签名驱动模块（IntelOcMailbox + IntelMSR.bin）**没有**被引入本构建，')
w('      编译产物里也找不到相关字符串；')
w('    * 等原作者修（用户 2026-10-06 决定：「走2 英特尔让原作者修 搁置保留他的代码就行」）。')

io.open(OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(L) + '\n')
print('written', OUT, len(L), 'lines')
