using Koukei.Mpv;
using Koukei.Mpv.Interop;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Koukei.Video;

internal sealed class MpvSeekPreviewThumbnailGenerator : IDisposable
{
    private static readonly TimeSpan GatePollingInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan FrameReadyTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MpvHandle _handle;
    private string? _loadedFilePath;
    private bool _isInitialized;
    private bool _isDisposed;

    public async Task<string?> CreateAsync(
        string filePath,
        string outputPath,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (_isDisposed ||
            cancellationToken.IsCancellationRequested ||
            !await TryEnterGateAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await Task.Run(
                () => CreateCore(
                    Path.GetFullPath(filePath),
                    Path.GetFullPath(outputPath),
                    position,
                    cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"mpv seek preview generation failed for '{filePath}': {ex.Message}");
            ResetCore();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _gate.Wait();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            ResetCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseResourcesAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isDisposed)
            {
                ResetCore();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryEnterGateAsync(CancellationToken cancellationToken)
    {
        while (!_isDisposed && !cancellationToken.IsCancellationRequested)
        {
            if (await _gate.WaitAsync(GatePollingInterval).ConfigureAwait(false))
            {
                if (_isDisposed || cancellationToken.IsCancellationRequested)
                {
                    _gate.Release();
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    private string? CreateCore(
        string filePath,
        string outputPath,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        if (_isDisposed || cancellationToken.IsCancellationRequested || !File.Exists(filePath))
        {
            return null;
        }

        EnsureInitialized();
        DrainPendingEvents();

        var isNewFile = !string.Equals(_loadedFilePath, filePath, StringComparison.OrdinalIgnoreCase);
        var targetPosition = Math.Max(0, position.TotalSeconds);
        MpvError commandError;
        if (isNewFile)
        {
            commandError = MpvCommandInvoker.Invoke(
                _handle,
                "loadfile",
                filePath,
                "replace",
                "-1",
                $"start={FormatDouble(targetPosition)},pause=yes");
            if (commandError == MpvError.Success)
            {
                _loadedFilePath = filePath;
            }
        }
        else
        {
            commandError = MpvCommandInvoker.Invoke(
                _handle,
                "seek",
                FormatDouble(targetPosition),
                "absolute+exact");
        }

        if (commandError != MpvError.Success ||
            !WaitForFrameReady(isNewFile, targetPosition, cancellationToken) ||
            cancellationToken.IsCancellationRequested)
        {
            if (isNewFile && commandError != MpvError.Success)
            {
                _loadedFilePath = null;
            }

            return null;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var temporaryPath = Path.Combine(
            outputDirectory ?? Path.GetTempPath(),
            $"{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.tmp.jpg");
        try
        {
            commandError = MpvCommandInvoker.Invoke(
                _handle,
                "screenshot-to-file",
                temporaryPath,
                "video");
            if (commandError != MpvError.Success ||
                cancellationToken.IsCancellationRequested ||
                !File.Exists(temporaryPath) ||
                new FileInfo(temporaryPath).Length == 0)
            {
                return null;
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            temporaryPath = string.Empty;
            return outputPath;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }

    private void EnsureInitialized()
    {
        if (_isInitialized && _handle.Handle != IntPtr.Zero)
        {
            return;
        }

        ResetCore();
        MpvNativeLibraryResolver.EnsureRegistered();
        _handle = MpvNative.MpvCreate();
        if (_handle.Handle == IntPtr.Zero)
        {
            throw new MpvException("mpv_create returned a null handle for seek previews.");
        }

        SetOption("config", "no");
        SetOption("load-scripts", "no");
        SetOption("idle", "yes");
        SetOption("keep-open", "yes");
        SetOption("pause", "yes");
        SetOption("vo", "null");
        SetOption("ao", "null");
        SetOption("audio", "no");
        SetOption("sub", "no");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");
        SetOption("osc", "no");
        SetOption("osd-level", "0");
        SetOption("terminal", "no");
        SetOption("msg-level", "all=no");
        SetOption("untimed", "yes");
        SetOption("hr-seek", "yes");
        SetOption("cache", "no");
        SetOption("demuxer-max-bytes", "16MiB");
        SetOption("demuxer-max-back-bytes", "4MiB");
        SetOption("vf", "scale=320:-2");
        SetOption("screenshot-format", "jpg");
        SetOption("screenshot-jpeg-quality", "82");

        var initializeError = MpvNative.MpvInitialize(_handle);
        if (initializeError != MpvError.Success)
        {
            throw new MpvException("mpv seek preview initialization failed", initializeError);
        }

        _isInitialized = true;
    }

    private bool WaitForFrameReady(
        bool isNewFile,
        double targetPosition,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() +
            (long)(FrameReadyTimeout.TotalSeconds * Stopwatch.Frequency);
        var hasStartedTargetFile = !isNewFile;
        var hasRestartedPlayback = false;

        while (!cancellationToken.IsCancellationRequested &&
            Stopwatch.GetTimestamp() < deadline)
        {
            var eventPointer = MpvNative.MpvWaitEvent(_handle, 0.05);
            if (eventPointer == IntPtr.Zero)
            {
                continue;
            }

            var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPointer);
            switch (mpvEvent.EventId)
            {
                case MpvEventId.StartFile:
                    hasStartedTargetFile = true;
                    break;
                case MpvEventId.PlaybackRestart when hasStartedTargetFile:
                    hasRestartedPlayback = true;
                    break;
                case MpvEventId.EndFile when hasStartedTargetFile:
                case MpvEventId.Shutdown:
                    if (mpvEvent.EventId == MpvEventId.Shutdown)
                    {
                        _isInitialized = false;
                    }

                    _loadedFilePath = null;
                    return false;
            }

            if (hasRestartedPlayback && IsAtTargetPosition(targetPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAtTargetPosition(double targetPosition)
    {
        if (TryGetPropertyFlag("seeking", out var isSeeking) && isSeeking)
        {
            return false;
        }

        return TryGetPropertyDouble("time-pos", out var actualPosition) &&
            Math.Abs(actualPosition - targetPosition) <= 1;
    }

    private bool TryGetPropertyDouble(string name, out double value)
    {
        var error = MpvNative.MpvGetProperty(_handle, name, MpvFormat.Double, out var node);
        if (error == MpvError.Success)
        {
            value = node.Double;
            return true;
        }

        value = 0;
        return false;
    }

    private bool TryGetPropertyFlag(string name, out bool value)
    {
        var error = MpvNative.MpvGetProperty(_handle, name, MpvFormat.Flag, out var node);
        if (error == MpvError.Success)
        {
            value = node.Flag != 0;
            return true;
        }

        value = false;
        return false;
    }

    private void DrainPendingEvents()
    {
        for (var i = 0; i < 256; i++)
        {
            var eventPointer = MpvNative.MpvWaitEvent(_handle, 0);
            if (eventPointer == IntPtr.Zero)
            {
                return;
            }

            var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPointer);
            if (mpvEvent.EventId == MpvEventId.None)
            {
                return;
            }

            if (mpvEvent.EventId == MpvEventId.Shutdown)
            {
                _isInitialized = false;
                _loadedFilePath = null;
                return;
            }
        }
    }

    private void SetOption(string name, string value)
    {
        var error = MpvNative.MpvSetOptionString(_handle, name, value);
        if (error != MpvError.Success)
        {
            throw new MpvException($"mpv seek preview option '{name}' failed", error);
        }
    }

    private void ResetCore()
    {
        if (_handle.Handle != IntPtr.Zero)
        {
            try
            {
                MpvNative.MpvTerminateDestroy(_handle);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        _handle = default;
        _loadedFilePath = null;
        _isInitialized = false;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
