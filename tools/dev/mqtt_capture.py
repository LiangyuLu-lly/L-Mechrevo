# -*- coding: utf-8 -*-
r"""
被动抓包 v2：记录 GCU MQTT 上所有来往消息（重点：LM 发出的显卡模式指令）
================================================================================
v1 问题：on_disconnect 回调签名不对（CallbackAPIVersion.VERSION2 要 5 个参数）-> 线程崩
        且没有自动重连。
v2 修复：正确签名 + 自动重连 + 记录 disconnect 原因 + 周期性心跳日志
⚠️ client_id 与 LM 区分（LM 惯用 UWPClient_2），避免互相踢下线。
用法：py -3 mqtt_capture.py [秒数]
"""
import sys
import io
import os
import datetime
import time

HOST, PORT = "127.0.0.1", 13688
CID = "UWPClient_DshSniffer_A17"
USER, PWD = "UWPClient_User_2", "UWPClient_Pwd888881772688_2"

KEYS = ("gpu", "igpu", "dgpu", "direct", "mode", "action", "support", "status",
        "display", "nvram", "oemdisplay", "hotswap", "mux", "reboot", "restart")


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    import paho.mqtt.client as mqtt

    seconds = int(sys.argv[1]) if len(sys.argv) > 1 else 240
    outdir = r'D:\openLM\diagnostics'
    os.makedirs(outdir, exist_ok=True)
    stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    path = os.path.join(outdir, 'mqtt_capture_%s.log' % stamp)
    f = io.open(path, 'w', encoding='utf-8', newline='\n')
    t0 = time.time()
    count = [0]

    def w(line):
        ts = datetime.datetime.now().strftime('%H:%M:%S.%f')[:-3]
        s = "%s %s" % (ts, line)
        try:
            print(s)
        except Exception:
            pass
        f.write(s + "\n")
        f.flush()

    def on_connect(cl, ud, flags, rc, properties=None):
        w("=== on_connect rc=%s flags=%s -> subscribe '#' ===" % (rc, flags))
        cl.subscribe("#", qos=0)

    def on_disconnect(cl, ud, flags, rc, properties=None):
        # VERSION2: (client, userdata, disconnect_flags, reason_code, properties)
        w("=== on_disconnect flags=%s rc=%s (received so far=%d) ===" % (flags, rc, count[0]))

    def on_message(cl, ud, m):
        count[0] += 1
        body = m.payload.decode("utf-8", "replace")
        low = body.lower()
        mark = "***" if any(k in low for k in KEYS) else "   "
        w("%s <<< RECV [%s] %s" % (mark, m.topic, body[:600]))

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_disconnect = on_disconnect
    c.on_message = on_message
    c.reconnect_delay_set(min_delay=1, max_delay=5)
    w("=== capturing %ds -> %s (client_id=%s) ===" % (seconds, path, CID))
    try:
        c.connect(HOST, PORT, keepalive=15)
    except Exception as ex:
        w("!!! initial connect failed: %r" % ex)
    c.loop_start()

    last = 0
    while time.time() - t0 < seconds:
        time.sleep(2)
        el = int(time.time() - t0)
        if el - last >= 30:
            last = el
            w("--- heartbeat %ds, connected=%s, received=%d ---" % (el, c.is_connected(), count[0]))

    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass
    w("=== finished: received=%d -> %s ===" % (count[0], path))
    f.close()
    print("LOG_PATH=%s" % path)


if __name__ == "__main__":
    main()
