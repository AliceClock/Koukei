using Koukei.Bus.Models;
using Koukei.Data;

namespace Koukei.Bus.Services;

public sealed class KoukeiDataGateway(IKoukeiDatabaseInitializer initializer) : IKoukeiDataGateway
{
    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        return initializer.EnsureReadyAsync(cancellationToken);
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return initializer.CanConnectAsync(cancellationToken);
    }

    public async Task<KoukeiSchemaStatus> VerifySchemaAsync(CancellationToken cancellationToken = default)
    {
        var result = await initializer.VerifySchemaAsync(cancellationToken);
        return new KoukeiSchemaStatus
        {
            MissingTables = result.MissingTables,
            MissingIndexes = result.MissingIndexes,
            MissingUniqueIndexes = result.MissingUniqueIndexes
        };
    }
}
