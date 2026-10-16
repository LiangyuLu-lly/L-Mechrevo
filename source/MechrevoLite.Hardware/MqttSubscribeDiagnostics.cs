using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Diagnostics.Logger;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Exceptions;
using MQTTnet.Implementations;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-09（GCU 订阅侧【取证】）：极光Pro 现场只留得下
/// 「MQTT CONNECT 发起 → CONNACK Success → MechrevoHw 订阅失败: The MQTT client is disconnected.」，
/// 关键那一格永远是空的：**SUBSCRIBE 到底有没有真的发出去、有没有收到任何回包**。
/// </summary>
/// <remarks>
/// 本类只是把默认适配器工厂**包一层**（行为逐字不变：参数原样透传给
/// <see cref="MqttClientAdapterFactory"/>），额外留一份 <see cref="IMqttChannelAdapter"/> 引用，
/// 用来在断开/超时日志里补上「连接四元组 + 收发字节数」这两样以前完全没有的证据。
/// 不碰任何 MQTT 语义：不改主题、不改 QoS、不改超时、不 PUBLISH。
/// </remarks>
internal sealed class MqttDiagAdapterFactory : IMqttClientAdapterFactory
{
	private readonly MqttClientAdapterFactory _inner = new MqttClientAdapterFactory();

	private IMqttChannelAdapter? _last;

	public IMqttChannelAdapter CreateClientAdapter(MqttClientOptions options, MqttPacketInspector packetInspector, IMqttNetLogger logger)
	{
		IMqttChannelAdapter adapter = _inner.CreateClientAdapter(options, packetInspector, logger);
		_last = adapter;
		return adapter;
	}

	/// <summary>连接四元组（本地/远端 IP:port）+ 该适配器累计收发的字节数；读不到就写原因，绝不抛。</summary>
	internal string DescribeSocket()
	{
		try
		{
			IMqttChannelAdapter? adapter = _last;
			if (adapter == null)
			{
				return "无(尚未建过适配器)";
			}
			return $"本地={DescribeEndPoint(adapter.LocalEndPoint)} 远端={DescribeEndPoint(adapter.RemoteEndPoint)} 累计发送字节={adapter.BytesSent} 累计接收字节={adapter.BytesReceived}";
		}
		catch (Exception ex)
		{
			return "读取失败(" + ex.GetType().Name + ")";
		}
	}

	private static string DescribeEndPoint(EndPoint? endPoint)
	{
		return (endPoint == null) ? "n/a" : endPoint.ToString();
	}
}

/// <summary>
/// 2026-10-09（GCU 订阅侧【取证】）：真报文级观察窗 —— 挂在 <c>IMqttClient.InspectPacketAsync</c> 上，
/// 拿到的是**实际写出/读入的原始报文**（含方向），因此能一眼分开两种现场：
/// ①「SUBSCRIBE 根本没发出去」（窗口里发出去 0 个 SUBSCRIBE）；
/// ②「发出去了但没收到 SUBACK」（窗口里发出去≥1 个 SUBSCRIBE、SUBACK 收到 0 个）。
/// </summary>
/// <remarks>
/// 只读观察：不改报文、不改时序、不新增订阅、不发任何 PUBLISH。
/// 入站报文默认只计数（PINGRESP / PUBLISH 很频繁，不能刷屏），只有 SUBSCRIBE / SUBACK 各写一行。
/// 线程：MQTTnet 在收发路径上回调，所有状态都在 <see cref="_sync"/> 里改；**绝不**在持锁时写日志。
/// ⚠️ 协议固定 V3.1.1（见 ConnectAsync 的 WithProtocolVersion），故 SUBSCRIBE/SUBACK 的变长头部
/// 只有 2 字节 packetId、没有 MQTT5 的 properties，解析口径按 V3.1.1。
/// </remarks>
internal sealed class MqttPacketWatch
{
	private const int PacketTypeSubscribe = 8;

	private const int PacketTypeSubAck = 9;

	private readonly object _sync = new object();

	private long _subscribeTotal;

	private long _windowSubscribeCount;

	private int _windowSubscribeBytes;

	private int _windowSubscribeTopics;

	private string _windowSubscribeQos = "n/a";

	private int _windowSubscribePacketId = -1;

	private long _windowStartTick;

	private long _windowInboundCount;

	private long _windowSubAckCount;

	private string _windowSubAckCodes = "无";

	private long _windowFirstInboundTick;

	private long _lastInboundTick;

	private string _lastInboundType = "无";

