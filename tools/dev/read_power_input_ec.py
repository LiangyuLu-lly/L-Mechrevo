# -*- coding: utf-8 -*-
# 读取 ACPIDriver EC 寄存器 1996(0x7CC)/1183(0x49F)，解码供电方式与适配器瓦数
import ctypes
import sys

kernel32 = ctypes.windll.kernel32
IOCTL_READ = 2621482120  # 0x9C07D2D0

WATT_TABLE = {
    0: 330, 8: 230, 16: 180, 24: 150,
    32: 120, 40: 90, 48: 65, 56: 40, 64: 280,
}

h = kernel32.CreateFileW(
    r"\\.\ACPIDriver",
    0xC0000000,  # GENERIC_READ | GENERIC_WRITE
    3,           # FILE_SHARE_READ | FILE_SHARE_WRITE
    None, 3, 0, None,
)
if h == -1 or h == 0xFFFFFFFFFFFFFFFF or h == 0:
    print("ACPIDriver 打开失败（需要管理员权限运行本脚本）")
    sys.exit(1)

def ec_read(addr):
    inbuf = ctypes.c_uint(addr)
    outbuf = (ctypes.c_ubyte * 16)()
    ret = ctypes.c_int(0)
    ok = kernel32.DeviceIoControl(h, IOCTL_READ, ctypes.byref(inbuf), 4, outbuf, 16, ctypes.byref(ret), None)
    if not ok:
        return None
    return outbuf[0]

c7cc = ec_read(1996)   # 0x7CC 复杂供电状态
c49f = ec_read(1183)   # 0x49F 适配器编码

if c7cc is None or c49f is None:
    print("EC 读取失败：0x7CC=%s 0x49F=%s" % (c7cc, c49f))
    kernel32.CloseHandle(h)
    sys.exit(1)

round_socket = bool(c7cc & 1)
type_c = bool(c7cc & 6)

if round_socket and type_c:
    kind = "圆口 + Type-C"
elif round_socket:
    kind = "圆口"
elif type_c:
    kind = "Type-C"
else:
    kind = "外部供电（方式未知）"

code = c49f & 0x78
watts = WATT_TABLE.get(code)
watts_text = ("%dW" % watts) if watts else "未解码（编码 0x%02X）" % code

print("0x7CC(1996) = 0x%02X -> 供电方式: %s" % (c7cc, kind))
print("0x49F(1183) = 0x%02X -> 编码 0x%02X -> %s" % (c49f, code, watts_text))

kernel32.CloseHandle(h)
