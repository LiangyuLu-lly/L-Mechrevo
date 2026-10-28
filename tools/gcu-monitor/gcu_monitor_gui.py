# -*- coding: utf-8 -*-
"""
gcu_monitor_gui.py — 机械革命翼龙15 Pro 实时监控（图形界面版 v4.4）

功能：
  · 风扇转速/百分比、CPU/GPU 温度与占用率、显存温度、内存、网络、功耗（MQTT + LHM）
  · GPU 卡可切换监控独显（RTX 4060）/ 核显（Radeon 780M），数据源自动切换
  · LHM 的 DLL 已封装进 exe（PyInstaller --add-binary），单文件零外部依赖
  · 风扇强冷开关（与 L-Mechrevo 控制台等效，Fan/Control 发布）
  · 悬浮窗：置顶迷你面板、可拖动、双击关闭、半透明、显示项目可自定义
  · 半透明采用 Win32 分层窗口自绘：背景+文字画进一张带透明通道的位图，
    整体随透明度一起变化（per-pixel alpha，无分层错位，文字始终清晰锐利）
  · 自动提权（打包时 --uac-admin）：双击弹 UAC 后以管理员运行，CPU 功耗可读
  · 悬浮窗配置保存于 exe 同目录 gcu_float.json（显示项、透明度、位置）

数据来源：
  · MQTT：本机 GCUBridge 服务 127.0.0.1:13688（UWPClient_9 专用槽位，只读订阅 + 强冷发布）
  · 功耗：L-Mechrevo 自带的 LibreHardwareMonitor 开源库（pythonnet 宿主 .NET Core）

打包：
  pyinstaller --onefile --noconsole --uac-admin --icon=gcu.ico --name gcu_monitor gcu_monitor_gui.py
"""
import os
import re
import sys
import json
import time
import ctypes
import struct
import threading
import datetime
import logging
from ctypes import wintypes

import tkinter as tk
from tkinter import font as tkfont

import paho.mqtt.client as mqtt
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageChops

try:
    import psutil
except Exception:
    psutil = None

if hasattr(mqtt, "CallbackAPIVersion"):
    _MQTT_KW = {"callback_api_version": mqtt.CallbackAPIVersion.VERSION2}
else:
    _MQTT_KW = {}

HOST = "127.0.0.1"
PORT = 13688
CLIENT_ID = "UWPClient_9"
USER = "UWPClient_User_9"
PWD = "UWPClient_Pwd888881772688_9"
# MQTT 仅用于强冷（GCU 通道）：订阅强冷状态，发布强冷命令；监控数据全部独立直读
SUB_TOPICS = [("Fan/Status", 0)]
REFRESH_MS = 2000

# 日志
try:
    LOG_PATH = os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])), "gcu_gui.log")
    logging.basicConfig(filename=LOG_PATH, level=logging.INFO,
                        format="%(asctime)s %(levelname)s %(message)s")
except Exception:
    logging.basicConfig(level=logging.CRITICAL)

# ---------- LHM 功耗/温度读取（DLL 已封装进 exe，零外部依赖） ----------
def _lhm_dir():
    if getattr(sys, "frozen", False):
        return getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(sys.argv[0])))
    return os.path.dirname(os.path.abspath(__file__))


LHM_OK = False
try:
    from pythonnet import load as _pynet_load
    _pynet_load("coreclr")
    import clr
    _LHM_BASE = _lhm_dir()
    clr.AddReference(os.path.join(_LHM_BASE, "HidSharp.dll"))
    clr.AddReference(os.path.join(_LHM_BASE, "LibreHardwareMonitorLib.dll"))
    import LibreHardwareMonitor.Hardware as HW
    LHM_OK = True
except Exception as e:
    logging.warning("LHM init failed: %s", e)


def is_admin():
    try:
        import ctypes
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


class LhmReader:
    """周期读取 LHM 功耗传感器（独立线程）"""
    def __init__(self):
        self._comp = None
        self._ok = False
        self.power = {}
        self.temp = {}
        self.clock = {}
        self.load = {}
        self.mem = {}
        self._stop = False
        if LHM_OK:
            try:
                comp = HW.Computer()
                comp.IsCpuEnabled = True
                comp.IsGpuEnabled = True
                comp.IsControllerEnabled = False
                comp.Open()
                self._comp = comp
                self._ok = True
            except Exception as e:
                logging.warning("LHM open failed: %s", e)
        self._t = threading.Thread(target=self._loop, daemon=True)
        self._t.start()

    def _collect(self):
        out = {"Power": {}, "Temperature": {}, "Clock": {}, "Load": {}, "SmallData": {}}
        def walk(hw):
            try:
                hw.Update()
            except Exception:
                pass
            for s in hw.Sensors:
                if s.Value is None:
                    continue
                st = str(s.SensorType)
                if st in out:
                    key = "%s|%s" % (hw.Name, s.Name)
                    out[st][key] = round(float(s.Value), 1)
            for sub in hw.SubHardware:
                walk(sub)
        if self._comp is not None:
            try:
                for hw in self._comp.Hardware:
                    walk(hw)
            except Exception:
                pass
        return out

    def _loop(self):
        while not self._stop:
            try:
                d = self._collect()
                self.power = d.get("Power", {})
                self.temp = d.get("Temperature", {})
                self.clock = d.get("Clock", {})
                self.load = d.get("Load", {})
                self.mem = d.get("SmallData", {})
            except Exception:
                pass
            time.sleep(2)

    def stop(self):
        self._stop = True
        if self._comp is not None:
            try:
                self._comp.Close()
            except Exception:
                pass

    def cpu_w(self):
        for k, v in self.power.items():
            if "8745H" in k and "Package" in k and v > 0:
                return v
        return None

    def gpu_w(self):
        for k, v in self.power.items():
            if "RTX 4060" in k and "Package" in k and v > 0:
                return v
        return None

    def igpu_w(self):
        total = 0.0
        for k, v in self.power.items():
            if "Radeon 780M" in k and ("GPU Core" in k or "GPU SoC" in k):
                total += v
        return total

    def igpu_temp(self):
        for k, v in self.temp.items():
            if "Radeon 780M" in k and "VR SoC" in k:
                return v
        return None

    def igpu_load(self):
        for k, v in self.load.items():
            if "Radeon 780M" in k and "GPU Core" in k:
                return v
        return None

    def igpu_freq_core(self):
        for k, v in self.clock.items():
            if "Radeon 780M" in k and "GPU Core" in k:
                return v
        return None

    def igpu_freq_mem(self):
        for k, v in self.clock.items():
            if "Radeon 780M" in k and "GPU Memory" in k:
                return v
        return None

    def igpu_mem_used(self):
        for k, v in self.mem.items():
            if "Radeon 780M" in k and "GPU Memory Used" in k:
                return v
        return None

    def gpu_memtemp(self):
        for k, v in self.temp.items():
            if "RTX 4060" in k and "Memory Junction" in k and v > 0:
                return v
        return None

    # ---- 独显（RTX 4060）数据 ----
    def dgpu_temp(self):
        for k, v in self.temp.items():
            if "RTX 4060" in k and "GPU Core" in k and v > 0:
                return v
        return None

    def dgpu_load(self):
        for k, v in self.load.items():
            if "RTX 4060" in k and "GPU Core" in k:
                return v
        return None

    def dgpu_freq_core(self):
        for k, v in self.clock.items():
            if "RTX 4060" in k and "GPU Core" in k:
                return v
        return None

    def dgpu_freq_mem(self):
        for k, v in self.clock.items():
            if "RTX 4060" in k and "GPU Memory" in k:
                return v
        return None

    def dgpu_mem_used(self):
        for k, v in self.mem.items():
            if "RTX 4060" in k and "GPU Memory Used" in k:
                return v
        return None

    # ---- CPU 温度（LHM） ----
    def cpu_temp_lhm(self):
        for k, v in self.temp.items():
            if "8745H" in k and ("Tctl" in k or "Tdie" in k or "CPU Package" in k or "Core" in k and "Average" in k):
                if v > 0:
                    return v
        return None


