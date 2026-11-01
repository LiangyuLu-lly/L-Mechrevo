using System;
using System.Text.RegularExpressions;
using MechrevoLite.Gpu;

namespace MechrevoLite.Usage;

internal sealed record UsageSnapshot(string Id, string Ver, string Event, string Model, string Project, string Bios, string Gpu, string GpuName, string Cpu, bool Gcu, bool Keyboard, bool Lightbar, bool Elevated = false, string Tier = "", int Port = 0, string Os = "", string Update = "", int Errors = 0)
{
	internal const int MaxFieldChars = 80;

	internal const string EventStart = "start";

	internal const string EventBeat = "beat";

	internal const string EventStop = "stop";

	private static readonly Regex SafeToken = new Regex("[^A-Za-z0-9 ._\\-+#()]", RegexOptions.Compiled);

	internal static bool IsAllowedEvent(string? value)
	{
		switch (value)
		{
		case "start":
		case "beat":
		case "stop":
			return true;
		default:
			return false;
		}
	}

	internal static string Clip(string? value)
	{
		string text = (value ?? "").Trim();
		if (text.Length > 80)
		{
			text = text.Substring(0, 80);
		}
		return SafeToken.Replace(text, "").Trim();
	}

	internal static UsageSnapshot Sanitize(UsageSnapshot raw)
	{
		string text = (IsAllowedEvent(raw.Event) ? raw.Event : "beat");
		string text2 = Clip(raw.Id);
		int length = text2.Length;
		if (length < 8 || length > 64)
		{
			text2 = "";
		}
		int port = ((raw.Port > 0 && raw.Port <= 65535) ? raw.Port : 0);
		return raw with
		{
			Id = text2,
			Ver = Clip(raw.Ver),
			Event = text,
			Model = Clip(raw.Model),
			Project = Clip(raw.Project),
			Bios = Clip(raw.Bios),
			Gpu = Clip(raw.Gpu),
			GpuName = Clip(raw.GpuName),
			Cpu = Clip(raw.Cpu),
			Tier = Clip(raw.Tier),
			Port = port,
			Os = Clip(raw.Os),
			Update = Clip(raw.Update),
			Errors = Math.Clamp(raw.Errors, 0, 1000000)
		};
	}

	internal static string GpuGenToken(DgpuGenerationKind kind)
	{
		return kind switch
		{
			DgpuGenerationKind.Gen30 => "30", 
			DgpuGenerationKind.Gen40 => "40", 
			DgpuGenerationKind.Gen50 => "50", 
			DgpuGenerationKind.NoDgpu => "none", 
			_ => "unknown", 
		};
	}
}
