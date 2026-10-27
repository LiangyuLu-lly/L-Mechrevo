using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Hardware;

internal sealed class PowerWallVerifier
{
	/// <summary>
	/// 2026-10-09（NVML 原生崩溃修复）：本地功耗采样链已降级停用时，UI 明确显示「不可用」，
	/// 既不显示旧瓦数（假数字），也不显示「需要一段持续高负载才能判定」这种会误导人的未知文案。
	/// </summary>
	internal const string UnavailableText = "功耗墙判定不可用（本地功耗采样已停用，稍后自动重试）";

	internal static readonly TimeSpan Window = TimeSpan.FromSeconds(60L);

	internal const int MinimumSamples = 20;

	internal const double ExceedRatio = 1.15;

	internal const double EngagedRatio = 0.85;

	private readonly object _sync = new object();

	private readonly Queue<(DateTime At, double Watts)> _samples = new Queue<(DateTime, double)>();

	private int _limitWatts = -1;

	public int LimitWatts
	{
		get
		{
			lock (_sync)
			{
				return _limitWatts;
			}
		}
	}

	public double? AverageWatts
	{
		get
		{
			lock (_sync)
			{
				return AverageWattsCore();
			}
		}
	}

	public void Observe(double watts, int limitWatts, DateTime now)
	{
		lock (_sync)
		{
			if (limitWatts != _limitWatts)
			{
				_samples.Clear();
				_limitWatts = limitWatts;
			}
			if (limitWatts > 0 && double.IsFinite(watts) && !(watts < 0.0))
			{
				_samples.Enqueue((now, watts));
				while (_samples.Count > 0 && now - _samples.Peek().At > Window)
				{
					_samples.Dequeue();
				}
			}
		}
	}

	public void Reset()
	{
		lock (_sync)
		{
			_samples.Clear();
			_limitWatts = -1;
		}
	}

	public PowerWallVerdict Evaluate(DateTime now)
	{
		lock (_sync)
		{
			return EvaluateCore(now);
		}
	}

	private PowerWallVerdict EvaluateCore(DateTime now)
	{
		if (_limitWatts <= 0 || _samples.Count < 20)
		{
			return PowerWallVerdict.Unknown;
		}
		if (now - _samples.Peek().At < Window)
		{
			return PowerWallVerdict.Unknown;
		}
		double num = _samples.Sum(((DateTime At, double Watts) sample) => sample.Watts) / (double)_samples.Count;
		if (num > (double)_limitWatts * 1.15)
		{
			return PowerWallVerdict.NotEnforced;
		}
		if (num >= (double)_limitWatts * 0.85)
		{
			return PowerWallVerdict.Enforced;
		}
		return PowerWallVerdict.Unknown;
	}

	private double? AverageWattsCore()
	{
		if (_samples.Count >= 20)
		{
			return _samples.Sum(((DateTime At, double Watts) sample) => sample.Watts) / (double)_samples.Count;
		}
		return null;
	}

	public string Describe(DateTime now)
	{
		lock (_sync)
		{
			PowerWallVerdict powerWallVerdict = EvaluateCore(now);
			double? num = AverageWattsCore();
			return powerWallVerdict switch
			{
				PowerWallVerdict.Enforced => $"功耗墙生效中（{Window.TotalSeconds:F0}s 均值 {num.Value:F1}W / 上限 {_limitWatts}W）", 
				PowerWallVerdict.NotEnforced => $"功耗墙未生效：{Window.TotalSeconds:F0}s 均值 {num.Value:F1}W 超过上限 {_limitWatts}W", 
				_ => "功耗墙状态未知（需要一段持续高负载才能判定）", 
			};
		}
	}
}
