using Koukei.Bus.Services;
using Koukei.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Koukei.Bus;

public static class KoukeiBusServiceCollectionExtensions
{
    public static IServiceCollection AddKoukeiBus(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddKoukeiData(databasePath);
        services.AddScoped<IKoukeiDataGateway, KoukeiDataGateway>();
        services.AddScoped<IMediaLibraryBus, MediaLibraryBus>();
        services.AddScoped<IPlaylistBus, PlaylistBus>();

        return services;
    }
}
