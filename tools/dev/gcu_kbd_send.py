# -*- coding: utf-8 -*-
"""
GCU 键盘灯 —— 反向发送测试工具（验证 30 系 GCU 认哪种 payload）
================================================================
用途：把「官方抓到的 payload」和「LM 现有 payload」的字段差异逐个删/换，
      看键盘灯实际反应，从而确定 30 系 GCU 到底需要哪些字段。

安全：只发 keyboard 灯效指令，不改任何配置；用 UWPClient_2，不影响官方控制台。

用法：
  py -3 gcu_kbd_send.py --list          # 看有哪些测试
  py -3 gcu_kbd_send.py B               # 跑测试 B
  py -3 gcu_kbd_send.py B --light 3     # 指定亮度
  py -3 gcu_kbd_send.py off             # 用 SetPower 关灯（收尾用）
  py -3 gcu_kbd_send.py on              # 用 SetPower 开灯

测试定义（对照 diagnostics\\30系键盘灯GCU协议_抓包分析.md 第五节）：
  A  官方 30 系原包         MonochromeIndex=2（预设色板）+ ColorBuffer 带 ID
  B  40 系风格 + 30 系取值  MonochromeIndex=33 + ColorBuffer 纯蓝 带 ID   <-- 本次
  C  B 删掉 ID              MonochromeIndex=33 + ColorBuffer 纯蓝 无 ID
  D  官方形状删索引         MonochromeIndex 整个不发 + ColorBuffer 带 ID
"""
import sys, io, json, time, datetime, threading

HOST, PORT = "127.0.0.1", 13688
TOPIC = "Keyboard/Ctrl"

# 官方控制台自己在用 UWPClient_1；_2 实测可用且互不干扰
CID, USER, PWD = "UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"


def now():
    return datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]


def cb(ci, r, g, b, with_id=True):
    d = {"R": r, "G": g, "B": b}
    if with_id:
        return {"ID": ci, **d}
    return d


def build(variant, light=3, speed=2):
    """返回 (payload_dict, 说明字符串)"""
    v = variant.upper()
    base = {
        "function": "SetEffectALL",
        "mode": "Lighting",
        "effect": "Single",
        "light": str(light),
        "speed": str(speed),
        # 官方 30 系：direction / nv_save 都是 null
        "direction": None,
        "nv_save": None,
        "color": {"isCircular": True},
        "MqttID": None,
        "backgroundcolor": 0,
        "alphabet": None,
        "solution": 0,
        "level": 0,
        "type": 0,
        # 官方 30 系 SetEffectALL 的 powerstatus 就是 0（≠40 系的 1）
        "powerstatus": 0,
        "ManualIndex1": "1", "ManualIndex2": "5", "ManualIndex3": "9",
        "ManualIndex4": "13", "ManualIndex5": "21", "ManualIndex6": "25",
        "ManualInterval": "10",
        "BreathingIndex": "1",
        "DefaultData": "",
        "AuroraSwitch": False,
    }

    if v == "A":
        # 官方 30 系原包：预设色板索引 2 + ColorBuffer 带 ID（官方那 7 段红是残留值，不参与）
        base["MonochromeIndex"] = "2"
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 255, 0, 0, True) for i in range(7)]
        return base, "A 官方原包：MonochromeIndex=2（预设色板第2色）+ ID 齐全"

    if v == "B":
        # 本次测试：自定义色模式 —— MonochromeIndex=33 + ColorBuffer 纯蓝 + ID
        base["MonochromeIndex"] = "33"
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 0, 0, 255, True) for i in range(7)]
        return base, "B 40系风格(33)+自定义纯蓝 + ID 齐全  <== 本次测试"

    if v == "C":
        # B 去掉 ColorBuffer 的 ID —— 验证「无 ID 整条不生效」在 30 系是否成立
        base["MonochromeIndex"] = "33"
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 0, 0, 255, False) for i in range(7)]
        return base, "C = B 但删掉 ID"

    if v == "D":
        # 官方形状但不发 MonochromeIndex —— = LM 通用包现状
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 255, 0, 0, True) for i in range(7)]
        return base, "D 官方形状但删掉 MonochromeIndex（≈LM 通用包）"

    if v == "E":
        # 模拟"打完补丁后 LM 实际会发的包"：nv_save/direction 用 LM 的老值，
        # MonochromeIndex 故意用 7（≠当前 33），靠状态变化客观判断 GCU 认不认
        base["nv_save"] = "SAVE"
        base["direction"] = "None"
        base["MonochromeIndex"] = "7"
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 255, 0, 0, True) for i in range(7)]
        return base, "E 补丁后 LM 风格（nv_save=SAVE, direction=None, MonochromeIndex=7, 带 ID）"

    if v == "F":
        # 补丁后 LM 实际发出的包（逐字段一致）：nv_save=SAVE / direction=None /
        # MonochromeIndex=33 / 自定义色 7 段带 ID。用于验证"会不会把灯弄灭"。
        base["nv_save"] = "SAVE"
        base["direction"] = "None"
        base["MonochromeIndex"] = "33"
        base["color"]["ColorBlocks"] = 7
        base["color"]["ColorBuffer"] = [cb(i, 0, 0, 255, True) for i in range(7)]
        return base, "F = 补丁后 LM 实际包（SAVE/None/33/自定义蓝+ID）"

    raise SystemExit("未知测试: %s（可用 A/B/C/D/E/F）" % variant)


