# -*- coding: utf-8 -*-
"""
重启后自检：核显模式到底有没有生效
用法：py -3 post_reboot_check.py
"""
import sys
import os
import io
import json
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HOST, PORT = "127.0.0.1", 13688
CID, USER, PWD = "UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"
LOG = os.path.join(os.environ.get("APPDATA", ""), "MechrevoLite", "log.txt")


def gcu_status():
    import paho.mqtt.client as mqtt
    out = {}

    def on_connect(c, u, f, rc, p=None):
        c.subscribe("#", qos=0)

    def on_message(c, u, m):
        body = m.payload.decode("utf-8", "replace")
        try:
            obj = json.loads(body)
        except Exception:
            return
        for k in ("IGpuOnlyConnectionSwitch_Status", "IGpuOnlyConnectionSwitch_Support",
                  "DiscreteGpuDirectConnectionSwitch_Status"):
            if k in obj:
                out[k] = obj[k]

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect(HOST, PORT, keepalive=20)
    c.loop_start()
    time.sleep(1.5)
    c.publish("Setting/Control", json.dumps({"Action": "GETSTATUS"}), qos=0)
    time.sleep(2.5)
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass
    return out


def nvram_lines(n=3):
    if not os.path.isfile(LOG):
        return []
    t = io.open(LOG, encoding="utf-8", errors="replace").read().split("\n")
    return [l.strip() for l in t if "NVRAM display byte" in l][-n:]


def main():
    print("=" * 72)
    print("重启后自检")
    print("=" * 72)

    st = gcu_status()
    print("\n[GCU 状态]")
    for k, v in st.items():
        print("   %-44s = %r" % (k, v))

    igpu_mode = str(st.get("IGpuOnlyConnectionSwitch_Status", ""))
    if "IGPU_ONLY_CONNECT_RB_ON" in igpu_mode or "IGPU_ONLY_ON" in igpu_mode:
        print("\n>>> 核显模式【已生效】✅  （LM 的核显按钮应该出现了）")
    else:
        print("\n>>> 核显模式【未生效】❌  （这条 GCU 变体不支持，需换 GCU-40-51749 系包）")

    print("\n[NVRAM 显示模式字节（最后 %d 条）]" % 3)
    for l in nvram_lines():
        print("   " + l)

    print("\n提示：NVRAM 只在 LM 启动/路由复核时读取；")
    print("      若这里没有新的时间戳，说明 LM 自重启后还没写过日志行。")


if __name__ == "__main__":
    main()