# ---------- EC 直读（UWACPI 驱动，独立数据源，不依赖官方控制台） ----------
# 协议来源：mech-forza-control 逆向寄存器映射，实测与官方控制台数据完全一致。
# 只发读取 IOCTL（0x9C402008），绝不写 EC —— 与官方 ACPIDriverDLL 同协议，安全。
EC_DEV_PATH = r"\\.\ACPIDriver"
IOCTL_EC_READ = 2621482120        # 0x9C402008
EC_CPU_TEMP = 0x043E              # EC 温控温度（风扇控制参考点）
EC_MAIN_FAN_RPM_HI = 0x0464       # CPU 风扇 RPM 高字节
EC_MAIN_FAN_RPM_LO = 0x0465       # CPU 风扇 RPM 低字节
EC_SEC_FAN_RPM_LO = 0x046B        # GPU 风扇 RPM 低字节
EC_SEC_FAN_RPM_HI = 0x046C        # GPU 风扇 RPM 高字节
EC_MAIN_FAN_DUTY = 0x075B         # CPU 风扇占空比（值/2 ≈ %）
EC_SEC_FAN_DUTY = 0x075C          # GPU 风扇占空比（值/2 ≈ %）


class EcReader:
    """EC 直读（纯读取、零写入）：风扇转速/占空比，独立于官方控制台与 MQTT。"""
    def __init__(self):
        self.ok = False
        self.main_rpm = None
        self.sec_rpm = None
        self.main_duty = None
        self.sec_duty = None
        self.cpu_temp = None
        self._h = None
        self._lock = threading.Lock()
        self._stop = False
        try:
            k = ctypes.windll.kernel32
            h = k.CreateFileW(EC_DEV_PATH, 0xC0000000, 3, None, 3, 0, None)
            if h in (-1, 0xFFFFFFFFFFFFFFFF):
                logging.warning("EC 驱动不可用 err=%s（风扇将回退 MQTT）",
                                ctypes.get_last_error())
            else:
                self._h = h
                self._k = k
                self.ok = True
                logging.info("EC 直读已启用（UWACPIDriver）")
        except Exception as e:
            logging.warning("EC init failed: %s", e)
        if self.ok:
            self._t = threading.Thread(target=self._loop, daemon=True)
            self._t.start()

    def _read(self, addr):
        try:
            with self._lock:
                inbuf = struct.pack("<II", addr, 1)
                out = ctypes.c_int(0)
                if not self._k.DeviceIoControl(self._h, IOCTL_EC_READ, inbuf, 8,
                                               ctypes.byref(out), 4, None, None):
                    return None
                return out.value & 0xFF
        except Exception:
            return None

    def _read16(self, lo_a, hi_a):
        lo, hi = self._read(lo_a), self._read(hi_a)
        if lo is None or hi is None:
            return None
        return (hi << 8) | lo

    def _loop(self):
        while not self._stop:
            try:
                self.main_rpm = self._read16(EC_MAIN_FAN_RPM_LO, EC_MAIN_FAN_RPM_HI)
                self.sec_rpm = self._read16(EC_SEC_FAN_RPM_LO, EC_SEC_FAN_RPM_HI)
                self.main_duty = self._read(EC_MAIN_FAN_DUTY)
                self.sec_duty = self._read(EC_SEC_FAN_DUTY)
                self.cpu_temp = self._read(EC_CPU_TEMP)
            except Exception as e:
                logging.error("EC loop: %s", e)
            time.sleep(2)

    def stop(self):
        self._stop = True
        if self._h is not None:
            try:
                self._k.CloseHandle(self._h)
            except Exception:
                pass
            self._h = None


# 配色
C_BG = "#0e1116"
C_CARD = "#161b24"
C_BORDER = "#232a36"
C_TITLE = "#f1f5f9"
C_TEXT = "#cbd5e1"
C_DIM = "#64748b"
C_ACCENT = "#22d3ee"
C_OK = "#4ade80"
C_ERR = "#f87171"
C_WARN = "#fbbf24"
C_TRANS = "#010203"       # 透明魔法色（保留兼容）

# ---------- Win32 分层窗口自绘（per-pixel alpha 半透明悬浮窗） ----------
GWL_EXSTYLE = -20
_WS_EX_LAYERED = 0x00080000
_WS_EX_TOOLWINDOW = 0x00000080
_ULW_ALPHA = 0x02
_AC_SRC_OVER = 0x00
_CN_RE = re.compile(r'[\u4e00-\u9fff]+|[^\u4e00-\u9fff]+')

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32


class _BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long),
                ("biHeight", ctypes.c_long), ("biPlanes", wintypes.WORD),
                ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long),
                ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wintypes.DWORD),
                ("biClrImportant", wintypes.DWORD)]


