namespace Koukei.Data;

public sealed class KoukeiDatabaseMaintenance(KoukeiDataOptions options) : IKoukeiDatabaseMaintenance
{
    public Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        return KoukeiDatabase.VacuumAsync(options.DatabasePath, cancellationToken);
    }

    public Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        return KoukeiDatabase.AnalyzeAsync(options.DatabasePath, cancellationToken);
    }

    public Task CheckpointWalAsync(CancellationToken cancellationToken = default)
    {
        return KoukeiDatabase.CheckpointWalAsync(options.DatabasePath, cancellationToken);
    }

    public Task BackupAsync(
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        return KoukeiDatabase.BackupSqliteAsync(
            options.DatabasePath,
            destinationPath,
            overwrite,
            cancellationToken);
    }
}
