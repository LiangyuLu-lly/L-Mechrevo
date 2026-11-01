# -*- coding: utf-8 -*-
# resx 提示文本精简（两行）
def edit_resx(path, name, new_value, tag):
    raw = open(path, 'rb').read()
    has_bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig' if has_bom else 'utf-8')
    old = '<data name="' + name + '" xml:space="preserve">'
    i = text.find(old)
    if i < 0:
        raise SystemExit('ABORT ' + tag + ': data 标签未找到')
    j = text.find('<value>', i)
    k = text.find('</value>', j)
    if j < 0 or k < 0:
        raise SystemExit('ABORT ' + tag + ': value 未找到')
    text = text[:j] + '<value>' + new_value + text[k + len('</value>'):]
    open(path, 'wb').write(text.encode('utf-8-sig' if has_bom else 'utf-8'))
    print('[' + tag + '] OK')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', 'CustomCalibrationHint',
          '开启后，校色下拉列表将出现“自定义”选项，\n经 Windows 颜色管理应用所选 ICC（不经由 GCU）；关闭则取消所有已应用的 ICC。',
          'resx-zh-hint')

edit_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', 'CustomCalibrationHint',
          'When enabled, a "Custom" option appears in the calibration list.\nIt applies the selected ICC via Windows color management (not via GCU); turning it off removes all applied ICC profiles.',
          'resx-en-hint')

print('ALL OK')