class _BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", _BITMAPINFOHEADER)]


class _BLENDFUNCTION(ctypes.Structure):
    _fields_ = [("BlendOp", ctypes.c_ubyte), ("BlendFlags", ctypes.c_ubyte),
                ("SourceConstantAlpha", ctypes.c_ubyte), ("AlphaFormat", ctypes.c_ubyte)]


class _POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class _SIZE(ctypes.Structure):
    _fields_ = [("cx", ctypes.c_long), ("cy", ctypes.c_long)]


class FloatPainter:
    """自绘半透明悬浮窗：背景+文字画进一张带 alpha 的位图，整体随透明度一起变化。"""
    _F_LAT = None
    _F_CN = None
    ROW_H = 26
    PAD_X = 12
    PAD_TOP = 9

    @classmethod
    def _fonts(cls):
        if cls._F_LAT is None:
            try:
                cls._F_LAT = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 15)
            except Exception:
                cls._F_LAT = ImageFont.load_default()
            try:
                cls._F_CN = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 15)
            except Exception:
                cls._F_CN = cls._F_LAT
        return cls._F_LAT, cls._F_CN

    @classmethod
    def text_w(cls, text):
        fl, fc = cls._fonts()
        w = 0.0
        for seg in _CN_RE.findall(text):
            f = fc if seg and '\u4e00' <= seg[0] <= '\u9fff' else fl
            w += f.getlength(seg)
        return w

    @classmethod
    def measure(cls, texts):
        return max(cls.text_w(t) for t in texts)

    @classmethod
    def draw(cls, w, h, alpha, rows):
        """rows: [(text, hex_color)]；alpha 0-1 只作用于背景卡，
        文字永远 100% 清晰（alpha=255）；返回预乘后的 RGBA 位图。
        文字先画在独立透明层（边缘像素 RGB 保持文字色，不混入暗背景），
        再覆盖到背景上，避免文字边缘出现黑色杂块。"""
        a = int(255 * alpha)
        bg = Image.new("RGBA", (w, h), (14, 17, 22, a))
        d = ImageDraw.Draw(bg)
        d.rectangle([0, 0, w - 1, h - 1], outline=(34, 211, 238, a), width=1)
        # 文字渲染成 alpha mask，再重建为固定文字色的 RGBA 层
        # （FreeType 会同时调制 RGB，直接画会污染文字颜色；mask 只取覆盖率）
        fl, fc = cls._fonts()
        y = cls.PAD_TOP
        for text, hexc in rows:
            r, g, b = int(hexc[1:3], 16), int(hexc[3:5], 16), int(hexc[5:7], 16)
            mk = Image.new("L", (w, h), 0)
            dm = ImageDraw.Draw(mk)
            x = cls.PAD_X
            for seg in _CN_RE.findall(text):
                f = fc if seg and '\u4e00' <= seg[0] <= '\u9fff' else fl
                dm.text((x, y), seg, font=f, fill=255)
                x += f.getlength(seg)
            txt = Image.merge("RGBA", (Image.new("L", (w, h), r),
                                       Image.new("L", (w, h), g),
                                       Image.new("L", (w, h), b), mk))
            bg = cls._paste_text(bg, txt)
            y += cls.ROW_H
        return cls._premultiply(bg)

    @staticmethod
    def _paste_text(bg, txt):
        """文字像素直接替换背景像素（RGB 不混合），边缘保持文字色"""
        b = np.array(bg)
        t = np.array(txt)
        mask = t[..., 3] > 0
        b[mask] = t[mask]
        return Image.fromarray(b, "RGBA")

    @staticmethod
    def _premultiply(img):
        """PIL 输出 straight alpha；Win32 分层窗口要求 premultiplied，
        统一预乘避免文字抗锯齿边缘出现黑/亮杂色块"""
        r, g, b, a = img.split()
        r = ImageChops.multiply(r, a)
        g = ImageChops.multiply(g, a)
        b = ImageChops.multiply(b, a)
        return Image.merge("RGBA", (r, g, b, a))

    @staticmethod
    def update(hwnd, img):
        w, h = img.size
        bmi = _BITMAPINFO()
        bmi.bmiHeader.biSize = ctypes.sizeof(_BITMAPINFOHEADER)
        bmi.bmiHeader.biWidth = w
        bmi.bmiHeader.biHeight = -h
        bmi.bmiHeader.biPlanes = 1
        bmi.bmiHeader.biBitCount = 32
        bmi.bmiHeader.biCompression = 0
        hdc = gdi32.CreateCompatibleDC(None)
        bits = ctypes.c_void_p()
        hbmp = gdi32.CreateDIBSection(hdc, ctypes.byref(bmi), 0, ctypes.byref(bits), None, 0)
        old = gdi32.SelectObject(hdc, hbmp)
        raw = img.tobytes("raw", "BGRa")
        ctypes.memmove(bits, raw, len(raw))
        screen_dc = user32.GetDC(None)
        pt_dst = _POINT(0, 0)
        pt_src = _POINT(0, 0)
        sz = _SIZE(w, h)
        blend = _BLENDFUNCTION(_AC_SRC_OVER, 0, 255, 1)
        ok = user32.UpdateLayeredWindow(hwnd, screen_dc, ctypes.byref(pt_dst),
                                        ctypes.byref(sz), hdc, ctypes.byref(pt_src),
                                        0, ctypes.byref(blend), _ULW_ALPHA)
        user32.ReleaseDC(None, screen_dc)
        gdi32.SelectObject(hdc, old)
        gdi32.DeleteObject(hbmp)
        gdi32.DeleteDC(hdc)
        return bool(ok)


# ---------- 悬浮窗配置 ----------
# 可选显示项：key -> (中文名, 行文本模板)
FLOAT_ITEMS = {
    "fan":    ("风扇转速", "CPU {cr} · GPU {gr} RPM"),
    "temp":   ("温度",     "CPU {ct}° · GPU {gt}°"),
    "boost":  ("强冷状态", "强冷{bs}"),
    "usage":  ("占用率",   "CPU {cu}% · GPU {gu}%"),
    "mem":    ("内存",     "内存 {mu} / {mt} GB ({mp}%)"),
    "net":    ("网络",     "↓ {nd} KB/s · ↑ {nu} KB/s"),
    "power":  ("功耗",     "CPU {cpw} W · GPU {gpw} W"),
    "memtemp": ("显存温度", "显存 {vt}°C"),
}

