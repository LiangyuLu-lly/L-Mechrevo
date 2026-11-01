using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MechrevoLite.Update;

internal static class UpdatePolicy
{
	private struct WinTrustFileInfo
	{
		public uint cbStruct;

		public nint pcwszFilePath;

		public nint hFile;

		public nint pgKnownSubject;
	}

	private struct WinTrustData
	{
		public uint cbStruct;

		public nint pPolicyCallbackData;

		public nint pSIPClientData;

		public uint dwUIChoice;

		public uint fdwRevocationChecks;

		public uint dwUnionChoice;

		public nint pFile;

		public uint dwStateAction;

		public nint hWVTStateData;

		public nint pwszURLReference;

		public uint dwProvFlags;

		public uint dwUIContext;

		public nint pSignatureSettings;
	}

	internal const long MaxPackageBytes = 419430400L;

	internal static readonly string[] AllowedDownloadHosts = new string[5] { "stats.l-mechrevo.cn", "l-mechrevo.cn", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };

	private static readonly Regex SaneVersionToken = new Regex("^[vV]?[0-9A-Za-z][0-9A-Za-z._+\\-]*$", RegexOptions.Compiled);

	private const int CertQueryObjectFile = 1;

	private const int CertQueryContentPkcs7SignedEmbed = 1024;

	private const int CertQueryFormatAll = 14;

	private const int CertHashPropId = 3;

	private const int X509AsnEncoding = 1;

	private const int Pkcs7AsnEncoding = 65536;

	private const int CertFindAny = 0;

	private const uint WtdUiNone = 2u;

	private const uint WtdChoiceFile = 1u;

	private const uint WtdStateActionVerify = 1u;

	private const uint WtdStateActionClose = 2u;

	private const uint WtdRevocationCheckNone = 16u;

	private const uint WtdCacheOnlyUrlRetrieval = 4096u;

