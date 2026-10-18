// 来源：_upstream\0.290.6\decomp\MechrevoLite.Hardware\LegacyMifsWmi.cs（作者 0.290.6 实现，语义照抄）。
// 用途：老机型（无 GCU 的新通路）通过 WMI 类 root\WMI:MICommonInterface 读写**内置性能档**：
//   读 = MiInterface(InData[1]=250) 后解析 OutData；写 = MiInterface(InData[1]=251) 后再回读确认。
//   档位映射沿用作者口径：内部 0/1/2 <-> MIFS 2/0/1，回读 1 与 3 都算档位 2。
// 许可/署名：与上游同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。
// ⚠️ 2026-10-15 C-1 第 1 步：纯增量底座。
// ✅ 2026-10-15 C-1 第 2 步（本轮）：已接线 —— 由 MechrevoBackendOrder.Probe() 按有序候选表探测，
//    探中即 ActiveBackend=LegacyMifs（见 MechrevoHw.ConnectAsync / ApplyLegacyBackendAsync）。
//   反编译残留写法（((Func<bool>)delegate {...}).Invoke()、显式 new char[2]{...}）已改写为正常 C#，语义不变。

using System;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

internal sealed class LegacyMifsWmi : IDisposable
{
	// ⭐ 2026-10-16（0.291.9 · 宝龙达适配第 2 步）：⭐ 可见性从 private 放宽到 internal ——
	//    ⭐ 仅供 `BaolongdaBackend` 做**只读对比诊断**（⭐ 看宝龙达机型的类 GUID 跟这份是否相同，
	//    ⭐ 以解释"为什么 LegacyMifsWmi 可能探不到"）✅
	//    ⚠️ 只是编译期可见性，⛔ **不改变任何运行时行为**（⭐ 值一字未动 ✅）。
	internal const string InterfaceGuid = "B60BFB48-3E5B-49E4-A0E9-8CFFE1B3434B";

	private readonly ManagementObject _instance;

	private readonly object _gate = new object();

	private long _version;

	private bool _disposed;

	internal bool Available { get; private set; }

	private LegacyMifsWmi(ManagementObject instance)
	{
		_instance = instance;
	}

