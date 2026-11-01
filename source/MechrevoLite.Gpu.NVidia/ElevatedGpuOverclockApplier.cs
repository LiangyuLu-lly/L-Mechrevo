using System;
using MechrevoLite.Properties;using System.ComponentModel;
using MechrevoLite.Properties;using System.Diagnostics;
using MechrevoLite.Properties;using System.IO;
using MechrevoLite.Properties;using System.IO.Pipes;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Security.AccessControl;
using MechrevoLite.Properties;using System.Security.Principal;
using MechrevoLite.Properties;using System.Text.Json;
using MechrevoLite.Properties;using System.Threading;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.Helpers;
using MechrevoLite.Properties;
namespace MechrevoLite.Gpu.NVidia;

public sealed class ElevatedGpuOverclockApplier : IGpuOverclockElevatedApplier, IDisposable
{
	private const string PipePrefix = "LMechrevoGpuOc-";

	private const int TokenHexLength = 32;

	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30L);

	private static readonly TimeSpan ElevationFailureCooldown = TimeSpan.FromSeconds(45L);

	private static readonly TimeSpan ElevationRefusalCooldown = TimeSpan.FromMinutes(2L);

	internal static readonly TimeSpan HelperIdleTimeout = TimeSpan.FromMinutes(5L);

	private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

	private string? _pipeName;

	private string? _token;

	private Process? _helper;

	private DateTimeOffset _elevationBlockedUntil = DateTimeOffset.MinValue;

	private bool _disposed;

	public async Task<GpuOverclockApplyResult> ApplyAsync(GpuOverclockApplyRequest request, CancellationToken cancellationToken)
	{
		await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (_pipeName != null && _token != null)
			{
				GpuOverclockApplyResult gpuOverclockApplyResult = await TrySendAsync(_pipeName, request with
				{
					Token = _token
				}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)gpuOverclockApplyResult != null)
				{
					return gpuOverclockApplyResult;
				}
				StopHelper();
			}
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			if (!ShouldPromptForElevation(utcNow, _elevationBlockedUntil))
			{
				return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteHelperLaunchFailed);
			}
			SecurityIdentifier securityIdentifier = TryGetCurrentUserSid();
			if ((object)securityIdentifier == null)
			{
				return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuCannotResolveSid);
			}
			string pipeName = "LMechrevoGpuOc-" + Guid.NewGuid().ToString("N");
			string token = Guid.NewGuid().ToString("N");
			if (!TryStartHelper(pipeName, token, securityIdentifier, out Process helper, out bool userRefused))
			{
				_elevationBlockedUntil = utcNow + (userRefused ? ElevationRefusalCooldown : ElevationFailureCooldown);
				return new GpuOverclockApplyResult(Success: false, 0, 0, userRefused ? Strings.GpuElevationCancelledNoWrite : Strings.GpuWriteHelperCannotLaunch);
			}
			GpuOverclockApplyResult gpuOverclockApplyResult2 = await TrySendAsync(pipeName, request with
			{
				Token = token
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if ((object)gpuOverclockApplyResult2 == null)
			{
				KillHelper(helper);
				_elevationBlockedUntil = DateTimeOffset.UtcNow + ElevationFailureCooldown;
				return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteHelperTimeout);
			}
			_pipeName = pipeName;
			_token = token;
			_helper = helper;
			if (gpuOverclockApplyResult2.Success)
			{
				_elevationBlockedUntil = DateTimeOffset.MinValue;
			}
			return gpuOverclockApplyResult2;
		}
		finally
		{
			_requestLock.Release();
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			StopHelper();
			_requestLock.Dispose();
		}
	}

	private void StopHelper()
	{
		KillHelper(_helper);
		_helper = null;
		_pipeName = null;
		_token = null;
	}

	private static void KillHelper(Process? helper)
	{
		if (helper == null)
		{
			return;
		}
		try
		{
			if (!helper.HasExited)
			{
				helper.Kill();
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't stop the GPU OC helper: " + ex.Message);
		}
		finally
		{
			helper.Dispose();
		}
	}

	private static SecurityIdentifier? TryGetCurrentUserSid()
	{
		try
		{
			using WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();
			return windowsIdentity.User;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't read the current user SID: " + ex.Message);
			return null;
		}
	}

	private static bool TryStartHelper(string pipeName, string token, SecurityIdentifier clientSid, out Process? helper, out bool userRefused)
	{
		helper = null;
		userRefused = false;
		try
		{
			if (!ExecutableTrust.IsCurrentImageInProtectedLocation(out string reason))
			{
				Logger.WriteLineThrottled("gpu-oc-helper-trust", "GPU OC helper is being elevated from a user-writable location: " + reason, 60000);
			}
			helper = Process.Start(new ProcessStartInfo
			{
				FileName = Application.ExecutablePath,
				Arguments = $"--gpu-oc-helper {pipeName} {token} {clientSid.Value}",
				WorkingDirectory = AppContext.BaseDirectory,
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			});
			return helper != null;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			Logger.WriteLine("GPU OC elevation was cancelled by the user.");
			userRefused = true;
			return false;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("GPU OC helper start failed: " + ex2.Message);
			return false;
		}
	}

	internal static bool ShouldPromptForElevation(DateTimeOffset now, DateTimeOffset blockedUntil)
	{
		return now >= blockedUntil;
	}

	internal static PipeSecurity CreatePipeSecurityForCurrentUser()
	{
		return CreatePipeSecurityForClient(WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("当前用户 SID 不可用。"));
	}

	internal static PipeSecurity CreatePipeSecurityForClient(SecurityIdentifier clientSid)
	{
		PipeSecurity pipeSecurity = new PipeSecurity();
		pipeSecurity.SetAccessRule(new PipeAccessRule(clientSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
		pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
		return pipeSecurity;
	}

	internal static NamedPipeServerStream CreatePipeServer(string pipeName)
	{
		return CreatePipeServer(pipeName, CreatePipeSecurityForCurrentUser());
	}

	internal static NamedPipeServerStream CreatePipeServer(string pipeName, PipeSecurity security)
	{
		return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
	}

	internal static NamedPipeClientStream CreateClientPipe(string pipeName)
	{
		return new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
	}

	private static async Task<GpuOverclockApplyResult?> TrySendAsync(string pipeName, GpuOverclockApplyRequest request, CancellationToken cancellationToken)
	{
		_ = 2;
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(ConnectTimeout);
			using NamedPipeClientStream pipe = CreateClientPipe(pipeName);
			await pipe.ConnectAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			using StreamWriter writer = new StreamWriter(pipe, null, -1, leaveOpen: true)
			{
				AutoFlush = true
			};
			using StreamReader reader = new StreamReader(pipe, null, detectEncodingFromByteOrderMarks: true, -1, leaveOpen: true);
			await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(continueOnCapturedContext: false);
			string text = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			return (text == null) ? null : JsonSerializer.Deserialize<GpuOverclockApplyResult>(text);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex2)
		{
			Logger.WriteLineThrottled("gpu-oc-helper-connect", "GPU OC helper connection failed: " + ex2.Message);
			return null;
		}
	}

	internal static int Run(string[] args)
	{
		if (args.Length != 3)
		{
			return 2;
		}
		if (!IsValidPipeName(args[0]))
		{
			return 2;
		}
		if (!IsValidToken(args[1]))
		{
			return 2;
		}
		SecurityIdentifier expectedClient;
		try
		{
			expectedClient = new SecurityIdentifier(args[2]);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU OC helper received an invalid client SID: " + ex.Message);
			return 2;
		}
		return RunAsync(args[0], args[1], expectedClient).GetAwaiter().GetResult();
	}

	private static bool IsValidPipeName(string pipeName)
	{
		if (pipeName.StartsWith("LMechrevoGpuOc-", StringComparison.Ordinal) && pipeName.Length == "LMechrevoGpuOc-".Length + 32)
		{
			return pipeName.Skip("LMechrevoGpuOc-".Length).All(char.IsAsciiHexDigit);
		}
		return false;
	}

	private static bool IsValidToken(string token)
	{
		if (token.Length == 32)
		{
			return token.All(char.IsAsciiHexDigit);
		}
		return false;
	}

	internal static bool TokenMatches(string? expected, string? provided)
	{
		if (expected == null || provided == null)
		{
			return false;
		}
		if (expected.Length != provided.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < expected.Length; i++)
		{
			num |= expected[i] ^ provided[i];
		}
		return num == 0;
	}

	private static async Task<int> RunAsync(string pipeName, string token, SecurityIdentifier expectedClient)
	{
		PipeSecurity security = CreatePipeSecurityForClient(expectedClient);
		while (true)
		{
			using NamedPipeServerStream pipe = CreatePipeServer(pipeName, security);
			using CancellationTokenSource idleTimeout = new CancellationTokenSource(HelperIdleTimeout);
			try
			{
				await pipe.WaitForConnectionAsync(idleTimeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				return 0;
			}
			try
			{
				if (!ClientSidMatches(pipe, expectedClient))
				{
					Logger.WriteLine("GPU OC helper rejected a connection from an unexpected identity.");
					continue;
				}
				using StreamReader reader = new StreamReader(pipe, null, detectEncodingFromByteOrderMarks: true, -1, leaveOpen: true);
				using StreamWriter writer = new StreamWriter(pipe, null, -1, leaveOpen: true)
				{
					AutoFlush = true
				};
				GpuOverclockApplyResult value = ResolveRequest(await reader.ReadLineAsync(idleTimeout.Token).ConfigureAwait(continueOnCapturedContext: false), token);
				await writer.WriteLineAsync(JsonSerializer.Serialize(value)).ConfigureAwait(continueOnCapturedContext: false);
				await writer.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					pipe.WaitForPipeDrain();
				}
				catch (Exception ex2)
				{
					Logger.WriteLine("GPU OC helper drain failed: " + ex2.Message);
				}
			}
			catch (Exception ex3)
			{
				Logger.WriteLine("GPU OC helper request failed: " + ex3.Message);
			}
		}
	}

	internal static GpuOverclockApplyResult ResolveRequestForTests(string? payload, string expectedToken)
	{
		return ResolveRequest(payload, expectedToken);
	}

	private static GpuOverclockApplyResult ResolveRequest(string? payload, string expectedToken)
	{
		if (payload == null)
		{
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteRequestEmpty);
		}
		GpuOverclockApplyRequest gpuOverclockApplyRequest;
		try
		{
			gpuOverclockApplyRequest = JsonSerializer.Deserialize<GpuOverclockApplyRequest>(payload);
		}
		catch (JsonException ex)
		{
			Logger.WriteLine("GPU OC helper received malformed JSON: " + ex.Message);
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteRequestInvalid);
		}
		if ((object)gpuOverclockApplyRequest == null)
		{
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteRequestInvalid);
		}
		if (!TokenMatches(expectedToken, gpuOverclockApplyRequest.Token))
		{
			Logger.WriteLine("GPU OC helper rejected a request with an invalid token.");
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteRequestValidationFailed);
		}
		return Apply(gpuOverclockApplyRequest);
	}

	internal static bool ClientSidMatches(NamedPipeServerStream pipe, SecurityIdentifier expectedClient)
	{
		try
		{
			SecurityIdentifier actual = null;
			pipe.RunAsClient(() =>
			{
				using WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();
				actual = windowsIdentity.User;
			});
			return (object)actual != null && actual.Equals(expectedClient);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU OC helper can't verify the client identity: " + ex.Message);
			return false;
		}
	}

	private static GpuOverclockApplyResult Apply(GpuOverclockApplyRequest? request)
	{
		if ((object)request == null)
		{
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuWriteRequestInvalid);
		}
		using IGpuOverclockControl gpuOverclockControl = NvidiaGpuControl.TryCreateOverclockControl();
		if (gpuOverclockControl == null || !gpuOverclockControl.IsAvailable)
		{
			return new GpuOverclockApplyResult(Success: false, 0, 0, Strings.GpuNvOverclockInterfaceMissing);
		}
		int num = ((request.Enabled != false) ? (request.CoreOffset ?? gpuOverclockControl.CoreOffset.Current) : 0);
		int num2 = ((request.Enabled != false) ? (request.MemoryOffset ?? gpuOverclockControl.MemoryOffset.Current) : 0);
		if ((gpuOverclockControl.CoreOffset.IsAdjustable && !gpuOverclockControl.CoreOffset.Contains(num)) || (gpuOverclockControl.MemoryOffset.IsAdjustable && !gpuOverclockControl.MemoryOffset.Contains(num2)))
		{
			return new GpuOverclockApplyResult(Success: false, gpuOverclockControl.CoreOffset.Current, gpuOverclockControl.MemoryOffset.Current, Strings.GpuNvRangeExceeded);
		}
		bool num3 = !gpuOverclockControl.CoreOffset.IsAdjustable || gpuOverclockControl.CoreOffset.Current == num || gpuOverclockControl.SetCoreOffset(num);
		bool flag = !gpuOverclockControl.MemoryOffset.IsAdjustable || gpuOverclockControl.MemoryOffset.Current == num2 || gpuOverclockControl.SetMemoryOffset(num2);
		gpuOverclockControl.Refresh();
		bool flag2 = (num3 & flag) && (!gpuOverclockControl.CoreOffset.IsAdjustable || gpuOverclockControl.CoreOffset.Current == num) && (!gpuOverclockControl.MemoryOffset.IsAdjustable || gpuOverclockControl.MemoryOffset.Current == num2);
		Logger.WriteLine($"Elevated NVIDIA OC: core={num}/{gpuOverclockControl.CoreOffset.Current}, memory={num2}/{gpuOverclockControl.MemoryOffset.Current}, confirmed={flag2}");
		return new GpuOverclockApplyResult(flag2, gpuOverclockControl.CoreOffset.Current, gpuOverclockControl.MemoryOffset.Current, flag2 ? null : Strings.GpuNvReadbackMismatch);
	}
}
