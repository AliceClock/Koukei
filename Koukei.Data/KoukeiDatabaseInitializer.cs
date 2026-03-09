using Microsoft.EntityFrameworkCore;

namespace Koukei.Data;

public sealed class KoukeiDatabaseInitializer(
    KoukeiDbContext context,
    KoukeiDataOptions options) : IKoukeiDatabaseInitializer
{
    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        return EnsureCreatedCoreAsync(cancellationToken);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var migrations = context.Database.GetMigrations();

        if (migrations.Any())
        {
            await context.Database.MigrateAsync(cancellationToken);
            await KoukeiDatabase.ApplySqlitePragmasAsync(context, options, cancellationToken);
            return;
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await KoukeiDatabase.ApplySqlitePragmasAsync(context, options, cancellationToken);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.MigrateAsync(cancellationToken);
        await KoukeiDatabase.ApplySqlitePragmasAsync(context, options, cancellationToken);
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return context.Database.CanConnectAsync(cancellationToken);
    }

    public Task<KoukeiSchemaVerificationResult> VerifySchemaAsync(CancellationToken cancellationToken = default)
    {
        return KoukeiSchemaVerifier.VerifySqliteAsync(options.DatabasePath, cancellationToken);
    }

    private async Task EnsureCreatedCoreAsync(CancellationToken cancellationToken)
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await KoukeiDatabase.ApplySqlitePragmasAsync(context, options, cancellationToken);
    }
}
