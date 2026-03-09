using Koukei.Data.Repositories;
using Koukei.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Koukei.Data;

public static class KoukeiDataServiceCollectionExtensions
{
    public static IServiceCollection AddKoukeiData(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return services.AddKoukeiData(options => options.DatabasePath = databasePath);
    }

    public static IServiceCollection AddKoukeiData(
        this IServiceCollection services,
        Action<KoukeiDataOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var koukeiOptions = new KoukeiDataOptions();
        configureOptions(koukeiOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(koukeiOptions.DatabasePath);
        KoukeiDatabase.EnsureDatabaseDirectory(koukeiOptions.DatabasePath);

        services.AddSingleton(koukeiOptions);

        services.AddDbContext<KoukeiDbContext>(options =>
            options.UseSqlite(KoukeiDatabase.CreateSqliteConnectionString(koukeiOptions.DatabasePath)));

        services.AddScoped<IKoukeiDatabaseInitializer, KoukeiDatabaseInitializer>();
        services.AddScoped<IKoukeiDatabaseMaintenance, KoukeiDatabaseMaintenance>();
        services.AddScoped<IKoukeiUnitOfWork, KoukeiUnitOfWork>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IApplicationSettingService, ApplicationSettingService>();
        services.AddScoped<IMediaLibraryService, MediaLibraryService>();
        services.AddScoped<IMediaLibraryRootService, MediaLibraryRootService>();
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<IUserMediaStateService, UserMediaStateService>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        return services;
    }
}
