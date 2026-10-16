# Ghidra post-script v2: fast byte-search for sleep-guard strings, decompile direct referencing functions
# @category Revo
import array
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

out_path = r'D:\openLM\tools\revo_decomp.txt'
targets = [
    b'sleep_guard_enabled', b'sleep_disconnect_net', b'sleep_block_away',
    b'sleep_ignore_sysreq', b'sleep_disable_wake_timers', b'sleep_deep_sleep',
    b'sleep_hibernate_30m', b'Invalid sleep guard option', b'set_master_sleep_guard',
    b'set_sleep_guard_option', b'get_sleep_guard', b'disconnect_net', b'block_away',
    b'ignore_sysreq', b'disable_wake_timers', b'deep_sleep', b'hibernate_30m',
    b'hibernate-timeout-dc', b'hibernate-timeout-ac', b'75b0ae3f', b'238c9fa8',
    b'bd3b718a', b'Networking connectivity', b'AwayMode', b'Session Manager',
    b'PlatformAoAc', b'CsEnabled', b'PowerSetActiveScheme', b'keep_awake'
]

f = open(out_path, 'w', encoding='utf-8', errors='replace')
mem = currentProgram.getMemory()
rm = currentProgram.getReferenceManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
seen_fns = set()

# collect image bytes once
blocks = []
for block in mem.getBlocks():
    if block.isInitialized() and block.isLoaded():
        try:
            blocks.append((block, bytearray(block.getData())))
        except Exception:
            pass
total = sum(len(b) for _, b in blocks)
f.write('blocks=%d total_bytes=%d\n' % (len(blocks), total))

def find_in_bytes(needle):
    hits = []
    for block, data in blocks:
        start = 0
        while True:
            idx = data.find(needle, start)
            if idx < 0:
                break
            hits.append(block.getAddress().add(idx))
            start = idx + 1
    return hits

for t in targets:
    hits = find_in_bytes(t)
    if not hits:
        f.write('### NOT FOUND: %s\n' % t.decode('ascii', 'replace'))
        continue
    f.write('### FOUND %s (%d):\n' % (t.decode('ascii', 'replace'), len(hits)))
    for addr in hits[:5]:
        fn = getFunctionContaining(addr)
        f.write('   addr=%s fn=%s\n' % (addr, fn.getName() if fn else None))
        if fn is None:
            continue
        refs = rm.getReferencesTo(addr)
        for r in refs:
            f_ = getFunctionContaining(r.getFromAddress())
            if f_ is None:
                continue
            key = f_.getEntryPoint().getOffset()
            if key in seen_fns:
                continue
            seen_fns.add(key)
            f.write('   -> REF %s -> function %s @ %s\n' % (r.getFromAddress(), f_.getName(), f_.getEntryPoint()))
            res = decomp.decompileFunction(f_, 90, monitor)
            if res and res.decompileCompleted():
                cf = res.getDecompiledFunction()
                if cf:
                    f.write('   ---- decompiled %s ----\n' % f_.getName())
                    f.write(cf.getC())
                    f.write('\n')
            else:
                f.write('   ---- decompile FAILED/TIMEOUT for %s ----\n' % f_.getName())
f.write('\n=== TOTAL functions decompiled: %d ===\n' % len(seen_fns))
f.flush()
f.close()
print('DONE. output:', out_path)
