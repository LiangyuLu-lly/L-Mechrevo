# -*- coding: utf-8 -*-
r"""
去掉两个 resx 里插在 <data> 之间的 XML 注释
================================================================================
现象：新增的 ModeConfigTiny 在两个 .resources 中间产物里都不存在
      （而紧邻的 ChargeTaskLocationFailed / ModeConfigShort 都在）
怀疑：.resx 用 DataSet 读取，<data> 之间插 XML 注释会让后续条目被丢弃
做法：删掉那两行注释（保留 <data> 元素本身，缩进对齐原风格）
"""
import io
import sys

TARGETS = [
    (r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
     '<!-- 2026-10-05: short form used only inside the narrow perf-config combo (Width = D(72));\n'
     '     "Config 1" is clipped to "Conf..." there. Keep ModeConfigShort for buttons/titles. -->\n'),
    (r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
     '<!-- 2026-10-05: 仅用于窄下拉框（Width = D(72)）；中文此宽度足够，故与 Short 同值。 -->\n'),
]


def main():
    ok = True
    for path, comment in TARGETS:
        data = io.open(path, 'rb').read()
        for enc_comment in (comment.encode('utf-8'),
                            comment.replace('\n', '\r\n').encode('utf-8')):
            if enc_comment in data:
                if '--check' in sys.argv:
                    print('  [check] 将删除注释: %s' % path.split('\\')[-1])
                else:
                    io.open(path, 'wb').write(data.replace(enc_comment, b'', 1))
                    print('  已删注释: %s' % path.split('\\')[-1])
                break
        else:
            print('  ! 未找到注释（可能已删）: %s' % path.split('\\')[-1])
            ok = False
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