	internal static bool IsValidSha256(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i++)
		{
			if (!Uri.IsHexDigit(value[i]))
			{
				return false;
			}
		}
		return true;
	}

	internal static bool IsSaneVersion(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
		{
			return false;
		}
		string text = version.Trim();
		if (text.Length > 64)
		{
			return false;
		}
		if (!SaneVersionToken.IsMatch(text))
		{
			return false;
		}
		if (!text.Any(char.IsAsciiDigit))
		{
			return false;
		}
		bool flag;
		switch (text[0])
		{
		case '+':
		case '-':
		case '.':
		case '_':
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3;
			switch (text[text.Length - 1])
			{
			case '+':
			case '-':
			case '.':
			case '_':
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag2 = flag3;
		}
		if (flag2)
		{
			return false;
		}
		if (text.Contains(".."))
		{
			return false;
		}
		return true;
	}

	internal static bool IsAcceptableDownloadUri(Uri uri)
	{
		if (!(uri.Scheme == Uri.UriSchemeHttps))
		{
			if (uri.Scheme == Uri.UriSchemeHttp)
			{
				return uri.IsLoopback;
			}
			return false;
		}
		return true;
	}

	internal static bool IsAllowedDownloadHost(Uri uri)
	{
		if (uri.IsLoopback)
		{
			return true;
		}
		string[] allowedDownloadHosts = AllowedDownloadHosts;
		foreach (string b in allowedDownloadHosts)
		{
			if (string.Equals(uri.Host, b, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool TryAcceptDownloadUrl(string? url, out Uri? uri, out string reason)
	{
		uri = null;
		if (string.IsNullOrWhiteSpace(url))
		{
			reason = "服务端没有提供下载地址";
			return false;
		}
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			reason = "下载地址不是合法的 URL";
			return false;
		}
		if (!IsAcceptableDownloadUri(result))
		{
			reason = "下载地址必须使用 HTTPS（当前 " + result.Scheme + "）";
			return false;
		}
		if (!IsAllowedDownloadHost(result))
		{
			reason = "下载地址主机不在允许列表内（" + result.Host + "）";
			return false;
		}
		uri = result;
		reason = "";
		return true;
	}

	internal static bool TryAcceptOffer(UpdateInfo info, out string reason)
	{
		if (!info.UpdateAvailable)
		{
			reason = "没有可用的更新";
			return false;
		}
		if (!IsSaneVersion(info.LatestVersion))
		{
			reason = "服务端版本号不合法";
			return false;
		}
		if (!TryAcceptDownloadUrl(info.DownloadUrl, out Uri _, out reason))
		{
			return false;
		}
		if (!IsValidSha256(info.Sha256))
		{
			reason = "服务端未提供有效的 SHA-256 校验值，已拒绝自动安装";
			return false;
		}
		if (!IsAcceptablePackageSize(info.Size))
		{
			reason = "服务端未提供有效的体积声明，已拒绝自动安装";
			return false;
		}
		reason = "";
		return true;
	}

	internal static bool IsAcceptablePackageSize(long? size)
	{
		if (size.HasValue)
		{
			long valueOrDefault = size.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				return valueOrDefault <= 419430400;
			}
		}
		return false;
	}

	internal static bool MatchesDeclaredSize(long actualLength, long? declared)
	{
		if (IsAcceptablePackageSize(declared))
		{
			return declared == actualLength;
		}
		return false;
	}

	internal static bool TryAcceptAuthenticode(string path, out string reason)
	{
		reason = "更新包没有有效的 Authenticode 签名，拒绝安装";
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}
		try
		{
			if (!WinVerifyTrustFile(path))
			{
				return false;
			}
			if (!TryGetCertHash(path, out byte[] hash) || hash.Length == 0)
			{
				return false;
			}
			reason = "";
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	[DllImport("wintrust.dll", ExactSpelling = true)]
	private unsafe static extern int WinVerifyTrust(nint hwnd, Guid* pgActionID, WinTrustData* pWvtData);

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CryptQueryObject(int dwObjectType, [MarshalAs(UnmanagedType.LPWStr)] string pvObject, int dwExpectedContentTypeFlags, int dwExpectedFormatTypeFlags, int dwFlags, out int pdwMsgAndCertEncodingType, out int pdwContentType, out int pdwFormatType, out nint phCertStore, out nint phMsg, out nint ppvContext);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CertFreeCertificateContext(nint pCertContext);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CertCloseStore(nint hCertStore, int dwFlags);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CryptMsgClose(nint hCryptMsg);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern nint CertFindCertificateInStore(nint hCertStore, int dwCertEncodingType, int dwFindFlags, int dwFindType, nint pvFindPara, nint pPrevCertContext);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CertGetCertificateContextProperty(nint pCertContext, int dwPropId, byte[]? pvData, ref int pcbData);

	private unsafe static bool WinVerifyTrustFile(string path)
	{
		Guid guid = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
		fixed (char* pcwszFilePath = path)
		{
			WinTrustFileInfo winTrustFileInfo = new WinTrustFileInfo
			{
				cbStruct = (uint)sizeof(WinTrustFileInfo),
				pcwszFilePath = (nint)pcwszFilePath
			};
			WinTrustData winTrustData = new WinTrustData
			{
				cbStruct = (uint)sizeof(WinTrustData),
				dwUIChoice = 2u,
				dwUnionChoice = 1u,
				pFile = (nint)(&winTrustFileInfo),
				dwStateAction = 1u,
				dwProvFlags = 4112u
			};
			int num = WinVerifyTrust(IntPtr.Zero, &guid, &winTrustData);
			winTrustData.dwStateAction = 2u;
			WinVerifyTrust(IntPtr.Zero, &guid, &winTrustData);
			return num == 0;
		}
	}

	private static bool TryGetCertHash(string path, out byte[] hash)
	{
		hash = Array.Empty<byte>();
		if (!CryptQueryObject(1, path, 1024, 14, 0, out var pdwMsgAndCertEncodingType, out var _, out var _, out var phCertStore, out var phMsg, out var ppvContext))
		{
			return false;
		}
		try
		{
			if (ppvContext == IntPtr.Zero && phCertStore != IntPtr.Zero)
			{
				ppvContext = CertFindCertificateInStore(phCertStore, (pdwMsgAndCertEncodingType != 0) ? pdwMsgAndCertEncodingType : 65537, 0, 0, IntPtr.Zero, IntPtr.Zero);
			}
			if (ppvContext == IntPtr.Zero)
			{
				return false;
			}
			int pcbData = 0;
			CertGetCertificateContextProperty(ppvContext, 3, null, ref pcbData);
			if (pcbData <= 0)
			{
				return false;
			}
			hash = new byte[pcbData];
			return CertGetCertificateContextProperty(ppvContext, 3, hash, ref pcbData) && hash.Length != 0;
		}
		finally
		{
			if (ppvContext != IntPtr.Zero)
			{
				CertFreeCertificateContext(ppvContext);
			}
			if (phMsg != IntPtr.Zero)
			{
				CryptMsgClose(phMsg);
			}
			if (phCertStore != IntPtr.Zero)
			{
				CertCloseStore(phCertStore, 0);
			}
		}
	}
}