	// ⚠️ 返回类型不写 LegacyMifsWmi? 注解：本工程未开 nullable（写了会新增 CS8632 警告），
	//    与本树既有约定一致（见 GcuVariantFamily.cs 同类说明）。运行时语义完全相同。
	internal static LegacyMifsWmi TryCreate()
	{
		LegacyMifsWmi legacyMifsWmi = null;
		try
		{
			using ManagementClass managementClass = new ManagementClass("root\\WMI:MICommonInterface");
			managementClass.Get();
			if (!string.Equals(managementClass.Qualifiers["Guid"].Value?.ToString()?.Trim('{', '}') ?? "", InterfaceGuid, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			using ManagementObjectCollection managementObjectCollection = managementClass.GetInstances(new System.Management.EnumerationOptions
			{
				Timeout = TimeSpan.FromSeconds(2L)
			});
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				legacyMifsWmi = new LegacyMifsWmi((ManagementObject)managementObjectEnumerator.Current);
				legacyMifsWmi.ReadMode();
				return legacyMifsWmi;
			}
		}
		catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
		{
			legacyMifsWmi?.Dispose();
		}
		catch (Exception ex2)
		{
			legacyMifsWmi?.Dispose();
			Logger.WriteLine("Legacy MIFS detection failed: " + ex2.Message);
		}
		return null;
	}

	internal static byte[] BuildModeRequest(bool write, int operatingMode = 1)
	{
		byte b = operatingMode switch
		{
			0 => (byte)2, 
			1 => (byte)0, 
			2 => (byte)1, 
			_ => throw new ArgumentOutOfRangeException("operatingMode"), 
		};
		byte[] array = new byte[32];
		array[1] = (byte)(write ? 251 : 250);
		array[3] = 8;
		if (write)
		{
			array[4] = b;
		}
		return array;
	}

	internal static int ReadOperatingMode(byte[] output)
	{
		// ⭐ 2026-10-17：先取出**原始值**再映射 —— 这样"未知值"能如实打进日志（⭐ 见下 default 分支）。
		int index = output.Length switch
		{
			30 => 2, 
			32 => 4, 
			_ => throw LogAndThrowUnknownLength(output.Length), 
		};
		byte raw = output[index];
		switch (raw)
		{
		case 0:
			return 1;
		case 1:
		case 3:
			return 2;
		case 2:
			return 0;
		default:
			// ⭐⭐ 2026-10-17 真机排障（蛟龙16 · 6800H＋3070Ti · 板号 `MRID6`）：
			//   该机 `SwitchMode(1)`/`SwitchMode(2)` 恒 Failed 而 `SwitchMode(0)` 成功，
			//   且 A 面灯**确实变色** ⇒ ⭐ 说明指令下发成功、**只是回读值不在已知映射里** ⚠️
			//   ⇒ ⭐ 旧代码在这里**静默抛异常**，日志里看不出"读到的是几" ❌
			//   ⇒ ⭐ 现在如实打出原始值，下一份诊断包就能判断"是固件不认"还是"该机型映射不同" ✅
			Logger.WriteError($"老接口 MIFS：读到**未知档位原始值 {raw}**（⭐ 响应长度={output.Length} · 取值下标={index}"
				+ " ⇒ ⭐ 现有映射只覆盖原始 0/1/2/3 ⇒ ⭐ 该机型的原始→内部映射可能与 40 系不同 ⚠️）");
			throw new IOException($"Unknown MIFS performance mode (raw={raw}).");
		}
	}

	/// <summary>
	/// ⭐ 2026-10-17：响应长度不在已知两档（30 / 32）时 —— ⭐ **也必须留下可见证据** ⚠️
	/// （⭐ 旧代码只抛一句"Invalid MIFS response length"、不带实际长度 ⇒ ⭐ 排障时看不出"到底回了多少字节" ❌）。
	/// </summary>
	private static IOException LogAndThrowUnknownLength(int actualLength)
	{
		Logger.WriteError($"老接口 MIFS：**未知响应长度 {actualLength} 字节**（⭐ 已知只有 30 / 32 两档"
			+ " ⇒ ⭐ 该机型的 MIFS 响应帧可能与本树假设不同 ⚠️）");
		return new IOException($"Invalid MIFS response length ({actualLength}).");
	}

	internal LegacyMifsSnapshot ReadMode()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			try
			{
				using ManagementBaseObject managementBaseObject = _instance.GetMethodParameters("MiInterface");
				managementBaseObject["InData"] = BuildModeRequest(write: false);
				using ManagementBaseObject managementBaseObject2 = _instance.InvokeMethod("MiInterface", managementBaseObject, new InvokeMethodOptions
				{
					Timeout = TimeSpan.FromSeconds(2L)
				});
				int operatingMode = ReadOperatingMode((managementBaseObject2["OutData"] as byte[]) ?? throw new IOException("MIFS did not return OutData."));
				Available = true;
				return new LegacyMifsSnapshot(operatingMode, ++_version);
			}
			catch
			{
				Available = false;
				throw;
			}
		}
	}

	internal async Task<bool> SwitchModeAsync(int operatingMode, CancellationToken ct)
	{
		byte[] request = BuildModeRequest(write: true, operatingMode);
		await Task.Run(() =>
		{
			lock (_gate)
			{
				ct.ThrowIfCancellationRequested();
				ObjectDisposedException.ThrowIf(_disposed, this);
				using ManagementBaseObject managementBaseObject = _instance.GetMethodParameters("MiInterface");
				managementBaseObject["InData"] = request;
				using (_instance.InvokeMethod("MiInterface", managementBaseObject, new InvokeMethodOptions
				{
					Timeout = TimeSpan.FromSeconds(2L)
				}))
				{
				}
			}
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		int lastSeen = -1;
		for (int attempt = 0; attempt < 5; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			lastSeen = (await Task.Run((Func<LegacyMifsSnapshot>)ReadMode, ct).ConfigureAwait(continueOnCapturedContext: false)).OperatingMode;
			if (lastSeen == operatingMode)
			{
				return true;
			}
			await Task.Delay(150, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		// ⭐ 2026-10-17：如实记下「写了什么 / 回读是什么」—— ⭐ 这是区分
		//   「固件没接受这个档」与「该机型原始→内部映射不同」的唯一证据 ✅
		//   （⚠️ 真机依据：蛟龙16 `MRID6` 上 0 成功、1/2 恒失败，而 A 面灯确实变色 ⇒ 多半是后者 ⚠️）
		Logger.WriteError($"老接口 MIFS：写档位内部值 {operatingMode} ⇒ 5 次回读仍是 {lastSeen}（每次间隔 150ms）"
			+ " ⇒ ⭐ 如实判为【未确认】（⚠️ 不是「写失败」：⛔ 本判据只证明回读不一致 ⚠️）");
		return false;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			Available = false;
			_instance.Dispose();
		}
	}
}
