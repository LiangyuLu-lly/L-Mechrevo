# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '\t\t\tif (hotIgpuSwitch)\n\t\t\t\t{\n\t\t\t\t\tLogger.WriteLine($"Manual hot switch to iGPU not confirmed; offering reboot-to-apply (expected={GPUMode} actual={Program.hw.GpuMode})");\n\t\t\t\t\tawait OfferRebootFallbackAsync(modeBeforeSwitch, GPUMode).ConfigureAwait(continueOnCapturedContext: false);\n\t\t\t\t\treturn;\n\t\t\t\t}\n'
assert old in t, 'not found'
t = t.replace(old, '', 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('1b OK')
