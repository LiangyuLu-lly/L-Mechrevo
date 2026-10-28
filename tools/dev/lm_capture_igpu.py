# -*- coding: utf-8 -*-
r"""
LM 冷切换核显 —— 动作采集脚本（给外部机型用，尤其 50 系）
================================================================================
为什么不用抓包：
  GCU 的 MQTT broker 对 client_id 有白名单，只接受 `UWPClient_2`（LM 自己占用）。
  其它 id 一律 "Client identifier not valid" -> 被动抓包在本机/外部机型都**做不到** ✅
因此本脚本采用**唯一可行且更可靠**的办法：
  **让 LM 自己把发出的报文写进日志（log.txt），再把关键行回收成报告** ✅

用法（把本文件拷到目标机器，需要 Python 3 与 paho-mqtt 仅在 probe 模式需要）：

  1) 开启全量日志（写用户环境变量，需重启 LM 生效）
       py -3 lm_capture_igpu.py enable

  2) 按提示操作：重启 LM -> 点「核显模式」（冷切换）-> 若提示重启就重启
     （日志在磁盘上，重启不会丢 ✅）

  3) 回收并生成报告
       py -3 lm_capture_igpu.py collect
     -> 在桌面生成 LM-IGPU-CAPTURE-<机型>-<时间>.txt，把它发回来即可 ✅

  可选）直接读一次 GCU 当前状态（⚠️ 会短暂把 LM 挤下线，几秒后自动重连）
       py -3 lm_capture_igpu.py probe
"""
import io
import os
import re
import sys
import json
import glob
import datetime
import subprocess

APP = "MechrevoLite"
ENV_LEVEL = "LMECHREVO_LOG_LEVEL"
ENV_DIAG = "LMECHREVO_DIAGNOSTIC_PACK_PATH"   # 若曾被设死，诊断包名字会怪，这里顺手提示

PATTERNS = [
    ("切模式（LM 实际发出的动作）", re.compile(r'SwitchGpuMode\(\d+\)\s*->|MechrevoService\.SwitchGpuMode')),
    ("冷切换载荷序列",            re.compile(r'GPU restart route applied')),
    ("模式回读",                  re.compile(r'GpuMode\s*->')),
    ("路由 / NVRAM 待生效",       re.compile(r'GPU route (at startup|readback|verify)')),
    ("能力位",                    re.compile(r'TrayGPU:|CanOfferIgpuOnly|IgpuOnlyConnectionSwitch|DiscreteGpuDirectConnectionSwitch')),
    ("核显省电",                  re.compile(r'SetIGpuOnlySaveMode|IgpuSaving')),
    ("机型画像",                  re.compile(r'Device capabilities:|HardwareInfo:')),
]


def log_path():
    appdata = os.environ.get("APPDATA", "")
    return os.path.join(appdata, APP, "log.txt")


def config_path():
    appdata = os.environ.get("APPDATA", "")
    return os.path.join(appdata, APP, "config.json")


def read(p, limit=None):
    try:
        with io.open(p, encoding="utf-8-sig", errors="replace") as f:
            return f.read() if limit is None else f.read(limit)
    except OSError:
        return ""


