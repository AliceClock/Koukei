using Koukei.Data.Entities;

namespace Koukei.Data.Services;

public interface IApplicationSettingService
{
    Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task<TValue?> GetJsonValueAsync<TValue>(string key, CancellationToken cancellationToken = default);

    Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default);

    Task SetJsonValueAsync<TValue>(string key, TValue value, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
