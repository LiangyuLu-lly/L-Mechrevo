# -*- coding: utf-8 -*-
"""抓官方控制台灯效下发报文（GCU MQTT，本地 127.0.0.1:13688）。

用法：
  py -3 tools\\gcu_light_capture.py            # 默认 180 秒
  py -3 tools\\gcu_light_capture.py 300        # 抓 300 秒

操作：脚本连上后，在官方控制台里依次切换目标灯效（如 Logo 的单色/呼吸/混色/彩虹、
亮度 4），每种停 3~5 秒；脚本会把灯效相关报文打印并落盘。

输出（仓库 diagnostics 目录）：
  GCU_light_capture_<时间>.jsonl     全部报文（jsonl）
  GCU_light_capture_<时间>_灯效.log   灯效相关报文（可读）
  GCU_light_capture_<时间>_摘要.txt   按 topic+effect 去重后的完整 payload
"""
import sys, os, io, json, time, datetime, threading

BROKER_HOST = "127.0.0.1"
BROKER_PORT = 13688

# 尽量不踢掉官方控制台的 client_id：先用 _2/_3
CREDENTIALS = [
    ("UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"),
    ("UWPClient_3", "UWPClient_User_3", "UWPClient_Pwd888881772688_3"),
    ("GCUClient", "GCUClient_User", "GCUClient_Pwd888881772688"),
    ("UWPClient_1", "UWPClient_User_1", "UWPClient_Pwd888881772688_1"),
]

OUT_DIR = r"D:\openLM\diagnostics"
LIGHT_TOPIC_PARTS = ("hidlightbar", "keyboard/", "lighting", "/ctrl")


def out_dir():
    d = OUT_DIR if os.path.isdir(OUT_DIR) else os.path.dirname(os.path.abspath(__file__))
    return d


def now_ms():
    return datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]


def stamp():
    return datetime.datetime.now().strftime("%Y%m%d_%H%M%S")


def build_client(mqtt, cid, user, pwd, on_connect, on_message):
    try:
        c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=cid)
    except AttributeError:
        c = mqtt.Client(client_id=cid)
    c.username_pw_set(user, pwd)
    c.on_connect = on_connect
    c.on_message = on_message
    return c


def is_light_topic(topic):
    t = (topic or "").lower()
    return any(p in t for p in LIGHT_TOPIC_PARTS)


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    import paho.mqtt.client as mqtt

    seconds = 180
    for a in sys.argv[1:]:
        try:
            seconds = max(20, int(a))
        except Exception:
            pass

    s = stamp()
    base = os.path.join(out_dir(), "GCU_light_capture_" + s)
    jsonl = io.open(base + ".jsonl", "w", encoding="utf-8")
    log = io.open(base + "_灯效.log", "w", encoding="utf-8")
    log.write("# 官方控制台灯效报文抓包  开始=%s  时长=%ss\n"
              % (datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), seconds))
    log.write("# 格式: HH:MM:SS.mmm | topic | payload\n")
    log.flush()

    state = {"connected": False, "count": 0, "light": 0, "used": None}
    lock = threading.Lock()

    def on_connect(cl, userdata, flags, rc, properties=None):
        code = getattr(rc, "value", rc)
        if code == 0:
            state["connected"] = True
            cl.subscribe("#", qos=0)
            print("[OK] 已连接（%s），开始抓包 %ss" % (state["used"], seconds), flush=True)
            print("     现在去官方控制台切换 Logo 灯效（每种停 3~5 秒）", flush=True)
        else:
            print("[FAIL] 连接失败 rc=%s" % code, flush=True)

    def on_message(cl, userdata, m):
        body = m.payload.decode("utf-8", "replace")
        with lock:
            state["count"] += 1
            try:
                jsonl.write(json.dumps({"t": now_ms(), "topic": m.topic, "payload": body},
                                       ensure_ascii=False) + "\n")
                jsonl.flush()
            except Exception:
                pass
            if is_light_topic(m.topic):
                state["light"] += 1
                line = "%s | %s | %s" % (now_ms(), m.topic, body)
                log.write(line + "\n")
                log.flush()
                print(line[:400], flush=True)

    client = None
    for cid, user, pwd in CREDENTIALS:
        state["used"] = cid
        c = build_client(mqtt, cid, user, pwd, on_connect, on_message)
        try:
            c.connect(BROKER_HOST, BROKER_PORT, keepalive=20)
            c.loop_start()
            for _ in range(40):
                if state["connected"]:
                    break
                time.sleep(0.1)
            if state["connected"]:
                client = c
                break
            c.loop_stop()
        except Exception as e:
            print("[..] %s 连接异常: %r" % (cid, e), flush=True)

    if client is None:
        print("[X] 所有凭据都连不上 %s:%d" % (BROKER_HOST, BROKER_PORT), flush=True)
        return 1

    deadline = time.time() + seconds
    try:
        while time.time() < deadline:
            time.sleep(1)
    except KeyboardInterrupt:
        pass
    client.loop_stop()
    try:
        client.disconnect()
    except Exception:
        pass

    jsonl.close()
    log.close()

    # 摘要：按 topic + effect 去重，保留最后一次完整 payload
    summary = {}
    try:
        for line in io.open(base + ".jsonl", "r", encoding="utf-8"):
            try:
                rec = json.loads(line)
            except Exception:
                continue
            if not is_light_topic(rec.get("topic", "")):
                continue
            try:
                obj = json.loads(rec.get("payload", ""))
            except Exception:
                continue
            effect = str(obj.get("effect", ""))
            key = rec["topic"] + "|" + effect
            summary[key] = obj
    except Exception:
        pass

    sp = base + "_摘要.txt"
    with io.open(sp, "w", encoding="utf-8") as f:
        f.write("# 按 topic+effect 去重（最后一条完整 payload）\n\n")
        for k in sorted(summary):
            f.write("## " + k + "\n")
            f.write(json.dumps(summary[k], ensure_ascii=False, indent=2) + "\n\n")

    print("", flush=True)
    print("抓包结束：报文 %d 条，灯效相关 %d 条" % (state["count"], state["light"]), flush=True)
    print("  全部: " + base + ".jsonl", flush=True)
    print("  灯效: " + base + "_灯效.log", flush=True)
    print("  摘要: " + sp, flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