VARIANTS = {
    "A": "官方 30 系原包：MonochromeIndex=2（预设色板）+ ColorBuffer 带 ID",
    "B": "40 系风格：MonochromeIndex=33 + 自定义纯蓝 + ColorBuffer 带 ID  <== 本次",
    "C": "= B 但 ColorBuffer 删掉 ID（验证「无 ID 整条不生效」是否成立）",
    "D": "官方形状但删掉 MonochromeIndex（≈ LM 通用包现状）",
    "E": "补丁后 LM 风格：nv_save=SAVE / direction=None / MonochromeIndex=7 / 带 ID（客观测是否被接受）",
}


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    import paho.mqtt.client as mqtt

    argv = sys.argv[1:]
    if not argv or "--list" in argv:
        print("可用测试：")
        for k, v in VARIANTS.items():
            print("  %s  %s" % (k, v))
        print("\n另: on / off = 用 SetPower 开灯/关灯")
        return

    if "--query" in argv:
        # 只查当前键盘灯状态（发 GETSTATUS 读 Keyboard/Status）
        st = {"last": None}
        lock2 = threading.Lock()

        def on_c2(cl, ud, flags, rc, properties=None):
            cl.subscribe("Keyboard/#", qos=0)

        def on_m2(cl, ud, m):
            if m.topic == "Keyboard/Status":
                with lock2:
                    st["last"] = m.payload.decode("utf-8", "replace")

        c2 = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
        c2.username_pw_set(USER, PWD)
        c2.on_connect = on_c2
        c2.on_message = on_m2
        c2.connect(HOST, PORT, keepalive=30)
        c2.loop_start()
        time.sleep(1.5)
        c2.publish(TOPIC, json.dumps({"Action": "GETSTATUS"}), qos=0)
        time.sleep(3)
        c2.loop_stop()
        try:
            c2.disconnect()
        except Exception:
            pass
        body = st["last"]
        if not body:
            print("[查询] 未收到 Keyboard/Status")
            return
        try:
            j = json.loads(body, object_pairs_hook=lambda p: dict(p))
        except Exception:
            print("[查询] 原始:", body[:400])
            return
        print("[当前键盘灯状态]")
        for k in ("solution", "type", "powerStatus", "brightNess",
                  "MonochromeIndex", "ManualInterval", "BreathingIndex", "effect"):
            print("  %-18s %s" % (k, j.get(k)))
        print("  (原始 MonochromeIndex = %s)" % j.get("MonochromeIndex"))
        return

    arg = argv[0].lower()
    light = 3
    if "--light" in argv:
        try:
            light = int(argv[argv.index("--light") + 1])
        except Exception:
            pass

    got = {"resp": []}
    lock = threading.Lock()

    def on_connect(cl, ud, flags, rc, properties=None):
        code = getattr(rc, "value", rc)
        print("[连接] rc=%s" % code, flush=True)
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        body = m.payload.decode("utf-8", "replace")
        with lock:
            got["resp"].append((now(), m.topic, body))
        # 只打印键盘相关的回包
        if "keyboard" in m.topic.lower() or "Keyboard" in m.topic:
            print("  <<< %s %s" % (m.topic, body[:150]), flush=True)

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect(HOST, PORT, keepalive=30)
    c.loop_start()
    time.sleep(1.5)

    if arg in ("on", "off"):
        payload = {"function": "SetPower", "powerstatus": (1 if arg == "on" else 0)}
        desc = "SetPower %s" % arg
    else:
        payload, desc = build(arg, light=light)

    body = json.dumps(payload, ensure_ascii=False)
    print("\n=== 发送: %s ===" % desc)
    print(body)
    info = c.publish(TOPIC, body, qos=0)
    try:
        info.wait_for_publish(timeout=5)
    except Exception:
        pass
    print("[已发送] topic=%s rc=%s" % (TOPIC, info.rc))

    print("\n监听 6 秒，看 GCU 有没有回包…", flush=True)
    time.sleep(6)

    kbd = [x for x in got["resp"] if "keyboard" in x[1].lower()]
    print("\n--- 6 秒内键盘相关回包 %d 条 ---" % len(kbd))
    for t, topic, b in kbd:
        print("%s %s %s" % (t, topic, b[:300]))
    print("--- 全部回包 topic 数: %d ---" % len(got["resp"]))

    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass
    print("\n[完成] 请看键盘灯现在是什么颜色。")
    print("       蓝 = 30系认 33+自定义色那套（改动最小）")
    print("       没变化 = 33 那套不认，需要走预设索引")


if __name__ == "__main__":
    main()
