using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MechrevoLite.Usage;

// 2026-10-06 step8 (upstream 0.290.6 A-2，本地加固):
// 待上传日志批次的磁盘 Outbox。
//  - 原子落盘：先写 .tmp → Flush(flushToDisk:true) → File.Move
//  - 容量上限默认 32 批（键 telemetry_outbox_max_batches，可配置 8~128），超限时按文件名序（= 时间序）删最旧
//  - 读回时校验字段/尺寸，损坏条目隔离为 *.invalid，不阻塞后面的批次
//  - 落盘失败向上抛出，由 UsageTelemetry.PersistLog 记 "usage log backlog write failed"（可观测）
internal sealed class TelemetryLogOutbox
{
	// 上游常量：32 批。这里保留同名常量，实际上限由 AppConfig 覆盖。
	internal const int MaxBatches = 32;

	internal const int MinBatches = 8;

	internal const int AbsoluteMaxBatches = 128;

	// 上游单批上限 131072 B（序列化后）；这里放宽到 196608 留出 JSON 包装余量再判。
	internal const int MaxEntryBytes = 196608;

	// 隔离的坏条目最多保留这么多，再坏的按最旧淘汰，避免 *.invalid 无限堆积吃满磁盘。
	private const int MaxQuarantined = 16;

	private readonly object _gate = new object();

	private readonly string _directory;

	internal TelemetryLogOutbox(string directory)
	{
		_directory = directory;
	}

	internal string Directory => _directory;

	// 配置键 telemetry_outbox_max_batches（沿用本仓库 "telemetry_*" 小写下划线风格），默认 32。
	internal static int BatchLimit
	{
		get
		{
			int num = AppConfig.Get("telemetry_outbox_max_batches", MaxBatches);
			return Math.Clamp(num, MinBatches, AbsoluteMaxBatches);
		}
	}

	internal string Enqueue(TelemetryLogBatch batch)
	{
		lock (_gate)
		{
			System.IO.Directory.CreateDirectory(_directory);
			string text = Path.Combine(_directory, $"{DateTime.UtcNow.Ticks:D19}-{batch.Batch}.json");
			string text2 = text + ".tmp";
			try
			{
				using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					JsonSerializer.Serialize(fileStream, batch, UsageTelemetry.WireJson);
					fileStream.Flush(flushToDisk: true);
				}
				File.Move(text2, text);
			}
			finally
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			TrimToLimit();
			return text;
		}
	}

	internal (string Path, TelemetryLogBatch Batch)? Peek()
	{
		lock (_gate)
		{
			string[] array = Files();
			foreach (string text in array)
			{
				try
				{
					if (new FileInfo(text).Length > MaxEntryBytes)
					{
						throw new InvalidDataException("oversized outbox entry");
					}
					TelemetryLogBatch telemetryLogBatch = JsonSerializer.Deserialize<TelemetryLogBatch>(File.ReadAllText(text, Encoding.UTF8));
					if ((object)telemetryLogBatch == null || string.IsNullOrEmpty(telemetryLogBatch.Batch) || telemetryLogBatch.Batch.Length != 32 || string.IsNullOrEmpty(telemetryLogBatch.Id) || telemetryLogBatch.Id.Length < 8 || telemetryLogBatch.Ver == null || telemetryLogBatch.Log == null || Encoding.UTF8.GetByteCount(telemetryLogBatch.Log) > UsageTelemetry.MaxLogBytes)
					{
						throw new InvalidDataException("invalid outbox entry");
					}
					return (text, telemetryLogBatch);
				}
				catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
				{
					Quarantine(text);
					Logger.WriteInfo("usage log backlog entry quarantined: " + ex.Message);
				}
			}
			return null;
		}
	}

	internal void Acknowledge(string path)
	{
		lock (_gate)
		{
			File.Delete(path);
		}
	}

	internal void Clear()
	{
		lock (_gate)
		{
			if (!System.IO.Directory.Exists(_directory))
			{
				return;
			}
			string[] files = System.IO.Directory.GetFiles(_directory);
			for (int i = 0; i < files.Length; i++)
			{
				File.Delete(files[i]);
			}
		}
	}

	// 数量上限 + 明确淘汰策略：只数 *.json；超过上限就从文件名序开头（= 最旧）删。
	internal int TrimToLimit()
	{
		int num = BatchLimit;
		string[] array = Files();
		int num2 = Math.Max(0, array.Length - num);
		for (int i = 0; i < num2; i++)
		{
			File.Delete(array[i]);
			Logger.WriteInfo("usage log backlog capacity reached; the oldest snapshot expired");
		}
		TrimQuarantine();
		return array.Length - num2;
	}

	private void Quarantine(string path)
	{
		try
		{
			File.Move(path, path + ".invalid", overwrite: true);
		}
		catch (Exception ex)
		{
			Logger.WriteInfo("usage log backlog quarantine failed: " + ex.Message);
		}
	}

	private void TrimQuarantine()
	{
		try
		{
			if (!System.IO.Directory.Exists(_directory))
			{
				return;
			}
			string[] array = System.IO.Directory.GetFiles(_directory, "*.invalid").Order(StringComparer.Ordinal).ToArray();
			for (int i = 0; i < Math.Max(0, array.Length - MaxQuarantined); i++)
			{
				File.Delete(array[i]);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteInfo("usage log backlog quarantine cleanup failed: " + ex.Message);
		}
	}

	// 诊断用（不参与上传逻辑）。
	internal int PendingCount()
	{
		lock (_gate)
		{
			return Files().Length;
		}
	}

	private string[] Files()
	{
		if (!System.IO.Directory.Exists(_directory))
		{
			return Array.Empty<string>();
		}
		return System.IO.Directory.GetFiles(_directory, "*.json").Order(StringComparer.Ordinal).ToArray();
	}
}