	/// <summary>每发一次 SUBSCRIBE 之前调用：开一个新观察窗（窗口内计数归零）。</summary>
	internal void BeginSubscribeWait()
	{
		lock (_sync)
		{
			_windowStartTick = Environment.TickCount64;
			_windowSubscribeCount = 0L;
			_windowSubscribeBytes = 0;
			_windowSubscribeTopics = 0;
			_windowSubscribeQos = "n/a";
			_windowSubscribePacketId = -1;
			_windowInboundCount = 0L;
			_windowSubAckCount = 0L;
			_windowSubAckCodes = "无";
			_windowFirstInboundTick = 0L;
		}
	}

	/// <summary>
	/// 观察一个报文。返回值非空时 = 该写一行日志（调用方在锁外写）；返回 null = 只记账不写日志。
	/// </summary>
	internal string? Observe(MqttPacketFlowDirection direction, byte[]? buffer)
	{
		if (buffer == null || buffer.Length < 2)
		{
			return null;
		}
		int type = (buffer[0] >> 4) & 0x0F;
		long now = Environment.TickCount64;
		string? line = null;
		lock (_sync)
		{
			if (direction == MqttPacketFlowDirection.Inbound)
			{
				_lastInboundTick = now;
				_lastInboundType = PacketTypeName(type);
				if (_windowStartTick != 0L)
				{
					_windowInboundCount++;
					if (_windowFirstInboundTick == 0L)
					{
						_windowFirstInboundTick = now;
					}
					if (type == PacketTypeSubAck)
					{
						_windowSubAckCount++;
						_windowSubAckCodes = DescribeSubAck(buffer);
					}
				}
				if (type == PacketTypeSubAck)
				{
					line = $"MQTT 报文 收到: 方向=入站 类型=SUBACK 头=0x{buffer[0]:X2} 字节={buffer.Length} packetId={ReadPacketIdentifier(buffer)} 返回码=[{DescribeSubAck(buffer)}](0x80=该主题订阅被拒) 时刻={DiagClock(now)}";
				}
			}
			else if (type == PacketTypeSubscribe)
			{
				_subscribeTotal++;
				(int count, string qos) = DescribeSubscribe(buffer);
				if (_windowStartTick != 0L)
				{
					_windowSubscribeCount++;
					_windowSubscribeBytes = buffer.Length;
					_windowSubscribeTopics = count;
					_windowSubscribeQos = qos;
					_windowSubscribePacketId = ReadPacketIdentifier(buffer);
				}
				line = $"MQTT 报文 发出: 方向=出站 类型=SUBSCRIBE 头=0x{buffer[0]:X2} 字节={buffer.Length} packetId={ReadPacketIdentifier(buffer)} topics={count} qos={qos} 时刻={DiagClock(now)}";
			}
		}
		return line;
	}

	/// <summary>
	/// 一窗的结论：等待时长 + 窗口内收到多少报文 / 多少 SUBACK（含 0x80）+ 我方 SUBSCRIBE 到底发出去没有。
	/// ⚠️ 文案里刻意不出现「失败/超时/异常」等 Logger 错误标记词，成功路径的日志不会被误判成 Error。
	/// </summary>
	internal string DescribeWaitOutcome()
	{
		lock (_sync)
		{
			string waited = (_windowStartTick == 0L) ? "n/a" : ((Environment.TickCount64 - _windowStartTick).ToString(CultureInfo.InvariantCulture) + "ms");
			string firstInbound = (_windowFirstInboundTick == 0L) ? "首个回包=无" : ("首个回包耗时=" + (_windowFirstInboundTick - _windowStartTick).ToString(CultureInfo.InvariantCulture) + "ms");
			string subAck = (_windowSubAckCount == 0L) ? "SUBACK=0(未收到)" : ("SUBACK=" + _windowSubAckCount.ToString(CultureInfo.InvariantCulture) + " 返回码=[" + _windowSubAckCodes + "]");
			string sent = (_windowSubscribeCount == 0L)
				? $"SUBSCRIBE=窗口内发出去 0 个(累计 {_subscribeTotal} 个)"
				: ($"SUBSCRIBE=窗口内发出去 {_windowSubscribeCount} 个({_windowSubscribeBytes} 字节 packetId={_windowSubscribePacketId} topics={_windowSubscribeTopics} qos={_windowSubscribeQos})");
			string lastInbound = (_lastInboundTick == 0L) ? "最近入站=无" : ("最近入站=" + _lastInboundType);
			return $"等待={waited} 窗口内收到报文={_windowInboundCount} 个 {subAck} {firstInbound} {sent} {lastInbound}";
		}
	}

