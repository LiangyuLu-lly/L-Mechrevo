using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PawnIO;

public sealed class PawnIOWrapper : IDisposable
{
	private enum Ctl : uint
	{
		Load = 2712805508u,
		Execute = 2712805636u
	}

	public enum ConnectResult
	{
		OK,
		NotInstalled,
		AccessDenied,
		OtherError
	}

	private const int FN_LEN = 32;

	private const uint DEV_TYPE = 2712797184u;

	private nint _raw = IntPtr.Zero;

	private SafeFileHandle? _safe;

	private bool _loaded;

	private bool _disposed;

	public bool IsConnected
	{
		get
		{
			if (_raw != IntPtr.Zero)
			{
				return ((IntPtr)_raw).ToInt64() != -1;
			}
			return false;
		}
	}

	public bool IsModuleLoaded
	{
		get
		{
			if (_loaded && _safe != null)
			{
				return !_safe.IsInvalid;
			}
			return false;
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFile(string n, uint acc, uint share, nint sec, uint disp, uint fl, nint tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint h);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint dev, Ctl code, byte[] inB, uint inSz, byte[] outB, uint outSz, out uint ret, nint ovl);

	[DllImport("kernel32.dll")]
	private static extern bool DeviceIoControl(SafeFileHandle dev, Ctl code, [In] byte[] inB, uint inSz, [Out] byte[] outB, uint outSz, out uint ret, nint ovl);

	public ConnectResult Connect()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("PawnIOWrapper");
		}
		if (IsConnected || IsModuleLoaded)
		{
			return ConnectResult.OK;
		}
		_raw = CreateFile("\\\\?\\GLOBALROOT\\Device\\PawnIO", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		if (_raw == IntPtr.Zero || ((IntPtr)_raw).ToInt64() == -1)
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			_raw = IntPtr.Zero;
			switch (lastWin32Error)
			{
			case 2:
			case 3:
				return ConnectResult.NotInstalled;
			case 5:
				return ConnectResult.AccessDenied;
			default:
				return ConnectResult.OtherError;
			}
		}
		return ConnectResult.OK;
	}

	public bool LoadModule(byte[] data)
	{
		if (_disposed || _loaded)
		{
			return false;
		}
		if (!IsConnected || data == null || data.Length == 0)
		{
			return false;
		}
		if (!DeviceIoControl(_raw, Ctl.Load, data, (uint)data.Length, null, 0u, out var _, IntPtr.Zero))
		{
			return false;
		}
		_safe = new SafeFileHandle(_raw, ownsHandle: true);
		_raw = IntPtr.Zero;
		_loaded = true;
		return true;
	}

	public bool Execute(string functionName, ulong[]? input, ulong[]? output)
	{
		if (!IsModuleLoaded)
		{
			return false;
		}
		if (string.IsNullOrEmpty(functionName) || functionName.Length >= 32 || functionName.Any((char c) => c > '\u007f' || c == '\0'))
		{
			throw new ArgumentException("Invalid PawnIO function name.", "functionName");
		}
		byte[] bytes = Encoding.ASCII.GetBytes(functionName);
		int num = input?.Length ?? 0;
		byte[] array = new byte[32 + num * 8];
		Buffer.BlockCopy(bytes, 0, array, 0, Math.Min(bytes.Length, 31));
		if (input != null && num > 0)
		{
			byte[] array2 = new byte[num * 8];
			Buffer.BlockCopy(input, 0, array2, 0, array2.Length);
			Buffer.BlockCopy(array2, 0, array, 32, array2.Length);
		}
		int num2 = output?.Length ?? 0;
		byte[] array3 = ((num2 > 0) ? new byte[num2 * 8] : null);
		if (!DeviceIoControl(_safe, Ctl.Execute, array, (uint)array.Length, array3, (uint)(array3?.Length ?? 0), out var ret, IntPtr.Zero) || !HasCompleteOutput(ret, num2))
		{
			return false;
		}
		if (num2 > 0)
		{
			Buffer.BlockCopy(array3, 0, output, 0, num2 * 8);
		}
		return true;
	}

	internal static bool HasCompleteOutput(uint bytesReturned, int outputCount)
	{
		return bytesReturned == (long)outputCount * 8L;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_loaded = false;
			_safe?.Close();
			if (_raw != IntPtr.Zero && ((IntPtr)_raw).ToInt64() != -1)
			{
				CloseHandle(_raw);
			}
			_disposed = true;
		}
	}
}