DEFAULT_FLOAT_CFG = {
    "fan": True,
    "temp": True,
    "boost": True,
    "usage": False,
    "mem": False,
    "net": False,
    "power": False,
    "memtemp": False,
    "alpha": 0,           # 背景卡不透明度 0-100（0=纯文字悬浮，文字始终清晰）
    "pos": None,          # 上次拖动位置 [x, y]
}

CFG_PATH = os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])), "gcu_float.json")


def load_float_cfg():
    d = dict(DEFAULT_FLOAT_CFG)
    try:
        with open(CFG_PATH, "r", encoding="utf-8") as f:
            d.update(json.load(f))
    except Exception:
        pass
    return d


def save_float_cfg(cfg):
    try:
        with open(CFG_PATH, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


class MonitorApp:
    def __init__(self, root):
        self.root = root
        self.latest = {}
        self.connected = False
        self.client = None
        self.boost_on = False
        self.lhm = LhmReader() if LHM_OK else None
        self.ec = EcReader()
        self.float_win = None      # 顶层文字窗口
        self.float_bg = None       # 底层背景窗口
        self._fg_dx = 1
        self._fg_dy = 1
        self.float_cfg = load_float_cfg()
        self._fl_rows = {}
        self._cfg_vars = {}
        self.gpu_sel = "d"   # "d"=独显 RTX 4060, "i"=核显 Radeon 780M
        self._build_ui()
        self._start_mqtt()
        self.root.after(1200, self._refresh)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

    # ---------- 界面 ----------
    def _card(self, parent, title, row, col, colspan=1, rowspan=1):
        f = tk.Frame(parent, bg=C_CARD, highlightbackground=C_BORDER,
                     highlightthickness=1, bd=0)
        f.grid(row=row, column=col, columnspan=colspan, rowspan=rowspan,
               sticky="nsew", padx=6, pady=6)
        tk.Label(f, text=title, bg=C_CARD, fg=C_DIM,
                 font=("Microsoft YaHei UI", 10)).pack(anchor="w", padx=12, pady=(8, 0))
        return f

    def _big(self, parent, text, fg=C_TITLE, size=34):
        lb = tk.Label(parent, text=text, bg=C_CARD, fg=fg,
                      font=("Consolas", size, "bold"))
        lb.pack(anchor="w", padx=12)
        return lb

    def _line(self, parent, text, fg=C_TEXT, size=11):
        lb = tk.Label(parent, text=text, bg=C_CARD, fg=fg,
                      font=("Microsoft YaHei UI", size))
        lb.pack(anchor="w", padx=12)
        return lb

    def _bar(self, parent, height=10):
        c = tk.Canvas(parent, width=180, height=height, bg=C_CARD,
                      highlightthickness=0)
        c.pack(anchor="w", padx=12, pady=(4, 10))
        c.create_rectangle(0, 0, 180, height, fill="#2a313d", outline="")
        r = c.create_rectangle(0, 0, 0, height, fill=C_ACCENT, outline="")
        return c, r

    def _build_ui(self):
        root = self.root
        root.title("机械革命翼龙15 Pro 实时监控")
        root.configure(bg=C_BG)
        root.geometry("760x640")
        root.minsize(680, 600)

        # 顶栏
        head = tk.Frame(root, bg=C_BG)
        head.pack(fill="x", padx=10, pady=(10, 2))
        tk.Label(head, text="机械革命 翼龙15 Pro  实时监控", bg=C_BG, fg=C_TITLE,
                 font=("Microsoft YaHei UI", 15, "bold")).pack(side="left")
        st = tk.Frame(head, bg=C_BG)
        st.pack(side="right")
        self.dot = tk.Canvas(st, width=12, height=12, bg=C_BG, highlightthickness=0)
        self.dot.pack(side="left", padx=(0, 5))
        self.dot.create_oval(1, 1, 11, 11, fill="#94a3b8", outline="")
        self.st_label = tk.Label(st, text="连接中…", bg=C_BG, fg=C_DIM,
                                 font=("Microsoft YaHei UI", 10))
        self.st_label.pack(side="left")

        # 主网格
        body = tk.Frame(root, bg=C_BG)
        body.pack(fill="both", expand=True, padx=4, pady=4)
        for i in range(4):
            body.grid_rowconfigure(i, weight=1)
        body.grid_columnconfigure(0, weight=1)
        body.grid_columnconfigure(1, weight=1)

        # --- 风扇区 ---
        fan_row = tk.Frame(body, bg=C_BG)
        fan_row.grid(row=0, column=0, columnspan=2, sticky="nsew")
        fan_row.grid_columnconfigure(0, weight=1)
        fan_row.grid_columnconfigure(1, weight=1)

        fc = self._card(fan_row, "CPU 风扇", 0, 0)
        self.fan_cpu_rpm = self._big(fc, "-- RPM", C_ACCENT)
        self.fan_cpu_bar, self.fan_cpu_bar_r = self._bar(fc)
        self.fan_cpu_pct = self._line(fc, "占空比 --%", fg=C_TEXT)

        fg2 = self._card(fan_row, "GPU 风扇", 0, 1)
        self.fan_gpu_rpm = self._big(fg2, "-- RPM", C_ACCENT)
        self.fan_gpu_bar, self.fan_gpu_bar_r = self._bar(fg2)
        self.fan_gpu_pct = self._line(fg2, "占空比 --%", fg=C_TEXT)

        # --- 强冷开关 ---
        bc = self._card(body, "风扇强冷 / 悬浮窗", 1, 0, colspan=2)
        row = tk.Frame(bc, bg=C_CARD)
        row.pack(fill="x", padx=12, pady=(6, 12))
        self.boost_btn = tk.Button(
            row, text="❄ 强冷 OFF", command=self._toggle_boost,
            bg="#2a313d", fg=C_DIM, activebackground="#164e63",
            activeforeground=C_TITLE, relief="flat", bd=0,
            font=("Microsoft YaHei UI", 13, "bold"), padx=18, pady=6,
            cursor="hand2")
        self.boost_btn.pack(side="left")
        self.boost_state = tk.Label(row, text="状态：--", bg=C_CARD, fg=C_DIM,
                                    font=("Microsoft YaHei UI", 11))
        self.boost_state.pack(side="left", padx=(14, 0))
        self.float_btn = tk.Button(
            row, text="⊞ 悬浮窗 关", command=self._toggle_float,
            bg="#2a313d", fg=C_DIM, activebackground="#164e63",
            activeforeground=C_TITLE, relief="flat", bd=0,
            font=("Microsoft YaHei UI", 12, "bold"), padx=14, pady=6,
            cursor="hand2")
        self.float_btn.pack(side="left", padx=(16, 0))
        self.float_cfg_btn = tk.Button(
            row, text="⚙ 设置", command=self._open_float_settings,
            bg="#2a313d", fg=C_DIM, activebackground="#164e63",
            activeforeground=C_TITLE, relief="flat", bd=0,
            font=("Microsoft YaHei UI", 10), padx=10, pady=6,
            cursor="hand2")
        self.float_cfg_btn.pack(side="left", padx=(8, 0))
        tk.Label(row, text="悬浮窗置顶 · 可拖动 · 双击关闭 · 项目可自定义",
                 bg=C_CARD, fg=C_DIM, font=("Microsoft YaHei UI", 9)).pack(side="right")

        # --- CPU / GPU ---
        cc = self._card(body, "CPU · Ryzen 7 8745H", 2, 0)
        self.cpu_temp = self._big(cc, "--°C", fg=C_OK)
        self.cpu_usage = self._line(cc, "占用率 --%")
        self.cpu_freq = self._line(cc, "频率 -- MHz")
        self.cpu_power = self._line(cc, "功耗 -- W")

        gc = tk.Frame(body, bg=C_CARD, highlightbackground=C_BORDER,
                      highlightthickness=1, bd=0)
        gc.grid(row=2, column=1, sticky="nsew", padx=6, pady=6)
        gtit = tk.Frame(gc, bg=C_CARD)
        gtit.pack(fill="x", padx=12, pady=(8, 0))
        self.gpu_title = tk.Label(gtit, text="GPU · RTX 4060 Laptop",
                                  bg=C_CARD, fg=C_DIM,
                                  font=("Microsoft YaHei UI", 10))
        self.gpu_title.pack(side="left")
        self.gpu_sel_btn = tk.Button(
            gtit, text="独显", command=self._toggle_gpu_sel,
            bg="#2a313d", fg=C_DIM, activebackground="#164e63",
            activeforeground=C_TITLE, relief="flat", bd=0,
            font=("Microsoft YaHei UI", 9, "bold"), padx=8, pady=1,
            cursor="hand2")
        self.gpu_sel_btn.pack(side="right")
        self.gpu_temp = self._big(gc, "--°C", fg=C_OK)
        self.gpu_usage = self._line(gc, "占用率 --%")
        self.gpu_freq = self._line(gc, "核心 -- MHz · 显存 -- MHz")
        self.gpu_mem = self._line(gc, "显存 -- MB · 温度 --°C")
        self.gpu_power = self._line(gc, "功耗 -- W")

        # --- 内存 / 网络 ---
        mc = self._card(body, "内存", 3, 0)
        self.mem_use = self._big(mc, "-- / -- GB", C_TITLE, size=22)
        self.mem_pct = self._line(mc, "使用率 --%")

        nc = self._card(body, "网络", 3, 1)
        self.net_down = self._big(nc, "↓ --", C_TITLE, size=22)
        self.net_up = self._line(nc, "↑ --")

        self.time_label = tk.Label(body, text="", bg=C_BG, fg=C_DIM,
                                   font=("Consolas", 9))
        self.time_label.grid(row=3, column=1, sticky="se", padx=10, pady=(0, 4))

    # ---------- MQTT ----------
    def _start_mqtt(self):
        def worker():
            try:
                c = mqtt.Client(client_id=CLIENT_ID, clean_session=True,
                                protocol=mqtt.MQTTv311, **_MQTT_KW)
                c.username_pw_set(USER, PWD)
                c.on_connect = self._on_connect
                c.on_disconnect = self._on_disconnect
                c.on_message = self._on_message
                c.connect(HOST, PORT, keepalive=15)
                self.client = c
                c.loop_forever()
            except Exception as e:
                logging.error("MQTT worker: %s", e)
                time.sleep(3)
                worker()

        threading.Thread(target=worker, daemon=True).start()

    def _on_connect(self, client, userdata, flags, reason_code, properties=None):
        rc = getattr(reason_code, "value", reason_code)
        if rc == 0:
            self.connected = True
            try:
                client.subscribe(SUB_TOPICS)
            except Exception as e:
                logging.error("subscribe: %s", e)
            logging.info("connected client=%s", CLIENT_ID)
        else:
            self.connected = False
            logging.error("connect rc=%r", rc)

    def _on_disconnect(self, client, userdata, flags, reason_code=None, properties=None):
        self.connected = False
        logging.info("disconnected rc=%r", reason_code)

    def _on_message(self, client, userdata, msg):
        try:
            if msg.topic == "Fan/Status":
                d = json.loads(msg.payload.decode("utf-8", "replace"))
                if isinstance(d, dict):
                    self.boost_on = (str(d.get("FanBoostEnable", "0")) == "1")
        except Exception:
            pass

    # ---------- 强冷开关 ----------
    def _toggle_boost(self):
        if not self.connected or self.client is None:
            return
        action = "FAN_BOOST_OFF" if self.boost_on else "FAN_BOOST_ON"
        try:
            self.client.publish("Fan/Control", json.dumps({"Action": action}))
            logging.info("publish Fan/Control %s", action)
        except Exception as e:
            logging.error("publish: %s", e)

    # ---------- 显卡切换 ----------
    def _toggle_gpu_sel(self):
        if self.gpu_sel == "d":
            self.gpu_sel = "i"
            self.gpu_sel_btn.config(text="核显", bg="#0e7490", fg=C_TITLE)
            self.gpu_title.config(text="GPU · Radeon 780M 核显")
        else:
            self.gpu_sel = "d"
            self.gpu_sel_btn.config(text="独显", bg="#2a313d", fg=C_DIM)
            self.gpu_title.config(text="GPU · RTX 4060 Laptop")

    # ---------- 悬浮窗（双层：底层半透明背景卡 + 顶层透明纯文字） ----------
    def _toggle_float(self):
        if self.float_win is None:
            self._create_float()
            self.float_btn.config(text="⊞ 悬浮窗 开", bg="#0e7490", fg=C_TITLE)
        else:
            self._destroy_float()
            self.float_btn.config(text="⊞ 悬浮窗 关", bg="#2a313d", fg=C_DIM)

    def _float_pos(self):
        w = self.float_bg if self.float_bg is not None else self.float_win
        if w is not None:
            try:
                return [w.winfo_x(), w.winfo_y()]
            except Exception:
                pass
        return None

    def _destroy_float(self):
        if self.float_win is not None or self.float_bg is not None:
            pos = self._float_pos()
            if pos:
                self.float_cfg["pos"] = pos
                save_float_cfg(self.float_cfg)
            for w in (self.float_bg, self.float_win):
                if w is not None:
                    try:
                        w.destroy()
                    except Exception:
                        pass
            self.float_bg = None
            self.float_win = None

    def _create_float(self):
        alpha = min(max(int(self.float_cfg.get("alpha", 0)), 0), 100)
        keys = [k for k in FLOAT_ITEMS if self.float_cfg.get(k, False)]
        if not keys:
            keys = ["fan"]
        _ph = {"cr": "0000", "gr": "0000", "ct": "00", "gt": "00", "bs": "开",
               "cu": "00", "gu": "00", "mu": "00", "mt": "00", "mp": "00",
               "nd": "0", "nu": "0", "cpw": "00", "gpw": "00", "vt": "00"}
        wpx = int(FloatPainter.measure(
            FLOAT_ITEMS[k][1].format(**_ph) for k in keys)) + 56
        hpx = len(keys) * FloatPainter.ROW_H + 14
        pos = self.float_cfg.get("pos")
        sw, sh = self.root.winfo_screenwidth(), self.root.winfo_screenheight()
        if pos and len(pos) == 2:
            x, y = int(pos[0]), int(pos[1])
            # 超屏保护：位置越界（含半截在屏外）则回到默认右上角
            if (x < -wpx + 40 or y < -hpx + 40 or x > sw - 40 or y > sh - 40):
                x = max(0, sw - wpx - 24)
                y = 56
        else:
            x = max(0, sw - wpx - 24)
            y = 56
        # 1) 顶层：文字窗口（透明背景，文字永远清晰）
        fg = tk.Toplevel(self.root)
        fg.title("GCU_FLOAT")
        fg.overrideredirect(True)
        fg.attributes("-topmost", True)
        fg.configure(bg=C_TRANS)
        try:
            fg.attributes("-transparentcolor", C_TRANS)
        except Exception:
            pass
        self._fl_rows = {}
        for i, k in enumerate(keys):
            lb = tk.Label(fg, text="", bg=C_TRANS,
                          fg=C_ACCENT if k == "fan" else C_TITLE,
                          font=("Consolas", 12, "bold"))
            lb.pack(anchor="w", padx=12, pady=(8 if i == 0 else 2, 2))
            self._fl_rows[k] = lb
        fg.geometry("%dx%d+%d+%d" % (wpx, hpx, x + 1, y + 1))
        # 2) 底层：半透明背景卡（-alpha 只作用于该层，无文字故无分层问题）
        bg = tk.Toplevel(self.root)
        bg.title("GCU_FLOAT_BG")
        bg.overrideredirect(True)
        bg.attributes("-topmost", True)
        bg.attributes("-alpha", alpha / 100.0)
        bg.configure(bg=C_BG)
        inner = tk.Frame(bg, bg=C_BG, highlightbackground=C_ACCENT,
                         highlightthickness=1, bd=0)
        inner.pack(fill="both", expand=True, padx=0, pady=0)
        bg.geometry("%dx%d+%d+%d" % (wpx + 2, hpx + 2, x - 1, y - 1))
        # 3) 拖动/双击绑定（两层都要）
        for w in ([fg] + list(self._fl_rows.values())):
            w.bind("<Button-1>", self._float_start)
            w.bind("<B1-Motion>", self._float_move)
            w.bind("<Double-Button-1>", lambda e: self._toggle_float())
        for w in (bg, inner):
            w.bind("<Button-1>", self._float_start)
            w.bind("<B1-Motion>", self._float_move)
            w.bind("<Double-Button-1>", lambda e: self._toggle_float())
        bg.lift()
        fg.lift()   # 文字层必须在背景卡之上
        fg.update()
        bg.update()
        self.float_win = fg
        self.float_bg = bg
        self._fg_dx, self._fg_dy = 1, 1
        self._paint_float()
        logging.info("float window opened at +%d+%d (alpha=%d)", x, y, alpha)

    def _float_start(self, e):
        self._fx, self._fy = e.x_root, e.y_root
        w = self.float_bg if self.float_bg is not None else self.float_win
        if w is not None:
            try:
                self._wx, self._wy = w.winfo_x(), w.winfo_y()
            except Exception:
                pass

    def _float_move(self, e):
        try:
            nx = self._wx + e.x_root - self._fx
            ny = self._wy + e.y_root - self._fy
            if self.float_bg is not None:
                self.float_bg.geometry("+%d+%d" % (nx, ny))
            if self.float_win is not None:
                self.float_win.geometry("+%d+%d" % (nx + self._fg_dx,
                                                    ny + self._fg_dy))
        except Exception:
            pass

    def _float_data(self):
        d = {}
        cr, cd, gr, gd = self._fan_now()
        d["cr"] = cr
        d["gr"] = gr
        d["ct"] = self._cpu_temp_now()
        if self.lhm is not None:
            gt = self.lhm.dgpu_temp() if self.gpu_sel == "d" else self.lhm.igpu_temp()
            d["gt"] = gt if gt is not None else "--"
        else:
            d["gt"] = "--"
        d["bs"] = "开" if self.boost_on else "关"
        d["cu"] = self._cpu_usage_now()
        if self.lhm is not None:
            gu = self.lhm.dgpu_load() if self.gpu_sel == "d" else self.lhm.igpu_load()
            d["gu"] = gu if gu is not None else "--"
        else:
            d["gu"] = "--"
        mu, mt, mp = self._mem_now()
        d["mu"], d["mt"], d["mp"] = mu, mt, mp
        nd, nu = self._net_now()
        d["nd"], d["nu"] = nd, nu
        cw = self.lhm.cpu_w() if self.lhm else None
        gw = self.lhm.gpu_w() if self.lhm else None
        d["cpw"] = cw if cw else "--"
        d["gpw"] = gw if gw else "--"
        vt = self.lhm.gpu_memtemp() if self.lhm else None
        d["vt"] = vt if vt is not None else "--"
        return d

    def _paint_float(self):
        if self.float_win is None or not self._fl_rows:
            return
        try:
            d = self._float_data()
            for k, lb in self._fl_rows.items():
                lb.config(text=FLOAT_ITEMS[k][1].format(**d))
            # 保持文字层在最上（防止点击背景卡后 z 序变化）
            try:
                self.float_win.lift()
            except Exception:
                pass
        except Exception as e:
            logging.error("paint float: %s", e)

    def _update_float(self):
        if self.float_win is not None:
            self._paint_float()

    # ---------- 悬浮窗设置 ----------
    def _open_float_settings(self):
        win = tk.Toplevel(self.root)
        win.title("悬浮窗设置")
        win.configure(bg=C_BG)
        win.geometry("320x480")
        win.resizable(False, False)
        win.transient(self.root)
        win.grab_set()

        def sync_cfg():
            for k, v in self._cfg_vars.items():
                self.float_cfg[k] = bool(v.get())

        def apply_alpha(val=None):
            try:
                a = int(float(val)) if val is not None else int(self._alpha_var.get())
            except Exception:
                a = int(self._alpha_var.get())
            self.float_cfg["alpha"] = a
            if self.float_bg is not None:
                try:
                    self.float_bg.attributes("-alpha", a / 100.0)
                except Exception:
                    pass

        def on_item_toggle():
            sync_cfg()
            if self.float_win is not None:
                self._rebuild_float()

        tk.Label(win, text="悬浮窗显示项目（勾选即生效）", bg=C_BG, fg=C_DIM,
                 font=("Microsoft YaHei UI", 10)).pack(anchor="w", padx=18, pady=(14, 4))
        self._cfg_vars = {}
        for k, (label, _) in FLOAT_ITEMS.items():
            v = tk.BooleanVar(value=bool(self.float_cfg.get(k, False)))
            cb = tk.Checkbutton(win, text=label, variable=v, bg=C_BG, fg=C_TEXT,
                                activebackground=C_BG, activeforeground=C_TITLE,
                                selectcolor="#1a1f2b", font=("Microsoft YaHei UI", 11),
                                cursor="hand2", command=on_item_toggle)
            cb.pack(anchor="w", padx=24, pady=2)
            self._cfg_vars[k] = v
        tk.Label(win, text="背景卡不透明度（0=纯文字，拖动即时生效，文字始终清晰）", bg=C_BG, fg=C_DIM,
                 font=("Microsoft YaHei UI", 10)).pack(anchor="w", padx=18, pady=(12, 0))
        self._alpha_var = tk.IntVar(value=int(self.float_cfg.get("alpha", 0)))
        sc = tk.Scale(win, from_=0, to=100, orient="horizontal",
                      variable=self._alpha_var, bg=C_BG, fg=C_TEXT,
                      troughcolor="#232a36", highlightthickness=0,
                      font=("Microsoft YaHei UI", 9),
                      command=apply_alpha)
        sc.pack(fill="x", padx=18, pady=2)

        def save():
            sync_cfg()
            self.float_cfg["alpha"] = int(self._alpha_var.get())
            save_float_cfg(self.float_cfg)
            win.destroy()

        tk.Button(win, text="保存设置", command=save, bg="#0e7490", fg=C_TITLE,
                  activebackground="#164e63", activeforeground=C_TITLE,
                  relief="flat", bd=0, font=("Microsoft YaHei UI", 11, "bold"),
                  padx=16, pady=6, cursor="hand2").pack(pady=(14, 2))
        tk.Label(win, text="勾选与滑块调整即时生效，保存仅写入配置文件",
                 bg=C_BG, fg=C_DIM, font=("Microsoft YaHei UI", 9)).pack()

    def _rebuild_float(self):
        pos = self._float_pos()
        self._destroy_float()
        self._create_float()
        if pos:
            try:
                if self.float_bg is not None:
                    self.float_bg.geometry("+%d+%d" % (pos[0], pos[1]))
                if self.float_win is not None:
                    self.float_win.geometry("+%d+%d" % (pos[0] + self._fg_dx,
                                                        pos[1] + self._fg_dy))
            except Exception:
                pass

    # ---------- 风扇数据（EC 直读） ----------
    def _fan_now(self):
        """返回 (cpu_rpm, cpu_duty%, gpu_rpm, gpu_duty%)；EC 直读。"""
        if self.ec is not None and self.ec.ok:
            cr, cd = self.ec.main_rpm, self.ec.main_duty
            gr, gd = self.ec.sec_rpm, self.ec.sec_duty
            if None not in (cr, cd, gr, gd):
                return cr, round(cd / 2.0, 1), gr, round(gd / 2.0, 1)
        return "--", "--", "--", "--"

    # ---------- 独立数据源（EC + LHM + psutil，无官方控制台依赖） ----------
    def _cpu_temp_now(self):
        """CPU 温度：EC 温控点直读"""
        if self.ec is not None and self.ec.ok and self.ec.cpu_temp:
            return self.ec.cpu_temp
        return "--"

    def _cpu_usage_now(self):
        """CPU 占用%：psutil"""
        if psutil is not None:
            try:
                return int(psutil.cpu_percent(interval=None))
            except Exception:
                pass
        return "--"

    def _cpu_freq_now(self):
        """CPU 当前频率 MHz：psutil"""
        if psutil is not None:
            try:
                f = psutil.cpu_freq()
                if f is not None and f.current:
                    return int(f.current)
            except Exception:
                pass
        return "--"

    def _mem_now(self):
        """(used_GB, total_GB, pct)：psutil"""
        if psutil is not None:
            try:
                vm = psutil.virtual_memory()
                return (round(vm.used / 1024**3, 1),
                        round(vm.total / 1024**3, 1), int(vm.percent))
            except Exception:
                pass
        return "--", "--", "--"

    def _net_now(self):
        """(down_kbps, up_kbps)：psutil 差值"""
        if psutil is not None:
            try:
                now = psutil.net_io_counters()
                t = time.time()
                if getattr(self, "_net_last", None) is not None:
                    last, lt = self._net_last
                    dt = max(t - lt, 0.001)
                    dn = max(now.bytes_recv - last.bytes_recv, 0) / dt / 1000
                    up = max(now.bytes_sent - last.bytes_sent, 0) / dt / 1000
                    self._net_last = (now, t)
                    return int(dn), int(up)
                self._net_last = (now, t)
            except Exception:
                pass
        return "--", "--"

    def _gpu_now(self):
        """独显数据 (temp, load, core_mhz, mem_mhz, mem_mb)：LHM"""
        if self.lhm is not None:
            t = self.lhm.dgpu_temp()
            u = self.lhm.dgpu_load()
            cf = self.lhm.dgpu_freq_core()
            mf = self.lhm.dgpu_freq_mem()
            mm = self.lhm.dgpu_mem_used()
            if t is not None or u is not None:
                return (t if t is not None else "--",
                        u if u is not None else "--",
                        cf if cf is not None else "--",
                        mf if mf is not None else "--",
                        mm if mm is not None else "--")
        return "--", "--", "--", "--", "--"

    # ---------- 刷新 ----------
    def _set_bar(self, canvas, rect, pct):
        try:
            p = min(max(float(pct), 0.0), 100.0)
        except Exception:
            p = 0.0
        canvas.coords(rect, 0, 0, 180 * p / 100.0, 10)

    def _refresh(self):
        try:
            now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            self.time_label.config(text=now)

            if self.connected:
                self.dot.itemconfig(1, fill=C_OK)
                self.st_label.config(text="独立直读 · 强冷已连 GCU", fg=C_OK)
            else:
                self.dot.itemconfig(1, fill=C_WARN)
                self.st_label.config(text="独立直读 · 强冷离线", fg=C_WARN)

            # 强冷按钮状态
            if self.boost_on:
                self.boost_btn.config(text="❄ 强冷 ON", bg="#0e7490", fg=C_TITLE)
                self.boost_state.config(text="状态：已开启（风扇满转）", fg=C_ACCENT)
            else:
                self.boost_btn.config(text="❄ 强冷 OFF", bg="#2a313d", fg=C_DIM)
                self.boost_state.config(text="状态：关闭", fg=C_DIM)

            cr, cd, gr, gd = self._fan_now()
            self.fan_cpu_rpm.config(text="%s RPM" % cr)
            self.fan_cpu_pct.config(text="占空比 %s%%" % cd)
            self._set_bar(self.fan_cpu_bar, self.fan_cpu_bar_r, cd if cd != "--" else 0)
            self.fan_gpu_rpm.config(text="%s RPM" % gr)
            self.fan_gpu_pct.config(text="占空比 %s%%" % gd)
            self._set_bar(self.fan_gpu_bar, self.fan_gpu_bar_r, gd if gd != "--" else 0)

            t = self._cpu_temp_now()
            u = self._cpu_usage_now()
            f = self._cpu_freq_now()
            try:
                tc = float(t)
                tcol = C_OK if tc < 80 else (C_WARN if tc < 95 else C_ERR)
            except Exception:
                tcol = C_TITLE
            self.cpu_temp.config(text="%s°C" % t, fg=tcol)
            self.cpu_usage.config(text="占用率 %s%%" % u)
            self.cpu_freq.config(text="频率 %s MHz" % f)

            if self.gpu_sel == "i" and self.lhm is not None:
                # 核显模式：数据全部来自 LHM
                t = self.lhm.igpu_temp()
                u = self.lhm.igpu_load()
                cf = self.lhm.igpu_freq_core()
                mf = self.lhm.igpu_freq_mem()
                mm = self.lhm.igpu_mem_used()
                pw = self.lhm.igpu_w()
                tcol = C_TITLE
                if t is not None:
                    tcol = C_OK if t < 80 else (C_WARN if t < 90 else C_ERR)
                self.gpu_temp.config(text="%s°C" % (t if t is not None else "--"), fg=tcol)
                self.gpu_usage.config(text="占用率 %s%%" % (u if u is not None else "--"))
                self.gpu_freq.config(text="核心 %s MHz · 显存 %s MHz" % (
                    cf if cf is not None else "--", mf if mf is not None else "--"))
                self.gpu_mem.config(text="显存 %s MB · 温度 %s°C" % (
                    mm if mm is not None else "--", t if t is not None else "--"))
                self.gpu_power.config(text="功耗 %s W" % (pw if pw is not None else "--"))
            else:
                t, u, cf, mf, mm = self._gpu_now()
                try:
                    tg = float(t)
                    gcol = C_OK if tg < 80 else (C_WARN if tg < 90 else C_ERR)
                except Exception:
                    gcol = C_TITLE
                self.gpu_temp.config(text="%s°C" % t, fg=gcol)
                self.gpu_usage.config(text="占用率 %s%%" % u)
                self.gpu_freq.config(text="核心 %s MHz · 显存 %s MHz" % (cf, mf))
                vt = self.lhm.gpu_memtemp() if self.lhm else None
                self.gpu_mem.config(text="显存 %s MB · 温度 %s°C" % (
                    mm, vt if vt is not None else "--"))
                if self.gpu_power is not None:
                    pass  # 功耗在下方统一更新

            self._update_float()

            mu, mt, mp = self._mem_now()
            self.mem_use.config(text="%s / %s GB" % (mu, mt))
            self.mem_pct.config(text="使用率 %s%%" % mp)

            nd, nu = self._net_now()
            self.net_down.config(text="↓ %s KB/s" % nd)
            self.net_up.config(text="↑ %s KB/s" % nu)

            # 功耗（LHM）
            if self.lhm is not None:
                cw = self.lhm.cpu_w()
                self.cpu_power.config(text="功耗 %s W" % (cw if cw else "--"))
                if self.gpu_sel == "i":
                    pass  # GPU 功耗已在核显分支设置
                else:
                    gw = self.lhm.gpu_w()
                    self.gpu_power.config(text="功耗 %s W" % (gw if gw is not None else "--"))
            else:
                self.cpu_power.config(text="功耗 -- W")
                self.gpu_power.config(text="功耗 -- W")
        except Exception as e:
            logging.error("refresh: %s", e)
        self.root.after(REFRESH_MS, self._refresh)

    def _on_close(self):
        logging.info("exit")
        self._destroy_float()
        if self.ec is not None:
            self.ec.stop()
        if self.lhm is not None:
            self.lhm.stop()
        self.root.destroy()


def main():
    root = tk.Tk()
    try:
        root.iconbitmap(os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])),
                                     "gcu.ico"))
    except Exception:
        pass
    MonitorApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
