namespace Koukei.Data.Entities;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTimeOffset DateModified { get; set; } = DateTimeOffset.UtcNow;
}
