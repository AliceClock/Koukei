using Koukei.Mpv.Interop;

namespace Koukei.Mpv;

public sealed class MpvException : Exception
{
    public MpvException(string message)
        : base(message)
    {
    }

    public MpvException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal MpvException(string message, MpvError error)
        : base($"{message}: {GetErrorMessage(error)}")
    {
        Error = error;
    }

    public MpvError? Error { get; }

    private static string GetErrorMessage(MpvError error)
    {
        try
        {
            return $"{error} ({MpvNative.MpvErrorString(error)})";
        }
        catch
        {
            return error.ToString();
        }
    }
}
