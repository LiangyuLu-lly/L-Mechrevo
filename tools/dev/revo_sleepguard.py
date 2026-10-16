# Ghidra post-script: locate sleep-guard strings, decompile referencing functions
# @category Revo
import sys
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.listing import CodeUnit
from ghidra.util.task import ConsoleTaskMonitor

out_path = r'D:\openLM\tools\revo_decomp.txt'
targets = [
    'sleep_guard_enabled', 'sleep_disconnect_net', 'sleep_block_away',
    'sleep_ignore_sysreq', 'sleep_disable_wake_timers', 'sleep_deep_sleep',
    'sleep_hibernate_30m', 'Invalid sleep guard option', 'set_master_sleep_guard',
    'set_sleep_guard_option', 'get_sleep_guard', 'disconnect_net', 'block_away',
    'ignore_sysreq', 'disable_wake_timers', 'deep_sleep', 'hibernate_30m',
    'hibernate-timeout-dc', 'hibernate-timeout-ac', 'F15576E8', '75b0ae3f',
    '238c9fa8', 'bd3b718a', 'Networking connectivity', 'AwayMode', 'Session Manager',
    'PlatformAoAc', 'CsEnabled', 'ES_SYSTEM_REQUIRED', 'PowerSetActiveScheme',
    'set_active_windows_power_scheme', 'preferred_power_scheme', 'keep_awake'
]

f = open(out_path, 'w', encoding='utf-8', errors='replace')

listing = currentProgram.getListing()
rm = currentProgram.getReferenceManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)

def find_string_addrs(target):
    found = []
    data_iter = listing.getDefinedData(True)
    while data_iter.hasNext():
        d = data_iter.next()
        if d is None:
            continue
        try:
            v = d.getValue()
            if v is not None and isinstance(v, str) and target in v:
                found.append((d.getAddress(), v[:120]))
        except Exception:
            pass
    return found

monitor = ConsoleTaskMonitor()
seen_fns = set()

for t in targets:
    hits = find_string_addrs(t)
    if not hits:
        f.write('### NOT FOUND: %s\n' % t)
        continue
    f.write('### FOUND %s (%d):\n' % (t, len(hits)))
    for addr, val in hits:
        f.write('   addr=%s val=%r\n' % (addr, val))
        refs = rm.getReferencesTo(addr)
        for r in refs:
            from_addr = r.getFromAddress()
            fn = getFunctionContaining(from_addr)
            if fn is None:
                continue
            f.write('   -> ref from %s in function %s\n' % (from_addr, fn.getName()))
            if fn.getEntryPoint().getOffset() in seen_fns:
                continue
            seen_fns.add(fn.getEntryPoint().getOffset())
            res = decomp.decompileFunction(fn, 120, monitor)
            if res and res.depiledFunction() is not None:
                f.write('   ---- decompiled %s ----\n' % fn.getName())
                f.write(res.getDecompiledFunction().getC())
                f.write('\n')
f.write('\n=== TOTAL functions decompiled: %d ===\n' % len(seen_fns))
f.close()
print('DONE. output:', out_path)
