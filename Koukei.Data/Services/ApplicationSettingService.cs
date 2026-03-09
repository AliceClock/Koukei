using System.Text.Json;
using Koukei.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Services;

public sealed class ApplicationSettingService(KoukeiDbContext context) : IApplicationSettingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        return context.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Key == normalizedKey, cancellationToken);
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.AppSettings.AsNoTracking()
            .OrderBy(setting => setting.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await GetAsync(key, cancellationToken);
        return setting?.Value;
    }

    public async Task<TValue?> GetJsonValueAsync<TValue>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        return string.IsNullOrWhiteSpace(value)
            ? default
            : JsonSerializer.Deserialize<TValue>(value, JsonOptions);
    }

    public async Task SetValueAsync(
        string key,
        string? value,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(setting => setting.Key == normalizedKey, cancellationToken);

        if (setting is null)
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = normalizedKey,
                Value = value
            });
        }
        else
        {
            setting.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SetJsonValueAsync<TValue>(
        string key,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        return SetValueAsync(key, serialized, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var setting = await context.AppSettings
            .FirstOrDefaultAsync(setting => setting.Key == normalizedKey, cancellationToken);
        if (setting is null)
        {
            return false;
        }

        context.AppSettings.Remove(setting);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim();
    }
}
