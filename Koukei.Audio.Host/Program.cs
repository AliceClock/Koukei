using Koukei.Audio;

namespace Koukei.Audio.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!TryReadArguments(args, out var pipeName, out var parentProcessId))
        {
            return 2;
        }

        try
        {
            await using var playbackService = new SoundFlowAudioPlaybackService();
            await using var server = new AudioPlaybackHostServer(
                pipeName,
                parentProcessId,
                playbackService);
            await server.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static bool TryReadArguments(
        IReadOnlyList<string> args,
        out string pipeName,
        out int parentProcessId)
    {
        pipeName = string.Empty;
        parentProcessId = 0;
        for (var index = 0; index < args.Count - 1; index++)
        {
            switch (args[index])
            {
                case "--pipe":
                    pipeName = args[++index];
                    break;
                case "--parent-pid" when int.TryParse(args[++index], out var parsedProcessId):
                    parentProcessId = parsedProcessId;
                    break;
            }
        }

        return !string.IsNullOrWhiteSpace(pipeName) && parentProcessId > 0;
    }
}