	/// <summary>「SUBSCRIBE 是否真的发出去了」这一格的直接判据（供日志单独取用）。</summary>
	internal bool SubscribeObservedInWindow
	{
		get
		{
			lock (_sync)
			{
				return _windowSubscribeCount > 0L;
			}
		}
	}

	private static string PacketTypeName(int type)
	{
		switch (type)
		{
		case 1:
			return "CONNECT";
		case 2:
			return "CONNACK";
		case 3:
			return "PUBLISH";
		case 4:
			return "PUBACK";
		case 5:
			return "PUBREC";
		case 6:
			return "PUBREL";
		case 7:
			return "PUBCOMP";
		case 8:
			return "SUBSCRIBE";
		case 9:
			return "SUBACK";
		case 10:
			return "UNSUBSCRIBE";
		case 11:
			return "UNSUBACK";
		case 12:
			return "PINGREQ";
		case 13:
			return "PINGRESP";
		case 14:
			return "DISCONNECT";
		default:
			return "类型" + type.ToString(CultureInfo.InvariantCulture);
		}
	}

	/// <summary>跳过固定头（1 字节）+ 剩余长度（1~4 字节），返回变长头部起点；解析不了返回 -1。</summary>
	private static int BodyOffset(byte[] buffer)
	{
		int offset = 1;
		int guard = 0;
		while (offset < buffer.Length && guard < 4)
		{
			byte b = buffer[offset++];
			guard++;
			if ((b & 0x80) == 0)
			{
				break;
			}
		}
		return (offset >= buffer.Length) ? (-1) : offset;
	}

	private static int ReadPacketIdentifier(byte[] buffer)
	{
		int offset = BodyOffset(buffer);
		if (offset < 0 || offset + 2 > buffer.Length)
		{
			return -1;
		}
		return (buffer[offset] << 8) | buffer[offset + 1];
	}

	/// <summary>SUBACK 的返回码列表（0x00=GrantedQoS0 … 0x80=该主题被拒）。</summary>
	private static string DescribeSubAck(byte[] buffer)
	{
		int offset = BodyOffset(buffer);
		if (offset < 0 || offset + 2 > buffer.Length)
		{
			return "n/a";
		}
		int index = offset + 2;
		if (index >= buffer.Length)
		{
			return "无返回码";
		}
		string text = "";
		for (int i = index; i < buffer.Length; i++)
		{
			if (text.Length > 0)
			{
				text += ",";
			}
			text += "0x" + buffer[i].ToString("X2", CultureInfo.InvariantCulture);
		}
		return text;
	}

	/// <summary>SUBSCRIBE 载荷里的主题过滤器个数与出现过的 QoS 档位（我们只发 qos=0，变了就是行为被改了）。</summary>
	private static (int Count, string Qos) DescribeSubscribe(byte[] buffer)
	{
		int offset = BodyOffset(buffer);
		if (offset < 0 || offset + 2 > buffer.Length)
		{
			return (-1, "n/a");
		}
		int index = offset + 2;
		int count = 0;
		string qos = "";
		while (index < buffer.Length)
		{
			if (index + 2 > buffer.Length)
			{
				break;
			}
			int length = (buffer[index] << 8) | buffer[index + 1];
			index += 2;
			if (length < 0 || index + length >= buffer.Length)
			{
				break;
			}
			index += length;
			string one = buffer[index++].ToString(CultureInfo.InvariantCulture);
			count++;
			if (qos.Length == 0)
			{
				qos = one;
			}
			else if (qos.IndexOf(one, StringComparison.Ordinal) < 0)
			{
				qos = qos + "/" + one;
			}
		}
		return (count, (count == 0) ? "n/a" : qos);
	}

	internal static string DiagClock(long tickCount64)
	{
		return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
	}
}