def cmd_enable():
    print("=" * 74)
    print("步骤 1/3：开启全量日志（让 LM 把发出的报文写进磁盘 log.txt）")
    print("=" * 74)
    try:
        subprocess.run(["setx", ENV_LEVEL, "All"], check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("  已执行: setx %s All" % ENV_LEVEL)
    except Exception as ex:
        print("  自动设置失败(%r)，请手动：系统属性 -> 环境变量 -> 新建 %s = All" % (ex, ENV_LEVEL))
    if os.environ.get(ENV_DIAG):
        print("  ⚠️ 检测到 %s = %s" % (ENV_DIAG, os.environ[ENV_DIAG]))
        print("     它会让「导出诊断包」写死到该文件名；若不需要请删除该变量")
    print()
    print("  然后请：")
    print("    1) 完全退出 L-Mechrevo（托盘 -> 退出），再重新启动它")
    print("    2) 打开「设置」页，点一下【核显模式】按钮（这就是冷切换动作）")
    print("    3) 若提示需要重启，就重启 Windows（日志在磁盘上，不会丢）")
    print("    4) 重启后回到本脚本执行：  py -3 lm_capture_igpu.py collect")
    print()
    return 0


def cmd_collect():
    lp = log_path()
    cp = config_path()
    if not os.path.isfile(lp):
        print("!! 找不到日志文件：%s" % lp)
        print("   说明日志级别仍是 Off（未生效）。请先执行 enable 并**重启 LM**。")
        print("   提示：环境变量修改只对新启动的进程生效。")
        return 1

    lines = read(lp).split("\n")
    print("日志: %s  (%.1f MB, %d 行)" % (lp, os.path.getsize(lp) / 1048576.0, len(lines)))

    # 机型指纹
    model = bios = cpu = gpu = ""
    for l in lines:
        if not model and "Device capabilities:" in l:
            m = re.search(r'model=([^,]+).*?bios=([^,]*)', l)
            if m:
                model, bios = m.group(1).strip(), m.group(2).strip()
        if not cpu and "ProcessorInfo" in l:
            m = re.search(r'"ProcessorInfo":"([^"]+)"', l)
            if m:
                cpu = m.group(1)
        if not gpu and re.search(r'NVIDIA|GeForce', l):
            m = re.search(r'(NVIDIA[^",\\]{4,60})', l)
            if m:
                gpu = m.group(1).strip()

    buckets = {name: [] for name, _ in PATTERNS}
    for l in lines:
        for name, pat in PATTERNS:
            if pat.search(l):
                buckets[name].append(l.strip())
                break

    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    safe_model = re.sub(r'[^A-Za-z0-9]+', '-', model) or "unknown"
    out = os.path.join(os.path.expanduser("~"), "Desktop",
                       "LM-IGPU-CAPTURE-%s-%s.txt" % (safe_model, ts))
    if not os.path.isdir(os.path.dirname(out)):
        out = os.path.join(os.getcwd(), os.path.basename(out))

    cfg = {}
    try:
        cfg = json.loads(read(cp))
    except Exception:
        pass

    with io.open(out, "w", encoding="utf-8", newline="\n") as f:
        w = f.write
        w("L-Mechrevo 冷切换核显 —— 动作采集报告\n")
        w("=" * 74 + "\n")
        w("生成时间 : %s\n" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
        w("机型     : %s\n" % (model or "(未解析)"))
        w("BIOS     : %s\n" % (bios or "(未解析)"))
        w("CPU      : %s\n" % (cpu or "(未解析)"))
        w("GPU      : %s\n" % (gpu or "(未解析)"))
        for k in ("dgpu_generation", "dgpu_generation_source", "gpu_mode",
                  "gpu_restart_pending", "gpu_restart_target", "log_level"):
            if k in cfg:
                w("%-9s: %s\n" % (k[:9], cfg[k]))
        w("环境变量 : %s=%s\n" % (ENV_LEVEL, os.environ.get(ENV_LEVEL, "(未设置)")))
        w("日志大小 : %.1f MB / %d 行\n" % (os.path.getsize(lp) / 1048576.0, len(lines)))
        w("\n")

        for name, _pat in PATTERNS:
            items = buckets[name]
            w("=" * 74 + "\n")
            w("## %s   (%d 行)\n" % (name, len(items)))
            w("=" * 74 + "\n")
            if not items:
                w("   （无）\n")
            else:
                # 相同前缀去重：只保留动作相关与首末样本，避免全量 TrayGPU 刷屏
                if name == "能力位":
                    keep = items[:6] + items[-6:]
                    w("   ... 共 %d 行，以下为首尾样本 ...\n" % len(items))
                elif len(items) > 60:
                    keep = items[:40] + ["   ... 省略 %d 行 ..." % (len(items) - 50)] + items[-10:]
                else:
                    keep = items
                for l in keep:
                    w("   " + l[:300] + "\n")
            w("\n")

        # 重点：把「切到核显(0)」的动作单独拎出来
        w("=" * 74 + "\n")
        w("## ★ 重点：切到核显模式(mode 0) 的动作\n")
        w("=" * 74 + "\n")
        focus = [l.strip() for l in lines
                 if re.search(r'SwitchGpuMode\(0\)|target=0|GpuMode -> 0 |TOGGLE_IGPU|IGPU_ONLY_CONNECT_RB_ON', l)]
        if focus:
            for l in focus[-40:]:
                w("   " + l[:300] + "\n")
        else:
            w("   （未发现切核显记录 —— 请确认已按提示点过「核显模式」）\n")

    print()
    print("=" * 74)
    print("报告已生成：")
    print("   " + out)
    print("=" * 74)
    tail = buckets["切模式（LM 实际发出的动作）"]
    print("\n快速预览（LM 实际发出的动作，最后 8 条）：")
    for l in tail[-8:]:
        print("   " + l[:220])
    if not tail:
        print("   （无 —— 说明日志级别可能仍未生效，或没点过模式切换）")
    print("\n请把上面那个报告文件发回来即可 ✅")
    return 0


def cmd_probe():
    import time
    try:
        import paho.mqtt.client as mqtt
    except ImportError:
        print("probe 模式需要 paho-mqtt：pip install paho-mqtt")
        return 1
    print("⚠️ 读取 GCU 状态会短暂使用 LM 的 client_id（UWPClient_2），LM 会掉线几秒后自动重连。")
    out = {}
    KEYS = ("IGpuOnlyConnectionSwitch_Support", "IGpuOnlyConnectionSwitch_Status",
            "DiscreteGpuDirectConnectionSwitch_Support", "DiscreteGpuDirectConnectionSwitch_Status",
            "IGpuAutoSupportSwitch")

    def on_connect(c, u, f, rc, p=None):
        c.subscribe("#", qos=0)

    def on_message(c, u, m):
        body = m.payload.decode("utf-8", "replace")
        try:
            obj = json.loads(body)
        except Exception:
            return
        for k in KEYS:
            if k in obj:
                out[k] = obj[k]

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="UWPClient_2")
    c.username_pw_set("UWPClient_User_2", "UWPClient_Pwd888881772688_2")
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect("127.0.0.1", 13688, keepalive=20)
    c.loop_start()
    time.sleep(1.5)
    c.publish("Setting/Control", json.dumps({"Action": "GETSTATUS"}), qos=0)
    time.sleep(2.5)
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass
    print("\nGCU 当前上报：")
    for k in KEYS:
        print("   %-44s = %r" % (k, out.get(k, "(未收到)")))
    return 0


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    arg = (sys.argv[1] if len(sys.argv) > 1 else "").lower()
    if arg == "enable":
        return cmd_enable()
    if arg == "collect":
        return cmd_collect()
    if arg == "probe":
        return cmd_probe()
    print(__doc__)
    return 0


if __name__ == "__main__":
    sys.exit(main())
