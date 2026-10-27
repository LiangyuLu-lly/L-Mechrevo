using System;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Display;

internal sealed class BrightnessCommitQueue : IDisposable
{
	private readonly Func<int, CancellationToken, Task> _writer;

	private readonly Action<Exception>? _onError;

	private readonly TimeSpan _debounce;

	private readonly object _gate = new object();

	private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

	private CancellationTokenSource? _requestCts;

	private TaskCompletionSource<bool> _idle = CompletedSource();

	private int? _latestValue;

	private bool _disposed;

	public bool HasPendingOrRunning
	{
		get
		{
			lock (_gate)
			{
				return _requestCts != null;
			}
		}
	}

	public BrightnessCommitQueue(Func<int, CancellationToken, Task> writer, TimeSpan debounce, Action<Exception>? onError = null)
	{
		_writer = writer ?? throw new ArgumentNullException("writer");
		if (debounce < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException("debounce");
		}
		_debounce = debounce;
		_onError = onError;
	}

	public void Submit(int value)
	{
		Start(value, _debounce);
	}

	public void Flush()
	{
		int value;
		lock (_gate)
		{
			if (_disposed || !_latestValue.HasValue)
			{
				return;
			}
			value = _latestValue.Value;
		}
		Start(value, TimeSpan.Zero);
	}

	internal Task WaitForIdleAsync()
	{
		lock (_gate)
		{
			return (_requestCts == null) ? Task.CompletedTask : _idle.Task;
		}
	}

	public void Dispose()
	{
		CancellationTokenSource requestCts;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			requestCts = _requestCts;
			_requestCts = null;
			_latestValue = null;
			_idle.TrySetResult(result: true);
		}
		requestCts?.Cancel();
		_disposeCts.Cancel();
	}

	private void Start(int value, TimeSpan delay)
	{
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
		CancellationTokenSource requestCts;
		lock (_gate)
		{
			if (_disposed)
			{
				cancellationTokenSource.Dispose();
				throw new ObjectDisposedException("BrightnessCommitQueue");
			}
			requestCts = _requestCts;
			_requestCts = cancellationTokenSource;
			_latestValue = value;
			if (_idle.Task.IsCompleted)
			{
				_idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			}
		}
		requestCts?.Cancel();
		CommitAsync(value, delay, cancellationTokenSource);
	}

	private async Task CommitAsync(int value, TimeSpan delay, CancellationTokenSource request)
	{
		_ = 2;
		try
		{
			await Task.Delay(delay, request.Token).ConfigureAwait(continueOnCapturedContext: false);
			await _writeLock.WaitAsync(request.Token).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				if (!request.IsCancellationRequested)
				{
					await _writer(value, _disposeCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			finally
			{
				_writeLock.Release();
			}
		}
		catch (OperationCanceledException) when (request.IsCancellationRequested)
		{
		}
		catch (Exception obj)
		{
			try
			{
				_onError?.Invoke(obj);
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Brightness commit error callback failed: " + ex2.GetType().Name + " " + ex2.Message);
			}
		}
		finally
		{
			lock (_gate)
			{
				if (_requestCts == request)
				{
					_requestCts = null;
					_latestValue = null;
					_idle.TrySetResult(result: true);
				}
			}
			request.Dispose();
		}
	}

	private static TaskCompletionSource<bool> CompletedSource()
	{
		TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		taskCompletionSource.TrySetResult(result: true);
		return taskCompletionSource;
	}
}
