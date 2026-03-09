namespace Koukei.Data;

public sealed class KoukeiDataOptions
{
    public string DatabasePath { get; set; } = KoukeiDatabase.DefaultDatabaseFileName;

    public bool EnableWriteAheadLog { get; set; } = true;
}
