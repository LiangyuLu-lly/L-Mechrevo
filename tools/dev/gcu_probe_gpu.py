# -*- coding: utf-8 -*-
"""
只读探针：抓 GPU 相关状态（用于 §9.4.1 #1「30 系显卡模式切换」验证）
被动监听，不发任何命令。关注 topic：GPUDevice/Status、Setting/Status、Customize/*、LCHWOC/Status
用法: py -3 gcu_probe_gpu.py [秒数]
"""
import sys
import json
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import paho.mqtt.client as mqtt

KEYWORDS = ("gpu", "igpu", "dgpu", "display", "route", "mux", "graphic", "nvidia", "amd")


def main():
    seconds = 10
    if len(sys.argv) > 1:
        try:
            seconds = max(3, int(sys.argv[1]))
        except Exception:
            pass

    hits = []

    def on_connect(cl, ud, flags, rc, properties=None):
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        if "GPU" in m.topic or "gpu" in m.topic or "Setting" in m.topic or "Customize" in m.topic or "LCHWOC" in m.topic:
            hits.append((m.topic, m.payload.decode("utf-8", "replace")))

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="UWPClient_2")
    c.username_pw_set("UWPClient_User_2", "UWPClient_Pwd888881772688_2")
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect("127.0.0.1", 13688, keepalive=30)
    c.loop_start()
    time.sleep(1.0)
    # 只读拉取
    for topic in ("GPUDevice/Control", "Setting/Control"):
        c.publish(topic, json.dumps({"Action": "GETSTATUS"}), qos=0)
        time.sleep(0.3)
    time.sleep(seconds)
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass

    seen = set()
    for topic, body in hits:
        if (topic, body) in seen:
            continue
        seen.add((topic, body))
        print("=" * 78)
        print("[" + topic + "]")
        try:
            obj = json.loads(body)
        except Exception:
            print("  " + body[:400])
            continue
        if not isinstance(obj, dict):
            print("  " + body[:400])
            continue
        printed = 0
        for k, v in obj.items():
            if any(word in k.lower() for word in KEYWORDS):
                print("   %-38s = %r" % (k, v))
                printed += 1
        if printed == 0:
            print("   (无 GPU 相关字段；共 %d 个字段) %s" % (len(obj), ",".join(list(obj.keys())[:14])))
    print("=" * 78)
    print("共 %d 条（去重 %d 条）" % (len(hits), len(seen)))


if __name__ == "__main__":
    main()
