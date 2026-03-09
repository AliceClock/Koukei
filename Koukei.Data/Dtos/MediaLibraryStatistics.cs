namespace Koukei.Data.Dtos;

public sealed class MediaLibraryStatistics
{
    public int TotalItems { get; init; }

    public int AudioItems { get; init; }

    public int VideoItems { get; init; }

    public int ImageItems { get; init; }

    public int DocumentItems { get; init; }

    public int Tags { get; init; }

    public int Genres { get; init; }

    public int People { get; init; }

    public int Studios { get; init; }
}
