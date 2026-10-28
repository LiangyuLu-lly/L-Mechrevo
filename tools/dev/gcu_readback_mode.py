# -*- coding: utf-8 -*-
"""
只读探针：被动监听 Fan/Status，打印模式与配置的回读字段。
用于验证「模式 × 配置」是否在硬件侧生效 —— 不发任何命令，不改状态。

关注字段：CustomProfileIndex / ProfileName / FAN_TableName / OperatingMode / CustomizeTarget
用法: py -3 gcu_readback_mode.py [秒数]
"""
import sys
import json
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import paho.mqtt.client as mqtt

WATCH = ("CustomProfileIndex", "ProfileName", "FAN_TableName", "TableName",
         "OperatingMode", "CustomizeTarget", "PowerMode", "FAN_OperatingMode")


def main():
    seconds = 12
    if len(sys.argv) > 1:
        try:
            seconds = max(3, int(sys.argv[1]))
        except Exception:
            pass

    hits = []

    def on_connect(cl, ud, flags, rc, properties=None):
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        body = m.payload.decode("utf-8", "replace")
        if any(k in body for k in WATCH):
            hits.append((m.topic, body))

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="UWPClient_2")
    c.username_pw_set("UWPClient_User_2", "UWPClient_Pwd888881772688_2")
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect("127.0.0.1", 13688, keepalive=30)
    c.loop_start()
    time.sleep(1.0)
    # 轻量拉取一次（只读）
    c.publish("Fan/Control", json.dumps({"Action": "GETSTATUS"}), qos=0)
    time.sleep(0.4)
    c.publish("Setting/Control", json.dumps({"Action": "GETSTATUS"}), qos=0)
    time.sleep(seconds)
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass

    if not hits:
        print("未捕获到含目标字段的消息（%ds）" % seconds)
        return
    seen = set()
    for topic, body in hits:
        key = (topic, body)
        if key in seen:
            continue
        seen.add(key)
        print("=" * 76)
        print("[" + topic + "]")
        try:
            obj = json.loads(body, object_pairs_hook=lambda p: dict(p))
        except Exception:
            print("  " + body[:600])
            continue
        for k in WATCH:
            if k in obj:
                print("   %-22s = %r" % (k, obj[k]))
    print("=" * 76)
    print("共 %d 条（去重后 %d 条）" % (len(hits), len(seen)))


if __name__ == "__main__":
    main()
