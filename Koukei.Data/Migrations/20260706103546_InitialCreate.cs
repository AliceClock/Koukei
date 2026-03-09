using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Koukei.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    DateModified = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TopParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    NormalizedPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Container = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ForcedSortName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    ProductionYear = table.Column<int>(type: "INTEGER", nullable: true),
                    DateCreated = table.Column<long>(type: "INTEGER", nullable: false),
                    DateLastSaved = table.Column<long>(type: "INTEGER", nullable: true),
                    DateLastRefreshed = table.Column<long>(type: "INTEGER", nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<long>(type: "INTEGER", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IndexNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentIndexNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ItemKind = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioBookReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    VolumeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AudioBookSeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkedBookId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MusicReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AlbumId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AirDate = table.Column<long>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    RadioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkedBookSeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MusicAlbumReleaseDate = table.Column<long>(type: "INTEGER", nullable: true),
                    BookPublicationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Book_VolumeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    BookSeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Isbn = table.Column<string>(type: "TEXT", nullable: true),
                    SerialPublicationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SerialId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JournalIssue_VolumeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    IssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    JournalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MagazineIssue_IssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    MagazineIssue_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    MagazineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Day = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    NewspaperIssue_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    NewspaperId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PublicationFrequency = table.Column<int>(type: "INTEGER", nullable: true),
                    Issn = table.Column<string>(type: "TEXT", nullable: true),
                    Magazine_PublicationFrequency = table.Column<int>(type: "INTEGER", nullable: true),
                    Magazine_Issn = table.Column<string>(type: "TEXT", nullable: true),
                    Newspaper_PublicationFrequency = table.Column<int>(type: "INTEGER", nullable: true),
                    Newspaper_Issn = table.Column<string>(type: "TEXT", nullable: true),
                    IsBook = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsMagazine = table.Column<bool>(type: "INTEGER", nullable: true),
                    Comic_VolumeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Comic_IssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ChapterNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TvSeason_SeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AirDays = table.Column<string>(type: "TEXT", nullable: true),
                    AirTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    MoviePremiereDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MovieSeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkedMusicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TvEpisodePremiereDate = table.Column<long>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VideoRecording_RecordingDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Items_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_AudioBookSeriesId",
                        column: x => x.AudioBookSeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_BookSeriesId",
                        column: x => x.BookSeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_JournalId",
                        column: x => x.JournalId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_LinkedBookId",
                        column: x => x.LinkedBookId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_LinkedBookSeriesId",
                        column: x => x.LinkedBookSeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_LinkedMusicId",
                        column: x => x.LinkedMusicId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_MagazineId",
                        column: x => x.MagazineId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_MovieSeriesId",
                        column: x => x.MovieSeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_NewspaperId",
                        column: x => x.NewspaperId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_Items_RadioId",
                        column: x => x.RadioId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Items_Items_TopParentId",
                        column: x => x.TopParentId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_Items_TvSeason_SeriesId",
                        column: x => x.TvSeason_SeriesId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraryRoots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NormalizedPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    IncludeSubdirectories = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateCreated = table.Column<long>(type: "INTEGER", nullable: false),
                    DateLastSaved = table.Column<long>(type: "INTEGER", nullable: true),
                    LastScanStartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastScanCompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastScanStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraryRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ratings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ratings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Studios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Studios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsScanned = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentInfos_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImageInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    ColorDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    ColorSpace = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    HasAlpha = table.Column<bool>(type: "INTEGER", nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageInfos_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemGenres",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenreId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemGenres", x => new { x.ItemId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_ItemGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemGenres_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaStreams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StreamIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Duration = table.Column<float>(type: "REAL", nullable: true),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    BitRate = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StreamType = table.Column<int>(type: "INTEGER", nullable: false),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    SampleRate = table.Column<int>(type: "INTEGER", nullable: true),
                    BitDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    FrameRate = table.Column<float>(type: "REAL", nullable: true),
                    ColorDepth = table.Column<string>(type: "TEXT", nullable: true),
                    IsHdr = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsDolbyVision = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaStreams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaStreams_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraryScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsDiscovered = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraryScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaLibraryScans_MediaLibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "MediaLibraryRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemPeople",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPeople", x => new { x.ItemId, x.PersonId, x.Role, x.SortOrder });
                    table.ForeignKey(
                        name: "FK_ItemPeople_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemRatings",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RatingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<float>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemRatings", x => new { x.ItemId, x.RatingId });
                    table.ForeignKey(
                        name: "FK_ItemRatings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemRatings_Ratings_RatingId",
                        column: x => x.RatingId,
                        principalTable: "Ratings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemStudios",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StudioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemStudios", x => new { x.ItemId, x.StudioId, x.SortOrder });
                    table.ForeignKey(
                        name: "FK_ItemStudios_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemStudios_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkedImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StudioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImageType = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DateModified = table.Column<long>(type: "INTEGER", nullable: true),
                    BlurHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkedImages_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedImages_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedImages_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTags", x => new { x.ItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ItemTags_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkedTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GenreId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StudioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RatingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TextType = table.Column<int>(type: "INTEGER", nullable: false),
                    TextIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    DateModified = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkedTexts_DocumentInfos_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "DocumentInfos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_ImageInfos_ImageId",
                        column: x => x.ImageId,
                        principalTable: "ImageInfos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_Ratings_RatingId",
                        column: x => x.RatingId,
                        principalTable: "Ratings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_Studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "Studios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LinkedTexts_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentInfos_ItemId",
                table: "DocumentInfos",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_SortName",
                table: "Genres",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_ImageInfos_ItemId",
                table: "ImageInfos",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemGenres_GenreId",
                table: "ItemGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPeople_PersonId",
                table: "ItemPeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRatings_RatingId",
                table: "ItemRatings",
                column: "RatingId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_AlbumId",
                table: "Items",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_AudioBookSeriesId",
                table: "Items",
                column: "AudioBookSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BookSeriesId",
                table: "Items",
                column: "BookSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DateCreated",
                table: "Items",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Hash",
                table: "Items",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemKind",
                table: "Items",
                column: "ItemKind");

            migrationBuilder.CreateIndex(
                name: "IX_Items_JournalId",
                table: "Items",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LinkedBookId",
                table: "Items",
                column: "LinkedBookId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LinkedBookSeriesId",
                table: "Items",
                column: "LinkedBookSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_LinkedMusicId",
                table: "Items",
                column: "LinkedMusicId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_MagazineId",
                table: "Items",
                column: "MagazineId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_MovieSeriesId",
                table: "Items",
                column: "MovieSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Name",
                table: "Items",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Items_NewspaperId",
                table: "Items",
                column: "NewspaperId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_NormalizedPath",
                table: "Items",
                column: "NormalizedPath",
                unique: true,
                filter: "\"NormalizedPath\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ParentId",
                table: "Items",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Path",
                table: "Items",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_Items_RadioId",
                table: "Items",
                column: "RadioId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_SeasonId",
                table: "Items",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_SeriesId",
                table: "Items",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_SortName",
                table: "Items",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TopParentId",
                table: "Items",
                column: "TopParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_TvSeason_SeriesId",
                table: "Items",
                column: "TvSeason_SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStudios_StudioId",
                table: "ItemStudios",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTags_TagId",
                table: "ItemTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedImages_ItemId_ImageType_ImageIndex",
                table: "LinkedImages",
                columns: new[] { "ItemId", "ImageType", "ImageIndex" },
                unique: true,
                filter: "\"ItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedImages_PersonId_ImageType_ImageIndex",
                table: "LinkedImages",
                columns: new[] { "PersonId", "ImageType", "ImageIndex" },
                unique: true,
                filter: "\"PersonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedImages_StudioId_ImageType_ImageIndex",
                table: "LinkedImages",
                columns: new[] { "StudioId", "ImageType", "ImageIndex" },
                unique: true,
                filter: "\"StudioId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_DocumentId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "DocumentId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"DocumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_GenreId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "GenreId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"GenreId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_ImageId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "ImageId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"ImageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_ItemId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "ItemId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"ItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_PersonId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "PersonId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"PersonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_RatingId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "RatingId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"RatingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_StudioId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "StudioId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"StudioId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedTexts_TagId_TextType_TextIndex",
                table: "LinkedTexts",
                columns: new[] { "TagId", "TextType", "TextIndex" },
                unique: true,
                filter: "\"TagId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryRoots_IsEnabled",
                table: "MediaLibraryRoots",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryRoots_LastScanStatus",
                table: "MediaLibraryRoots",
                column: "LastScanStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryRoots_NormalizedPath",
                table: "MediaLibraryRoots",
                column: "NormalizedPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryScans_LibraryRootId",
                table: "MediaLibraryScans",
                column: "LibraryRootId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryScans_StartedAt",
                table: "MediaLibraryScans",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryScans_Status",
                table: "MediaLibraryScans",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MediaStreams_ItemId_StreamIndex",
                table: "MediaStreams",
                columns: new[] { "ItemId", "StreamIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaStreams_StreamType",
                table: "MediaStreams",
                column: "StreamType");

            migrationBuilder.CreateIndex(
                name: "IX_People_Name",
                table: "People",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_SortName",
                table: "People",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_Source",
                table: "Ratings",
                column: "Source",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Studios_Name",
                table: "Studios",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Studios_SortName",
                table: "Studios",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SortName",
                table: "Tags",
                column: "SortName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "ItemGenres");

            migrationBuilder.DropTable(
                name: "ItemPeople");

            migrationBuilder.DropTable(
                name: "ItemRatings");

            migrationBuilder.DropTable(
                name: "ItemStudios");

            migrationBuilder.DropTable(
                name: "ItemTags");

            migrationBuilder.DropTable(
                name: "LinkedImages");

            migrationBuilder.DropTable(
                name: "LinkedTexts");

            migrationBuilder.DropTable(
                name: "MediaLibraryScans");

            migrationBuilder.DropTable(
                name: "MediaStreams");

            migrationBuilder.DropTable(
                name: "DocumentInfos");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "ImageInfos");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Ratings");

            migrationBuilder.DropTable(
                name: "Studios");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "MediaLibraryRoots");

            migrationBuilder.DropTable(
                name: "Items");
        }
    }
}
