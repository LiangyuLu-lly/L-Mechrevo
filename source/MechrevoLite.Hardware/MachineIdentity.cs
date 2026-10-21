using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>
/// 机型事实集（只读、带来源、可审计）。
/// <para>第一层「机型识别」只产出事实，不做路由；路由见 <see cref="BackendRouter" />。</para>
/// <para>每个事实失败都退化成 Unknown/空，不抛、不猜、不影响后端探测本身。</para>
/// </summary>
internal sealed record MachineIdentity(
	string Board,
	string BoardSource,
	bool BaolongdaWhitelisted,
	string GpuGeneration,
	bool GcuServiceInstalled,
	string Summary)
{
	internal static MachineIdentity Collect()
	{
		string board = string.Empty;
		string boardSource = "unknown";
		bool whitelisted = false;
		try
		{
			BoardWhitelist.BoardProductEvidence evidence = BoardWhitelist.GetBoardProductEvidence();
			board = evidence.Product ?? string.Empty;
			boardSource = string.IsNullOrEmpty(evidence.Source) ? "unknown" : evidence.Source;
			whitelisted = BoardWhitelist.Matches(board);
		}
		catch
		{
		}

		string generation = "Unknown";
		try
		{
			generation = GpuGenerationProvider.Current().Generation.ToString();
		}
		catch
		{
		}

		bool gcuInstalled = false;
		try
		{
			gcuInstalled = MechrevoHw.IsGcuServiceInstalled;
		}
		catch
		{
		}

		string summary = "board=" + (board.Length == 0 ? "?" : board) + "(" + boardSource + ")"
			+ " · whitelist=" + whitelisted
			+ " · gen=" + generation
			+ " · gcuInstalled=" + gcuInstalled;
		return new MachineIdentity(board, boardSource, whitelisted, generation, gcuInstalled, summary);
	}
}
