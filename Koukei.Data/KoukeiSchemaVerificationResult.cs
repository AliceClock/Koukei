namespace Koukei.Data;

public sealed class KoukeiSchemaVerificationResult
{
    public required IReadOnlyList<string> MissingTables { get; init; }

    public required IReadOnlyList<string> MissingIndexes { get; init; }

    public IReadOnlyList<string> MissingUniqueIndexes { get; init; } = [];

    public bool IsValid =>
        MissingTables.Count == 0 &&
        MissingIndexes.Count == 0 &&
        MissingUniqueIndexes.Count == 0;
}
