using System;
using System.Threading;
using Probe;

namespace MechrevoLite.Hardware;

public sealed class PowerInputSampler
{
	public const int RefreshIntervalMilliseconds = 5000;

	private const int MaxConsecutiveFailures = 3;

	private readonly Func<IEcReadTransport?> _transport;

	private readonly Func<bool> _acOnline;

	private readonly Func<bool> _decodeEnabled;

	private readonly Func<long> _tick;

	private readonly object _sync = new object();

	private PowerInputSample? _current;

	private int _lastComplex = int.MinValue;

	private bool? _lastAc;

	private int _lastBiosInfo3 = -1;

	private int _failures;

	public PowerInputSample? Current => Volatile.Read(in _current);

	public PowerInputSampler(Func<IEcReadTransport?> transport, Func<bool> acOnline, Func<bool> decodeEnabled, Func<long>? tick = null)
	{
		_transport = transport ?? throw new ArgumentNullException("transport");
		_acOnline = acOnline ?? throw new ArgumentNullException("acOnline");
		_decodeEnabled = decodeEnabled ?? throw new ArgumentNullException("decodeEnabled");
		_tick = tick ?? (() => Environment.TickCount64);
	}

	public bool NeedsRefresh(bool force = false)
	{
		if (!force)
		{
			PowerInputSample current = Current;
			if (current != null)
			{
				return SafeTick() - current.TakenTick >= 5000;
			}
		}
		return true;
	}

	public PowerInputSample Refresh(bool force = false)
	{
		lock (_sync)
		{
			long now = SafeTick();
			if (!force)
			{
				PowerInputSample current = _current;
				if (current != null && now - current.TakenTick < 5000)
				{
					return current;
				}
			}
			bool ac = SafeAc();
			bool decodeEnabled = SafeDecodeEnabled();
			int complex = -1;
			int biosInfo3 = -1;
			if (decodeEnabled)
			{
				IEcReadTransport? transport = null;
				try
				{
					transport = _transport();
					if (transport != null)
					{
						complex = transport.ReadByte(1996);
						bool complexChanged = _lastBiosInfo3 < 0 || complex != _lastComplex || _lastAc != ac;
						biosInfo3 = (complex >= 0 && complexChanged) ? transport.ReadByte(1183) : _lastBiosInfo3;
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLineIfChanged("power-input", "Power input EC read failed: " + ex.Message);
					complex = -1;
					biosInfo3 = -1;
				}
				finally
				{
					(transport as IDisposable)?.Dispose();
				}
			}
			if (decodeEnabled && complex < 0)
			{
				_failures++;
				if (_failures >= 3)
				{
					_lastBiosInfo3 = -1;
				}
			}
			else
			{
				_failures = 0;
				if (biosInfo3 >= 0)
				{
					_lastBiosInfo3 = biosInfo3;
				}
			}
			_lastComplex = complex;
			_lastAc = ac;
			PowerInputKind kind = PowerInputDecoder.Decode(ac, complex, decodeEnabled);
			bool isBarrel = kind == PowerInputKind.Barrel || kind == PowerInputKind.BarrelAndTypeC;
			int? watts = isBarrel && PowerInputDecoder.TryDecodeAdapterWatts(_lastBiosInfo3, out var decodedWatts) ? decodedWatts : (int?)null;
			PowerInputSample sample = new PowerInputSample(kind, watts, complex, decodeEnabled ? _lastBiosInfo3 : -1, now, decodeEnabled);
			PowerInputSample? prev = _current;
			if (prev == null || prev.Kind != sample.Kind || prev.ComplexStatusRaw != sample.ComplexStatusRaw)
			{
				Logger.WriteLineIfChanged("power-input", $"Power input: {kind} (ac={ac}, 0x7CC={(complex >= 0 ? "0x" + complex.ToString("X2") : "-")}, 0x49F={(sample.BiosInfo3Raw >= 0 ? "0x" + sample.BiosInfo3Raw.ToString("X2") : "-")}, watts={watts?.ToString() ?? "-"})");
			}
			Volatile.Write(ref _current, sample);
			return sample;
		}
	}

	private long SafeTick()
	{
		try
		{
			return _tick();
		}
		catch
		{
			return Environment.TickCount64;
		}
	}

	private bool SafeAc()
	{
		try
		{
			return _acOnline();
		}
		catch
		{
			return false;
		}
	}

	private bool SafeDecodeEnabled()
	{
		try
		{
			return _decodeEnabled();
		}
		catch
		{
			return false;
		}
	}
}
