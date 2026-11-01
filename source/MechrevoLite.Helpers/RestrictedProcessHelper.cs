using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MechrevoLite.Helpers;

public static class RestrictedProcessHelper
{
	private struct PROCESS_INFORMATION
	{
		public nint hProcess;

		public nint hThread;

		public int dwProcessId;

		public int dwThreadId;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct STARTUPINFO
	{
		public int cb;

		public string lpReserved;

		public string lpDesktop;

		public string lpTitle;

		public int dwX;

		public int dwY;

		public int dwXSize;

		public int dwYSize;

		public int dwXCountChars;

		public int dwYCountChars;

		public int dwFillAttribute;

		public int dwFlags;

		public short wShowWindow;

		public short cbReserved2;

		public nint lpReserved2;

		public nint hStdInput;

		public nint hStdOutput;

		public nint hStdError;
	}

	private struct TOKEN_MANDATORY_LABEL
	{
		public nint Sid;

		public uint Attributes;
	}

	private const uint SAFER_SCOPEID_USER = 2u;

	private const uint SAFER_LEVELID_NORMALUSER = 131072u;

	private const uint SAFER_LEVEL_OPEN = 1u;

	private const int TokenIntegrityLevel = 25;

	private const int TokenElevation = 20;

	private const uint SE_GROUP_INTEGRITY = 32u;

	public static void RunAsRestrictedUser(string command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
		string text = Path.Combine(folderPath, "cmd.exe");
		StringBuilder stringBuilder = new StringBuilder().Append('"').Append(text).Append("\" /C ")
			.Append(command);
		Logger.WriteLine($"Launching {stringBuilder}");
		if (!ProcessHelper.IsUserAdministrator())
		{
			Process.Start(new ProcessStartInfo(text, "/C " + command)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = folderPath
			})?.Dispose();
			return;
		}
		STARTUPINFO lpStartupInfo = default;
		lpStartupInfo.cb = Marshal.SizeOf(lpStartupInfo);
		lpStartupInfo.dwFlags = 1;
		lpStartupInfo.wShowWindow = 0;
		if (GetShellUserToken(out var token))
		{
			try
			{
				if (CreateProcessWithTokenW(token, 0u, null, stringBuilder, 0u, IntPtr.Zero, folderPath, ref lpStartupInfo, out var lpProcessInformation))
				{
					CloseHandle(lpProcessInformation.hProcess);
					CloseHandle(lpProcessInformation.hThread);
					return;
				}
				Logger.WriteLine($"CreateProcessWithTokenW failed: {Marshal.GetLastWin32Error()}");
			}
			finally
			{
				CloseHandle(token);
			}
		}
		if (!GetRestrictedSessionUserToken(out token))
		{
			return;
		}
		try
		{
			if (CreateProcessAsUser(token, null, stringBuilder, IntPtr.Zero, IntPtr.Zero, bInheritHandles: false, 0u, IntPtr.Zero, folderPath, ref lpStartupInfo, out var lpProcessInformation2))
			{
				CloseHandle(lpProcessInformation2.hProcess);
				CloseHandle(lpProcessInformation2.hThread);
			}
		}
		finally
		{
			CloseHandle(token);
		}
	}

	private static bool GetShellUserToken(out nint token)
	{
		token = IntPtr.Zero;
		GetWindowThreadProcessId(GetShellWindow(), out var lpdwProcessId);
		if (lpdwProcessId == 0)
		{
			return false;
		}
		nint num = OpenProcess(4096u, bInheritHandle: false, lpdwProcessId);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			if (!OpenProcessToken(num, 2u, out var TokenHandle))
			{
				return false;
			}
			try
			{
				if (!DuplicateTokenEx(TokenHandle, 395u, IntPtr.Zero, 2, 1, out token))
				{
					return false;
				}
				if (GetTokenInformation(token, 20, out var TokenInformation, 4u, out var _) && TokenInformation == 0)
				{
					return true;
				}
				CloseHandle(token);
				token = IntPtr.Zero;
				return false;
			}
			finally
			{
				CloseHandle(TokenHandle);
			}
		}
		finally
		{
			CloseHandle(num);
		}
	}

	private static bool GetRestrictedSessionUserToken(out nint token)
	{
		token = IntPtr.Zero;
		if (!SaferCreateLevel(2u, 131072u, 1u, out var pLevelHandle, IntPtr.Zero))
		{
			return false;
		}
		TOKEN_MANDATORY_LABEL TokenInformation = new TOKEN_MANDATORY_LABEL
		{
			Attributes = 32u
		};
		try
		{
			if (!SaferComputeTokenFromLevel(pLevelHandle, IntPtr.Zero, out token, 0, IntPtr.Zero))
			{
				return false;
			}
			if (!ConvertStringSidToSid("S-1-16-8192", out TokenInformation.Sid) || !SetTokenInformation(token, 25, ref TokenInformation, (uint)Marshal.SizeOf(TokenInformation)))
			{
				CloseHandle(token);
				token = IntPtr.Zero;
				return false;
			}
			return true;
		}
		finally
		{
			SaferCloseLevel(pLevelHandle);
			if (TokenInformation.Sid != IntPtr.Zero)
			{
				LocalFree(TokenInformation.Sid);
			}
		}
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool SaferCreateLevel(uint scope, uint level, uint openFlags, out nint pLevelHandle, nint lpReserved);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool SaferComputeTokenFromLevel(nint LevelHandle, nint InAccessToken, out nint OutAccessToken, int dwFlags, nint lpReserved);

	[DllImport("advapi32.dll")]
	private static extern bool SaferCloseLevel(nint hLevelHandle);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool ConvertStringSidToSid(string StringSid, out nint ptrSid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool SetTokenInformation(nint TokenHandle, int TokenInformationClass, ref TOKEN_MANDATORY_LABEL TokenInformation, uint TokenInformationLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(nint TokenHandle, int TokenInformationClass, out int TokenInformation, uint TokenInformationLength, out uint ReturnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, out nint TokenHandle);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool DuplicateTokenEx(nint hExistingToken, uint dwDesiredAccess, nint lpTokenAttributes, int ImpersonationLevel, int TokenType, out nint phNewToken);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateProcessAsUser(nint hToken, string? lpApplicationName, StringBuilder? lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateProcessWithTokenW(nint hToken, uint dwLogonFlags, string? lpApplicationName, StringBuilder? lpCommandLine, uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint hObject);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint hMem);

	[DllImport("user32.dll")]
	private static extern nint GetShellWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
}
