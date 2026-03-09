using Koukei.Bus.Models;

namespace Koukei.Bus.Services;

public interface IKoukeiDataGateway
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);

    Task<KoukeiSchemaStatus> VerifySchemaAsync(CancellationToken cancellationToken = default);
}
