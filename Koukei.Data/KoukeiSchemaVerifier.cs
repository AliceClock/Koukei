using Microsoft.Data.Sqlite;

namespace Koukei.Data;

public static class KoukeiSchemaVerifier
{
    private static readonly string[] RequiredTables =
    [
        "Items",
        "Tags",
        "Genres",
        "People",
        "Studios",
        "Ratings",
        "ItemTags",
        "ItemGenres",
        "ItemPeople",
        "ItemStudios",
        "ItemRatings",
        "LinkedTexts",
        "LinkedImages",
        "DocumentInfos",
        "ImageInfos",
        "MediaStreams",
        "AppSettings",
        "MediaLibraryRoots",
        "MediaLibraryScans",
        "Playlists",
        "PlaylistItems",
        "UserMediaStates"
    ];

    private static readonly string[] RequiredIndexes =
    [
        "IX_Items_ItemKind",
        "IX_Items_Path",
        "IX_Items_NormalizedPath",
        "IX_Items_ParentId",
        "IX_Items_ItemKind_DateCreated",
        "IX_Items_ItemKind_LastModified",
        "IX_Items_ItemKind_Name",
        "IX_Items_ItemKind_SortName",
        "IX_MediaStreams_ItemId_StreamIndex",
        "IX_ItemPeople_PersonId",
        "IX_ItemStudios_StudioId",
        "IX_ItemRatings_RatingId",
        "IX_AppSettings_Key",
        "IX_MediaLibraryRoots_NormalizedPath",
        "IX_MediaLibraryScans_LibraryRootId",
        "IX_Playlists_SortName",
        "IX_PlaylistItems_ItemId",
        "IX_PlaylistItems_PlaylistId_ItemId",
        "IX_PlaylistItems_PlaylistId_SortOrder",
        "IX_UserMediaStates_IsFavorite",
        "IX_UserMediaStates_LastPlayedAt"
    ];

    private static readonly string[] RequiredUniqueIndexes =
    [
        "IX_Items_NormalizedPath",
        "IX_MediaStreams_ItemId_StreamIndex",
        "IX_LinkedTexts_ItemId_TextType_TextIndex",
        "IX_LinkedTexts_PersonId_TextType_TextIndex",
        "IX_LinkedTexts_GenreId_TextType_TextIndex",
        "IX_LinkedTexts_TagId_TextType_TextIndex",
        "IX_LinkedTexts_StudioId_TextType_TextIndex",
        "IX_LinkedTexts_RatingId_TextType_TextIndex",
        "IX_LinkedTexts_ImageId_TextType_TextIndex",
        "IX_LinkedTexts_DocumentId_TextType_TextIndex",
        "IX_LinkedImages_ItemId_ImageType_ImageIndex",
        "IX_LinkedImages_PersonId_ImageType_ImageIndex",
        "IX_LinkedImages_StudioId_ImageType_ImageIndex",
        "IX_AppSettings_Key",
        "IX_MediaLibraryRoots_NormalizedPath",
        "IX_PlaylistItems_PlaylistId_ItemId",
        "IX_PlaylistItems_PlaylistId_SortOrder"
    ];

    public static async Task<KoukeiSchemaVerificationResult> VerifySqliteAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = new SqliteConnection(KoukeiDatabase.CreateSqliteConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);

        var tables = await GetSqliteObjectsAsync(connection, "table", cancellationToken);
        var indexes = await GetSqliteObjectsAsync(connection, "index", cancellationToken);
        var uniqueIndexes = await GetUniqueIndexesAsync(connection, cancellationToken);

        return new KoukeiSchemaVerificationResult
        {
            MissingTables = RequiredTables.Where(table => !tables.Contains(table)).ToList(),
            MissingIndexes = RequiredIndexes.Where(index => !indexes.Contains(index)).ToList(),
            MissingUniqueIndexes = RequiredUniqueIndexes.Where(index => !uniqueIndexes.Contains(index)).ToList()
        };
    }

    private static async Task<HashSet<string>> GetSqliteObjectsAsync(
        SqliteConnection connection,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type";
        command.Parameters.AddWithValue("$type", objectType);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<HashSet<string>> GetUniqueIndexesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'index'
              AND sql LIKE 'CREATE UNIQUE INDEX%'
            """;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
