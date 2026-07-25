using Koukei.Core.Tests.Infrastructure;
using Koukei.Data.Services;

namespace Koukei.Core.Tests;

public sealed class ApplicationSettingServiceTests
{
    [Fact]
    public async Task Settings_support_trimmed_keys_updates_json_and_deletion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ApplicationSettingService(database.Context);

        await service.SetValueAsync("  alpha  ", "first");
        await service.SetValueAsync("alpha", "updated");
        await service.SetValueAsync("beta", "second");
        await service.SetJsonValueAsync("preferences", new TestPreferences("dark", 125));

        Assert.Equal("updated", await service.GetValueAsync(" alpha "));
        Assert.Equal(
            new TestPreferences("dark", 125),
            await service.GetJsonValueAsync<TestPreferences>("preferences"));
        Assert.Equal(
            ["alpha", "beta", "preferences"],
            (await service.GetAllAsync()).Select(setting => setting.Key));
        Assert.True(await service.DeleteAsync(" beta "));
        Assert.False(await service.DeleteAsync("beta"));
        Assert.Null(await service.GetValueAsync("beta"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Settings_reject_empty_keys(string key)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ApplicationSettingService(database.Context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetValueAsync(key, "value"));
    }

    private sealed record TestPreferences(string Theme, int Volume);
}
