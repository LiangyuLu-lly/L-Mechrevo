# -*- coding: utf-8 -*-
"""全量打印 GCU 关键 topic 的原始 payload（用于核对字段名是否存在）"""
import sys, json, time
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import paho.mqtt.client as mqtt

TOPICS = ("Setting/Status", "Customize/SupportInfo", "Customize/Info",
          "Customize/SupportControl", "GPUDevice/Status")
got = {}


def main():
    def on_connect(cl, ud, flags, rc, properties=None):
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        if m.topic in TOPICS:
            got[m.topic] = m.payload.decode("utf-8", "replace")

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="UWPClient_2")
    c.username_pw_set("UWPClient_User_2", "UWPClient_Pwd888881772688_2")
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect("127.0.0.1", 13688, keepalive=30)
    c.loop_start()
    time.sleep(1.5)
    for t, p in (("Setting/Control", {"Action": "GETSTATUS"}),
                 ("Customize/Control", {"Action": "GETSUPPORT"}),
                 ("Customize/Control", {"Action": "GETSETUPINFO"}),
                 ("GPUDevice/Control", {"Action": "GETSTATUS"})):
        c.publish(t, json.dumps(p), qos=0)
        time.sleep(0.4)
    time.sleep(3)
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass

    for t in TOPICS:
        body = got.get(t)
        print("=" * 80)
        print("[" + t + "]")
        if not body:
            print("  （未收到）")
            continue
        try:
            obj = json.loads(body, object_pairs_hook=lambda p: dict(p))
            print(json.dumps(obj, ensure_ascii=False, indent=2)[:4000])
        except Exception:
            print("  " + body[:2000])
        # 显式核对核显相关字段
        low = body.lower()
        hits = [k for k in ("igpumodeonlysupport", "igpuonlyconnection", "igpuauto",
                            "integratedgpuonly", "igpuonlymodesupport")
                if k in low]
        print("  >>> 核显相关字段命中: %s" % (hits if hits else "无"))


if __name__ == "__main__":
    main()
