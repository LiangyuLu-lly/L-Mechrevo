namespace MechrevoLite.Usage;

// 2026-10-06 step8 (upstream 0.290.6 A-2/A-7): 一批待上传日志。
// Batch 是 GUID（N 格式，32 位）——服务端按它去重；CrashStamp>0 表示这批带崩溃现场证据。
internal sealed record TelemetryLogBatch(string Id, string Ver, string Batch, string Log, long CrashStamp, int Errors);
