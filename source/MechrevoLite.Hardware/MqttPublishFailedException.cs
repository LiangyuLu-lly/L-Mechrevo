using System;

namespace MechrevoLite.Hardware;

public sealed class MqttPublishFailedException : Exception
{
	public string Topic { get; }

	public MqttPublishFailedException(string topic, string message)
		: base("发布到 " + topic + " 失败：" + message)
	{
		Topic = topic;
	}

	public MqttPublishFailedException(string topic, string message, Exception innerException)
		: base("发布到 " + topic + " 失败：" + message, innerException)
	{
		Topic = topic;
	}
}
