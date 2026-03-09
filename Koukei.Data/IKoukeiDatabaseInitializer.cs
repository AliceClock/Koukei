namespace Koukei.Data;

public interface IKoukeiDatabaseInitializer
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task MigrateAsync(CancellationToken cancellationToken = default);

    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    Task<KoukeiSchemaVerificationResult> VerifySchemaAsync(CancellationToken cancellationToken = default);
}
