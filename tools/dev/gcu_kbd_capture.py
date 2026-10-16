# -*- coding: utf-8 -*-
"""
GCU 键盘灯协议抓包（被动监听版）
================================================================
用途：抓「官方控制台操作键盘灯光」时，GCU MQTT 上到底发了什么命令。
      这是主线问题的决定性实验：
        - 若 Keyboard/Ctrl 上出现官方下发的灯效命令  -> 修法 B 可行（LM 走 GCU 灯效通道）
        - 若键盘灯操作期间 Keyboard/Ctrl 毫无动静    -> 官方控制台是直接走 HID(VID 3554)，
                                                        MQTT 抓包无解，需改做 HID 层抓包

与旧脚本（patches/gcu_capture_standalone.py）的区别：
  1. 默认【纯被动】，不轮询 GETSTATUS —— 否则每 8 秒的 GETSTATUS 回包会淹没官方命令，
     无法反推协议。（要拉能力位用 --poll）
  2. 毫秒级时间戳 + 方向标注（<<< GCU 上报 / >>> 本脚本发送）
  3. 结束后额外产出「键盘相关子集」文件，直接拿来看

用法（真实 Python 在 Python312，PATH 上的 python 是空壳）：
  py -3 D:\\openLM\\tools\\gcu_kbd_capture.py                # 默认抓 240 秒
  py -3 D:\\openLM\\tools\\gcu_kbd_capture.py 300            # 抓 300 秒
  py -3 D:\\openLM\\tools\\gcu_kbd_capture.py 300 --poll      # 附带周期性 GETSTATUS
  py -3 D:\\openLM\\tools\\gcu_kbd_capture.py --probe         # 只测凭据，不抓包

建议操作流程：先启动脚本，等它打印 connected，
再打开官方控制台，然后【一次只做一个动作，间隔 5 秒】：
  1. 打开键盘灯效页（观察首次进入时的握手）
  2. 切换灯效模式（如 呼吸 -> 常亮）
  3. 调亮度 + / -
  4. 改颜色（选一个单色）
  5. 改速度 / 方向
  6. 关闭键盘灯，再打开
  7. 回主界面看键盘灯电源开关
"""
import sys, os, io, json, time, datetime, threading

BROKER_HOST = "127.0.0.1"
BROKER_PORT = 13688

# 官方 broker 校验 client_id（LM 自带的 UWPClient_4 会被拒），
# 这里按「尽量不踢掉官方控制台」的顺序试：_2/_3 优先，_1 最后（_1 是官方控制台自己在用的）
CREDENTIALS = [
    ("UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"),
    ("UWPClient_3", "UWPClient_User_3", "UWPClient_Pwd888881772688_3"),
    ("GCUClient",   "GCUClient_User",   "GCUClient_Pwd888881772688"),
    ("UWPClient_1", "UWPClient_User_1", "UWPClient_Pwd888881772688_1"),
]

OUT_DIR = r"D:\openLM\diagnostics"
KBD_TOPIC_HINTS = ("keyboard/", "hidlightbar/")
KBD_KEY_HINTS = ("effect", "light", "bright", "color", "colour", "speed",
                 "direction", "powerstatus", "breathing", "manual", "rgb",
                 "rainbow", "flow", "static", "index", "colorbuffer", "defaultdata")


def out_dir():
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return OUT_DIR if os.path.isdir(OUT_DIR) else os.path.dirname(os.path.abspath(__file__))


def now_ms():
    return datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]


def stamp():
    return datetime.datetime.now().strftime("%Y%m%d_%H%M%S")


def build_client(mqtt, cid, user, pwd, on_connect, on_message, on_disconnect):
    try:
        c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=cid)
    except AttributeError:                      # paho 1.x
        c = mqtt.Client(client_id=cid)
    c.username_pw_set(user, pwd)
    c.on_connect = on_connect
    c.on_message = on_message
    c.on_disconnect = on_disconnect
    return c


