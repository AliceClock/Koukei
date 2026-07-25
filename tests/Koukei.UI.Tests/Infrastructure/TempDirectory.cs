namespace Koukei.UI.Tests.Infrastructure;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string purpose)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Koukei.UI.Tests",
            purpose,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }

        // A locked diagnostic directory must not hide the test's primary result.
    }
}
