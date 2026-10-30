namespace MechrevoLite.Update;

internal sealed record UpdateInfo(string CurrentVersion, string? LatestVersion, string Channel, bool ChannelFallback, bool UpdateAvailable, string? ReleaseDate, string? Notes, string? FileName, long? Size, string? Sha256, string? DownloadUrl, string? DownloadPage, string? ServerCurrentVersion = null, string? ExternalGcuName = null, string? ExternalGcuUrl = null, long? ExternalGcuSize = null, string? ExternalGcuSha256 = null)
{
	internal bool HasVerifiablePackage
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(DownloadUrl))
			{
				return UpdatePolicy.IsValidSha256(Sha256);
			}
			return false;
		}
	}
}