def probe_only():
    """只测哪组凭据能连上，不订阅不抓包。"""
    import paho.mqtt.client as mqtt
    print("== 凭据探测 %s:%d ==" % (BROKER_HOST, BROKER_PORT))
    got = []
    for cid, user, pwd in CREDENTIALS:
        result = {"rc": None, "err": None}
        done = threading.Event()

        def on_connect(cl, ud, flags, rc, properties=None):
            result["rc"] = getattr(rc, "value", rc)
            done.set()

        c = build_client(mqtt, cid, user, pwd, on_connect, lambda *a: None, lambda *a: None)
        try:
            c.connect(BROKER_HOST, BROKER_PORT, keepalive=15)
            c.loop_start()
            done.wait(4)
            c.loop_stop()
            try:
                c.disconnect()
            except Exception:
                pass
        except Exception as e:
            result["err"] = repr(e)
        ok = (result["rc"] == 0)
        print("  [%s] %-12s rc=%s %s" % ("OK " if ok else ".. ", cid, result["rc"], result["err"] or ""))
        if ok:
            got.append(cid)
    print("可用 client_id: %s" % (", ".join(got) if got else "（无！）"))
    return got


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    argv = [a for a in sys.argv[1:]]
    if "--probe" in argv:
        probe_only()
        return

    poll = "--poll" in argv
    argv = [a for a in argv if not a.startswith("--")]
    seconds = 240
    if argv:
        try:
            seconds = max(20, int(argv[0]))
        except Exception:
            pass

    import paho.mqtt.client as mqtt

    s = stamp()
    logpath = os.path.join(out_dir(), "GCU_kbd_capture_%s.log" % s)
    subpath = os.path.join(out_dir(), "GCU_kbd_capture_%s_键盘相关.log" % s)
    f = io.open(logpath, "w", encoding="utf-8")
    f.write("# GCU 键盘灯协议抓包\n")
    f.write("# 开始: %s   时长: %ss   模式: %s\n" % (
        datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), seconds,
        "被动+周期GETSTATUS" if poll else "纯被动"))
    f.write("# 格式: HH:MM:SS.mmm | 方向 | topic | payload\n")
    f.write("# 方向: <<< = GCU 上报 / >>> = 本脚本发送 / ??? = 未知来源\n")
    f.flush()

    state = {"count": 0, "topics": {}, "kbd": 0, "connected": False, "used": None}
    lock = threading.Lock()
    kbd_lines = []

    def on_connect(cl, userdata, flags, rc, properties=None):
        code = getattr(rc, "value", rc)
        if code == 0:
            state["connected"] = True
            cl.subscribe("#", qos=0)
            f.write("### %s CONNECTED as %s, subscribed '#'\n" % (now_ms(), state["used"]))
            f.flush()
            print("[OK] connected as %s, subscribed #" % state["used"], flush=True)
        else:
            f.write("### %s CONNECT rc=%s\n" % (now_ms(), code))
            f.flush()
            print("[FAIL] connect rc=%s" % code, flush=True)

    def on_disconnect(cl, userdata, *args):
        f.write("### %s DISCONNECTED %s\n" % (now_ms(), args))
        f.flush()
        print("[!] disconnected %s" % (args,), flush=True)

    def on_message(cl, userdata, m):
        try:
            body = m.payload.decode("utf-8", "replace")
        except Exception:
            body = repr(m.payload)[:300]
        line = "%s | <<< | %s | %s" % (now_ms(), m.topic, body)
        with lock:
            state["count"] += 1
            state["topics"][m.topic] = state["topics"].get(m.topic, 0) + 1
            low = m.topic.lower()
            bl = body.lower()
            if low.startswith(KBD_TOPIC_HINTS) or any(k in bl for k in KBD_KEY_HINTS):
                state["kbd"] += 1
                kbd_lines.append(line)
            f.write(line + "\n")
            f.flush()

    conn = None
    for cid, user, pwd in CREDENTIALS:
        state["used"] = cid
        c = build_client(mqtt, cid, user, pwd, on_connect, on_message, on_disconnect)
        try:
            c.connect(BROKER_HOST, BROKER_PORT, keepalive=60)
            c.loop_start()
            for _ in range(30):                 # 最多等 3 秒
                if state["connected"]:
                    break
                time.sleep(0.1)
            if state["connected"]:
                conn = c
                if cid == "UWPClient_1":
                    print("[!] 用的是 UWPClient_1 —— 可能与官方控制台抢同一个 client_id", flush=True)
                    f.write("### 警告: 使用 UWPClient_1，可能与官方控制台抢连接\n")
                    f.flush()
                break
            c.loop_stop()
            try:
                c.disconnect()
            except Exception:
                pass
        except Exception as e:
            print("[..] %s 连接失败: %r" % (cid, e), flush=True)
            f.write("### %s 连接失败: %r\n" % (cid, e))
            f.flush()

    if conn is None:
        msg = ("[FAIL] 所有凭据都无法连接 %s:%d。请确认 GCUBridge 服务在运行"
               "（Get-Service GCUBridge）。" % (BROKER_HOST, BROKER_PORT))
        print(msg, flush=True)
        f.write(msg + "\n")
        f.close()
        return

    if poll:
        reqs = [("Setting/Control", {"Action": "GETSTATUS"}),
                ("Customize/Control", {"Action": "GETSUPPORT"}),
                ("Keyboard/Ctrl", {"Action": "GETSTATUS"}),
                ("HidLightbar/Ctrl", {"Action": "GETSTATUS"})]
        def pub(t, p):
            body = json.dumps(p)
            f.write("%s | >>> | %s | %s\n" % (now_ms(), t, body))
            f.flush()
            conn.publish(t, body)

        for t, p in reqs:
            pub(t, p)
            time.sleep(0.3)

    print("", flush=True)
    print("=" * 62, flush=True)
    print(" 抓包中 %ss —— 现在请打开官方控制台，按顺序操作键盘灯光：" % seconds, flush=True)
    print("   1) 打开键盘灯效页   2) 切模式   3) 调亮度", flush=True)
    print("   4) 改颜色           5) 改速度/方向  6) 关灯再开灯", flush=True)
    print(" 每个动作间隔约 5 秒。日志: %s" % logpath, flush=True)
    print("=" * 62, flush=True)

    end = time.time() + seconds
    try:
        while time.time() < end:
            time.sleep(5)
            if poll:
                for t, p in reqs:
                    pub(t, p)
                    time.sleep(0.2)
            left = int(end - time.time())
            print("  ...剩余 %3ds  已收 %d 条（键盘相关 %d）" % (left, state["count"], state["kbd"]), flush=True)
    except KeyboardInterrupt:
        print("\n[中断] 手动停止，正在落盘…", flush=True)

    try:
        conn.loop_stop()
        conn.disconnect()
    except Exception:
        pass

    with lock:
        f.write("\n=== 汇总: 共 %d 条消息, %d 个 topic, 键盘相关 %d 条 ===\n"
                % (state["count"], len(state["topics"]), state["kbd"]))
        for t in sorted(state["topics"]):
            f.write("  %-32s %d\n" % (t, state["topics"][t]))
        f.close()

        with io.open(subpath, "w", encoding="utf-8") as g:
            g.write("# 键盘灯相关子集（topic 含 keyboard/hidlightbar，或 payload 命中灯效关键字）\n")
            g.write("# 共 %d 条\n\n" % len(kbd_lines))
            for line in kbd_lines:
                g.write(line + "\n")

    print("", flush=True)
    print("完成: %d 条消息, %d 个 topic, 键盘相关 %d 条" % (state["count"], len(state["topics"]), state["kbd"]), flush=True)
    print("全量日志: %s" % logpath, flush=True)
    print("键盘子集: %s" % subpath, flush=True)


if __name__ == "__main__":
    main()
