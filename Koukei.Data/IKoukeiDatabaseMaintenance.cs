namespace Koukei.Data;

public interface IKoukeiDatabaseMaintenance
{
    Task VacuumAsync(CancellationToken cancellationToken = default);

    Task AnalyzeAsync(CancellationToken cancellationToken = default);

    Task CheckpointWalAsync(CancellationToken cancellationToken = default);

    Task BackupAsync(
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default);
}