/// <summary>
/// 2026-10-09（GCU 订阅侧【修复】）：订阅重试的节制参数 —— 一次订阅不成绝不再立刻判「断开」并回退 clientId。
/// </summary>
/// <remarks>
/// 现场之所以放大成「约每 30 秒无限循环」：`_4→_3→_2` 每次都是
/// 「CONNECT 成功 → SUBSCRIBE 没成 → 立刻断开 → 回退 clientId → 重连」，整段重连每轮约 30s（重连退避封顶值）。
/// 现在的口径：
/// ① 同一条连接上先做 <see cref="AttemptsPerConnection"/> 次 SUBSCRIBE（400ms / 1200ms 退避）；
/// ② 还不成而连接仍在 ⇒ **保持连接**，交给订阅重试循环（首轮 2s，逐轮翻倍、30s 封顶）只重发 SUBSCRIBE，不再整段重连；
/// ③ 只有「连续 <see cref="RoundsBeforeFallback"/> 轮」都没成，才回退 clientId 一档；回退总次数仍是既有上限 2（`_4→_3→_2`），
///    耗尽后停在最后一个 id 且**不再回退**（只在保持连接上继续重试 SUBSCRIBE）；
/// ④ 2026-10-09（本次修复：判死重建）：连接被看门狗判死（IsConnected=false）时**不再**整段拆连 + 立刻回退 clientId ——
///    由订阅重试循环复用既有 ConnectAsync 重建连接，连上后继续重试同一批 SUBSCRIBE，退避同样是 2s→4s→…→30s 封顶；
///    一个循环里判死重建最多 <see cref="MaxReconnectRounds"/> 次，到点退出交回既有重连机制（重连退避 30s 封顶）。
/// 不改主题集合、不新增订阅、不发任何 PUBLISH、不改既有的 clientId 白名单与凭据、不改任何超时。
/// </remarks>
internal static class MqttSubscribeRetryPolicy
{
	/// <summary>同一条连接内的 SUBSCRIBE 尝试次数（1 次首发 + 2 次重试）。</summary>
	internal const int AttemptsPerConnection = 3;

	internal const int RetryDelayFirstMs = 400;

	internal const int RetryDelaySecondMs = 1200;

	/// <summary>连续这么多「轮」订阅都没成，才允许回退 clientId 一档（回退总次数仍受 MaxClientIdFallbacks=2 限制）。</summary>
	internal const int RoundsBeforeFallback = 3;

	/// <summary>订阅重试循环的首轮间隔。</summary>
	internal const int FirstRoundDelayMs = 2000;

	/// <summary>订阅重试循环的间隔上限（比既有重连退避 30s 不再更频繁）。</summary>
	internal const int MaxRoundDelayMs = 30000;

	/// <summary>
	/// 2026-10-09（本次修复：判死重建）：**一个**订阅重试循环里允许「看门狗判死 ⇒ 重建连接 ⇒ 再试 SUBSCRIBE」
	/// 的最大次数。取 4 的理由：够走完一次 <see cref="RoundsBeforeFallback"/>=3 的连续失败计数（第 3 轮触发
	/// 回退 clientId 一档），再余 1 轮用新 id 复查；到点就退出循环、交回既有重连机制，避免把「重试」变成
	/// 新的无限拆连循环（改造前是真机上 8 次整段重连）。
	/// </summary>
	internal const int MaxReconnectRounds = 4;

	internal static int RetryDelayMs(int failedAttempt)
	{
		return (failedAttempt <= 1) ? RetryDelayFirstMs : RetryDelaySecondMs;
	}

	internal static int NextRoundDelayMs(int currentDelayMs)
	{
		return (int)Math.Min((long)MaxRoundDelayMs, Math.Max((long)FirstRoundDelayMs, (long)currentDelayMs) * 2L);
	}
}

/// <summary>2026-10-09（GCU 订阅侧取证）：订阅链路上几个纯函数式的判据/文案（不改任何状态）。</summary>
internal static class MqttSubscribeForensics
{
	/// <summary>
	/// 「没发出去」和「发了没回」的分界线：
	/// MqttCommunicationTimedOutException = 报文已写出、等对端回包到点（= 发了没回）；
	/// IOException / SocketException（含被 RST）在任何一层 = 写出/读回失败（= 没发出去）。
	/// </summary>
	internal static string DescribeSendVerdict(Exception? exception)
	{
		for (Exception? current = exception; current != null; current = current.InnerException)
		{
			if (current is MqttCommunicationTimedOutException)
			{
				return "发了没回(等 SUBACK 到点)";
			}
			if (current is IOException || current is SocketException)
			{
				return "没发出去(写出失败)";
			}
		}
		return "未定(既非等回包到点、也非写出失败)";
	}

	/// <summary>SUBACK 结果码汇总（成功时也应只有 GrantedQoS0/1/2，出现别的就是 broker 拒订）。</summary>
	internal static string DescribeSubscribeResultCodes(MqttClientSubscribeResult? result)
	{
		if (result == null)
		{
			return "n/a";
		}
		try
		{
			string text = string.Join(" ", result.Items.GroupBy(item => item.ResultCode).Select(group => group.Key + "×" + group.Count()));
			return (text.Length == 0) ? "n/a" : text;
		}
		catch (Exception ex)
		{
			return "汇总失败(" + ex.GetType().Name + ")";
		}
	}
}
