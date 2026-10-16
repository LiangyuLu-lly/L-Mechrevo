namespace PawnIO;

public enum SmuStatus : uint
{
	OK = 1u,
	Failed = 255u,
	UnknownCmd = 254u,
	CmdRejectedPrereq = 253u,
	CmdRejectedBusy = 252u
}
