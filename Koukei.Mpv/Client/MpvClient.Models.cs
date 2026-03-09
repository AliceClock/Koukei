//using System;
//using Koukei.Mpv.Interop;

//namespace Koukei.Mpv.Client;

//public sealed class MpvException : Exception
//{
//    public MpvError MpvError { get; set; }
//    public MpvException(string message, MpvError mpvError) : base(message) => MpvError = mpvError;

//    public MpvException(string message, Exception innerException, MpvError mpvError) : base(message, innerException) =>
//        MpvError = mpvError;
//}

//public sealed class MpvInitializeOptions
//{
//    public bool? UseConfig { get; set; }
//    public string? ConfigDirectory { get; set; }
//    public string? InputConfigPath { get; set; }
//    public bool? LoadScripts { get; set; }
//    public string? ScriptPath { get; set; }
//    public MpvPlayerOperationMode? PlayerOperationMode { get; set; }
//}

//public sealed class MpvPlayOptions
//{
//    public IntPtr? WindowHandle { get; set; }
//    public double? StartPosition { get; set; }
//    public double? InitialVolume { get; set; }
//    public double? InitialSpeed { get; set; }
//}

//public sealed class MpvClientNotifyEventArgs(MpvClientEventId id, object data) : EventArgs
//{
//    public MpvClientEventId Id { get; set; } = id;
//    public object Data { get; set; } = data;
//}

//internal sealed class MpvPlayerSnapshot(string? filePath, MpvPlayOptions? options)
//{
//    public string? FilePath { get; } = filePath;
//    public MpvPlayOptions? Options { get; internal set; } = options;
//}

//public class Error
//{
//    public string Message { get; }
//    public Exception? Exception { get; }

//    public Error(string message) => Message = message;

//    public Error(string message, Exception exception)
//    {
//        Message = message;
//        Exception = exception;
//    }
//}

//public class Result<T>
//{
//    public T? Value { get; }
//    public Error? Error { get; }
//    public bool IsSuccess => Error == null;

//    private Result(T? value, Error? error)
//    {
//        Value = value;
//        Error = error;
//    }

//    public static Result<T> Ok() => new(default, null);
//    public static Result<T> Ok(T value) => new(value, null);
//    public static Result<T> Fail(Error error) => new(default, error);
//}