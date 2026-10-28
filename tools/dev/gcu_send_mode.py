# -*- coding: utf-8 -*-
"""
直接给 GCU 发显卡模式指令（含救援），payload 与 LM 完全一致。
用法：
  py -3 gcu_send_mode.py status          # 只读：打印当前 ICpuOnly/DGpuDirect 状态
  py -3 gcu_send_mode.py off             # 发 IGPU_ONLY_CONNECT_RB_OFF（=混合模式，安全，用于验证发送链路）
  py -3 gcu_send_mode.py on              # 发 IGPU_ONLY_CONNECT_RB_ON （=核显模式，危险：可能黑屏）
  py -3 gcu_send_mode.py auto            # 发 IGPU_ONLY_CONNECT_RB_AUTO（核显省电）
  py -3 gcu_send_mode.py recover         # 【救援】发 DGPU_DIRECT_CONNECT_TOGGLE_ON 切回独显直连
  py -3 gcu_send_mode.py restart         # 发 DGPU_DIRECT_CONNECT_RESTART
"""
import sys
import json
import time

HOST, PORT = "127.0.0.1", 13688
CID, USER, PWD = "UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"
TOPIC = "Setting/Control"

PAYLOADS = {
    # 2026-10-05：与 LM 实际发送的一致（实测 LM 日志 SwitchGpuMode(0) -> TOGGLE_IGPU）
    "on":      {"Action": "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"},
    # 50 系风格命令，本机也能改 NVRAM 但 GCU 状态字段不反映它
    "on50":    {"Action": "IGPU_ONLY_CONNECT_RB_ON", "SetToWMIEC": "OK"},
    "off":     {"Action": "IGPU_ONLY_CONNECT_RB_OFF", "SetToWMIEC": "OK"},
    "auto":    {"Action": "IGPU_ONLY_CONNECT_RB_AUTO"},
    "dgpu":    {"Action": "DGPU_DIRECT_CONNECT_TOGGLE_ON"},
    "dgpuoff": {"Action": "DGPU_DIRECT_CONNECT_TOGGLE_OFF"},
    "recover": {"Action": "DGPU_DIRECT_CONNECT_TOGGLE_ON"},
    "restart": {"Action": "DGPU_DIRECT_CONNECT_RESTART"},
}
WATCH = ["IGpuOnlyConnectionSwitch_Support", "IGpuOnlyConnectionSwitch_Status",
         "DiscreteGpuDirectConnectionSwitch_Support", "DiscreteGpuDirectConnectionSwitch_Status",
         "IGpuAutoSupportSwitch"]


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    import paho.mqtt.client as mqtt

    arg = (sys.argv[1] if len(sys.argv) > 1 else "status").lower()
    seen = []

    def on_connect(cl, ud, flags, rc, properties=None):
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        body = m.payload.decode("utf-8", "replace")
        if any(k.lower() in body.lower() for k in WATCH):
            seen.append(body)

    def snapshot(c, label):
        seen.clear()
        c.publish(TOPIC, json.dumps({"Action": "GETSTATUS"}), qos=0)
        time.sleep(1.6)
        print("--- %s ---" % label)
        got = False
        for body in seen:
            try:
                obj = json.loads(body)
            except Exception:
                continue
            for k in WATCH:
                if k in obj:
                    print("   %-44s = %r" % (k, obj[k]))
                    got = True
            if got:
                break
        if not got:
            print("   （未取到状态）")

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect(HOST, PORT, keepalive=30)
    c.loop_start()
    time.sleep(1.5)

    snapshot(c, "BEFORE")

    if arg == "status":
        c.loop_stop()
        try:
            c.disconnect()
        except Exception:
            pass
        return

    if arg not in PAYLOADS:
        print("未知参数：%s（可用：%s）" % (arg, ", ".join(sorted(PAYLOADS))))
        c.loop_stop()
        return

    payload = PAYLOADS[arg]
    print("\n>>> 发送到 %s : %s" % (TOPIC, json.dumps(payload, ensure_ascii=False)))
    c.publish(TOPIC, json.dumps(payload), qos=0)
    time.sleep(2.5)

    snapshot(c, "AFTER (2.5s)")

    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass

    if arg in ("on", "auto"):
        print("\n提示：核显类切换通常需要重启生效；若屏幕黑掉，另开一个窗口/SSH 执行：")
        print("   py -3 gcu_send_mode.py recover")


if __name__ == "__main__":
    main()
