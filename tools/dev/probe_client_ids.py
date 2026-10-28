# -*- coding: utf-8 -*-
"""探测：哪些 client_id 能被 GCU 的 MQTT broker 接受（只连不订阅，安全）"""
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import paho.mqtt.client as mqtt

HOST, PORT = "127.0.0.1", 13688
USER, PWD = "UWPClient_User_2", "UWPClient_Pwd888881772688_2"

CANDIDATES = [
    "UWPClient_2", "UWPClient_1", "UWPClient_3", "UWPClient", "UWPClient_0",
    "UWPClient_User_2", "CCUWinUI", "ControlCenter", "L-Mechrevo", "L_Mechrevo",
    "MechrevoLite", "LM", "GCUClient", "MyControlCenter", "UWPClient_Test",
    "UWPClient_DshSniffer_A17", "UWPClient_DshSniffer_9F3",
]


def probe(cid, timeout=2.5):
    result = {"rc": None}

    def on_connect(cl, ud, flags, rc, properties=None):
        result["rc"] = str(rc)

    def on_disconnect(cl, ud, flags, rc, properties=None):
        pass

    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=cid)
    c.username_pw_set(USER, PWD)
    c.on_connect = on_connect
    c.on_disconnect = on_disconnect
    try:
        c.connect(HOST, PORT, keepalive=10)
    except Exception as ex:
        return "connect-exception: %r" % ex
    c.loop_start()
    t0 = time.time()
    while time.time() - t0 < timeout and result["rc"] is None:
        time.sleep(0.15)
    ok = c.is_connected()
    c.loop_stop()
    try:
        c.disconnect()
    except Exception:
        pass
    if result["rc"] is None:
        return "no-connack (connected=%s)" % ok
    return result["rc"]


def main():
    print("=" * 70)
    ok_list, bad_list = [], []
    for cid in CANDIDATES:
        r = probe(cid)
        mark = "OK  " if r == "Success" else "FAIL"
        print("  [%s] %-28s -> %s" % (mark, cid, r))
        (ok_list if r == "Success" else bad_list).append(cid)
    print("=" * 70)
    print("被接受的 client_id: %s" % (ok_list if ok_list else "（无）"))
    print("被拒绝的:            %d 个" % len(bad_list))


if __name__ == "__main__":
    main()
