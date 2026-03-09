using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koukei.UI.Helpers;

/// <summary>
/// Coordinates replaceable asynchronous work. Starting new work cancels the previous
/// request and only the latest request is allowed to commit a result.
/// </summary>
internal sealed class LatestOperationController : IDisposable
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _currentCancellation;
    private long _version;
    private bool _disposed;

    public async Task<bool> RunAsync(
        Func<CancellationToken, Task> operation,
        Action<Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var request = Begin(cancellationToken);
        try
        {
            await operation(request.Cancellation.Token);
            return IsCurrent(request.Version) && !request.Cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (onError is not null)
        {
            if (IsCurrent(request.Version))
            {
                onError(ex);
            }

            return false;
        }
        finally
        {
            Complete(request);
        }
    }

    public async Task<bool> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Action<T> commit,
        Action<Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(commit);

        var request = Begin(cancellationToken);
        try
        {
            var result = await operation(request.Cancellation.Token);
            if (!IsCurrent(request.Version) || request.Cancellation.IsCancellationRequested)
            {
                return false;
            }

            commit(result);
            return true;
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (onError is not null)
        {
            if (IsCurrent(request.Version))
            {
                onError(ex);
            }

            return false;
        }
        finally
        {
            Complete(request);
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = _currentCancellation;
            _currentCancellation = null;
            _version++;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Cancel();
    }

    private Request Begin(CancellationToken cancellationToken)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long version;

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _currentCancellation;
            current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentCancellation = current;
            version = ++_version;
        }

        previous?.Cancel();
        previous?.Dispose();
        return new Request(version, current);
    }

    private bool IsCurrent(long version)
    {
        lock (_syncRoot)
        {
            return !_disposed && version == _version;
        }
    }

    private void Complete(Request request)
    {
        lock (_syncRoot)
        {
            if (request.Version == _version && ReferenceEquals(_currentCancellation, request.Cancellation))
            {
                _currentCancellation = null;
            }
        }

        request.Cancellation.Dispose();
    }

    private readonly record struct Request(long Version, CancellationTokenSource Cancellation);
}

internal sealed class AsyncReentrancyGuard
{
    private int _isBusy;

    public bool IsBusy => Volatile.Read(ref _isBusy) != 0;

    public event EventHandler<bool>? IsBusyChanged;

    public async Task<bool> TryRunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0)
        {
            return false;
        }

        IsBusyChanged?.Invoke(this, true);
        try
        {
            await operation();
            return true;
        }
        finally
        {
            Volatile.Write(ref _isBusy, 0);
            IsBusyChanged?.Invoke(this, false);
        }
    }
}
