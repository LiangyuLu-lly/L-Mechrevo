# -*- coding: utf-8 -*-
"""
查 GCU 上报的显卡/核显能力字段（原始值）
订阅 + 发 GETSTATUS/GETSUPPORT，把与核显模式判断相关的字段打出来。
用法: py -3 gcu_query_igpu.py
"""
import sys
import io
import json
import time
import threading

HOST, PORT = "127.0.0.1", 13688
CID, USER, PWD = "UWPClient_2", "UWPClient_User_2", "UWPClient_Pwd888881772688_2"

# MechrevoDeviceCapabilities / MechrevoHw 实际认的字段名
KEYS = [
    # 核显模式能力（Capabilities.IgpuOnly 认这 4 个）
    "iGPUModeOnlySupport", "IGpuModeOnlySupport", "IGPUOnlyModeSupport",
    "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support",
    # 独显直连
    "DGpuDirectConnectionSupport", "DiscreteGpuDirectConnectionSupport",
    "MuxSwitchSupport", "DiscreteGpuDirectConnectionSwitch_Support",
    # 热切换
    "GpuHotSwapSwitchSupport", "lgpuHotSwapSwitchStatus",
    # 状态与上下文
    "IGpuOnlyConnectionSwitch_Status", "DiscreteGpuDirectConnectionSwitch_Status",
    "IGpuAutoSupportSwitch", "ProjectID", "KeyboardType", "SupportColorCalibration",
]


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    import paho.mqtt.client as mqtt

    seen = []

    def on_connect(cl, ud, flags, rc, properties=None):
        cl.subscribe("#", qos=0)

    def on_message(cl, ud, m):
        body = m.payload.decode("utf-8", "replace")
        low = body.lower()
        if any(k.lower() in low for k in KEYS):
            seen.append((m.topic, body))

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=CID)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect(HOST, PORT, keepalive=30)
    c.loop_start()
    time.sleep(1.5)

    for topic, payload in (("Setting/Control", {"Action": "GETSTATUS"}),
                           ("Customize/Control", {"Action": "GETSUPPORT"}),
                           ("Customize/Control", {"Action": "GETSETUPINFO"})):
        c.publish(topic, json.dumps(payload), qos=0)
        time.sleep(0.4)
    time.sleep(3)

    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass

    if not seen:
        print("未收到含相关字段的消息")
        return
    print("=" * 78)
    for topic, body in seen:
        try:
            obj = json.loads(body, object_pairs_hook=lambda p: dict(p))
        except Exception:
            obj = None
        print("[%s]" % topic)
        if isinstance(obj, dict):
            for k in KEYS:
                if k in obj:
                    print("   %-42s = %r" % (k, obj[k]))
            # 兜底：模糊匹配漏网的大小写变体
            for k, v in obj.items():
                if k not in KEYS and any(x.lower() == k.lower() for x in KEYS):
                    print("   %-42s = %r   (大小写变体)" % (k, v))
        else:
            print("   " + body[:400])
        print("-" * 78)


if __name__ == "__main__":
    main()
