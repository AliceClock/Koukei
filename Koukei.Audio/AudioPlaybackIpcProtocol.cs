using System.ComponentModel;
using System.Text.Json;

namespace Koukei.Audio;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class AudioPlaybackIpcProtocol
{
    public const int Version = 2;

    public const string RequestKind = "request";
    public const string ResponseKind = "response";
    public const string EventKind = "event";
    public const string CancelKind = "cancel";

    public const string PlayOperation = "play";
    public const string GetStateOperation = "getState";
    public const string SetPausedOperation = "setPaused";
    public const string SeekAbsoluteOperation = "seekAbsolute";
    public const string SetVolumeOperation = "setVolume";
    public const string SetMutedOperation = "setMuted";
    public const string SetSpeedOperation = "setSpeed";
    public const string StopOperation = "stop";
    public const string CloseOperation = "close";
    public const string ShutdownOperation = "shutdown";

    public const string StateChangedEvent = "stateChanged";
    public const string PlaybackEndedEvent = "playbackEnded";

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    public static string Serialize(
        string kind,
        long id = 0,
        string? name = null,
        object? payload = null,
        bool success = true,
        string? error = null) =>
        JsonSerializer.Serialize(
            new AudioPlaybackIpcMessage(
                Version,
                kind,
                id,
                name,
                payload,
                success,
                error),
            SerializerOptions);

    public static AudioPlaybackIpcMessage Deserialize(string json) =>
        JsonSerializer.Deserialize<AudioPlaybackIpcMessage>(json, SerializerOptions)
        ?? throw new InvalidDataException("The audio host returned an empty IPC message.");

    public static T DeserializePayload<T>(object? payload)
    {
        if (payload is JsonElement element)
        {
            return element.Deserialize<T>(SerializerOptions)
                ?? throw new InvalidDataException("The audio host returned an empty IPC payload.");
        }

        if (payload is null)
        {
            throw new InvalidDataException("The audio host did not return the expected IPC payload.");
        }

        return JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(payload, SerializerOptions),
                SerializerOptions)
            ?? throw new InvalidDataException("The audio host returned an invalid IPC payload.");
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AudioPlaybackIpcMessage(
    int Version,
    string Kind,
    long Id,
    string? Name,
    object? Payload,
    bool Success,
    string? Error);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AudioPlaybackBooleanValue(bool Value);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AudioPlaybackDoubleValue(double Value);
