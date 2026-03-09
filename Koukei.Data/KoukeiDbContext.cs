using Koukei.Data.Entities;
using Koukei.Data.Entities.Audio;
using Koukei.Data.Entities.Audio.AudioBook;
using Koukei.Data.Entities.Audio.Music;
using Koukei.Data.Entities.Audio.Radio;
using Koukei.Data.Entities.Document;
using Koukei.Data.Entities.Document.Book;
using Koukei.Data.Entities.Document.Journal;
using Koukei.Data.Entities.Document.Magazine;
using Koukei.Data.Entities.Document.Newspaper;
using Koukei.Data.Entities.Image.Comic;
using Koukei.Data.Entities.Image.Illustration;
using Koukei.Data.Entities.Image.Photo;
using Koukei.Data.Entities.Video;
using Koukei.Data.Entities.Video.Movie;
using Koukei.Data.Entities.Video.TV;
using Koukei.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Globalization;

using DocumentItem = Koukei.Data.Entities.Document.Document;
using ImageItem = Koukei.Data.Entities.Image.Image;

namespace Koukei.Data;

public class KoukeiDbContext(DbContextOptions<KoukeiDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<List<DayOfWeek>, string> AirDaysConverter = new(
        value => string.Join(',', value.Select(day => ((int)day).ToString(CultureInfo.InvariantCulture))),
        value => string.IsNullOrWhiteSpace(value)
            ? new List<DayOfWeek>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(day => (DayOfWeek)int.Parse(day, CultureInfo.InvariantCulture))
                .ToList());

    private static readonly ValueComparer<List<DayOfWeek>> AirDaysComparer = new(
        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right)),
        value => value.Aggregate(0, (hash, day) => HashCode.Combine(hash, day.GetHashCode())),
        value => value.ToList());

    public DbSet<BaseItem> Items => Set<BaseItem>();

    public DbSet<Audio> AudioItems => Set<Audio>();

    public DbSet<AudioRecording> AudioRecordings => Set<AudioRecording>();

    public DbSet<Music> MusicTracks => Set<Music>();

    public DbSet<MusicAlbum> MusicAlbums => Set<MusicAlbum>();

    public DbSet<AudioBook> AudioBooks => Set<AudioBook>();

    public DbSet<AudioBookSeries> AudioBookSeries => Set<AudioBookSeries>();

    public DbSet<Radio> Radios => Set<Radio>();

    public DbSet<RadioEpisode> RadioEpisodes => Set<RadioEpisode>();

    public DbSet<Video> Videos => Set<Video>();

    public DbSet<VideoRecording> VideoRecordings => Set<VideoRecording>();

    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<MovieSeries> MovieSeries => Set<MovieSeries>();

    public DbSet<TvSeries> TvSeries => Set<TvSeries>();

    public DbSet<TvSeason> TvSeasons => Set<TvSeason>();

    public DbSet<TvEpisode> TvEpisodes => Set<TvEpisode>();

    public DbSet<MusicVideo> MusicVideos => Set<MusicVideo>();

    public DbSet<ImageItem> Images => Set<ImageItem>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<PhotoAlbum> PhotoAlbums => Set<PhotoAlbum>();

    public DbSet<Illustration> Illustrations => Set<Illustration>();

    public DbSet<IllustrationCollection> IllustrationCollections => Set<IllustrationCollection>();

    public DbSet<Comic> Comics => Set<Comic>();

    public DbSet<ComicSeries> ComicSeries => Set<ComicSeries>();

    public DbSet<DocumentItem> Documents => Set<DocumentItem>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookSeries> BookSeries => Set<BookSeries>();

    public DbSet<Magazine> Magazines => Set<Magazine>();

    public DbSet<MagazineIssue> MagazineIssues => Set<MagazineIssue>();

    public DbSet<Newspaper> Newspapers => Set<Newspaper>();

    public DbSet<NewspaperIssue> NewspaperIssues => Set<NewspaperIssue>();

    public DbSet<Journal> Journals => Set<Journal>();

    public DbSet<JournalIssue> JournalIssues => Set<JournalIssue>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Studio> Studios => Set<Studio>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<ItemTag> ItemTags => Set<ItemTag>();

    public DbSet<ItemGenre> ItemGenres => Set<ItemGenre>();

    public DbSet<ItemPerson> ItemPeople => Set<ItemPerson>();

    public DbSet<ItemStudio> ItemStudios => Set<ItemStudio>();

    public DbSet<ItemRating> ItemRatings => Set<ItemRating>();

    public DbSet<LinkedText> LinkedTexts => Set<LinkedText>();

    public DbSet<LinkedImage> LinkedImages => Set<LinkedImage>();

    public DbSet<DocumentInfo> DocumentInfos => Set<DocumentInfo>();

    public DbSet<ImageInfo> ImageInfos => Set<ImageInfo>();

    public DbSet<MediaStreamInfo> MediaStreams => Set<MediaStreamInfo>();

    public DbSet<AudioStreamInfo> AudioStreams => Set<AudioStreamInfo>();

    public DbSet<VideoStreamInfo> VideoStreams => Set<VideoStreamInfo>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<MediaLibraryRoot> MediaLibraryRoots => Set<MediaLibraryRoot>();

    public DbSet<MediaLibraryScan> MediaLibraryScans => Set<MediaLibraryScan>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();

    public DbSet<UserMediaState> UserMediaStates => Set<UserMediaState>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureItems(modelBuilder);
        ConfigureLookupEntities(modelBuilder);
        ConfigureJoinEntities(modelBuilder);
        ConfigureLinkedResources(modelBuilder);
        ConfigureMetadata(modelBuilder);
        ConfigureMediaStreams(modelBuilder);
        ConfigureTypedRelationships(modelBuilder);
        ConfigureApplicationState(modelBuilder);
        ConfigurePlaylists(modelBuilder);
        ConfigureUserMediaState(modelBuilder);
    }

    private static void ConfigureItems(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<BaseItem>();
        item.ToTable("Items");
        item.HasKey(entity => entity.Id);
        item.Ignore(entity => entity.Kind);
        item.Ignore(entity => entity.MediaType);
        item.Ignore(entity => entity.IsFolder);

        item.Property(entity => entity.Name).HasMaxLength(512).IsRequired();
        item.Property(entity => entity.Path).HasMaxLength(2048);
        item.Property(entity => entity.NormalizedPath).HasMaxLength(2048);
        item.Property(entity => entity.Container).HasMaxLength(512);
        item.Property(entity => entity.FileSize);
        item.Property(entity => entity.SortName).HasMaxLength(512);
        item.Property(entity => entity.OriginalTitle).HasMaxLength(512);
        item.Property(entity => entity.ForcedSortName).HasMaxLength(512);
        item.Property(entity => entity.Hash).HasMaxLength(256);
        item.Property<BaseItemKind>("ItemKind");

        item.HasIndex("ItemKind");
        item.HasIndex(entity => entity.Name);
        item.HasIndex(entity => entity.SortName);
        item.HasIndex(entity => entity.Path);
        item.HasIndex(entity => entity.NormalizedPath)
            .IsUnique()
            .HasFilter("\"NormalizedPath\" IS NOT NULL");
        item.HasIndex(entity => entity.ParentId);
        item.HasIndex(entity => entity.TopParentId);
        item.HasIndex(entity => entity.DateCreated);
        item.HasIndex(entity => entity.Hash);
        item.HasIndex("ItemKind", nameof(BaseItem.DateCreated));
        item.HasIndex("ItemKind", nameof(BaseItem.Name));
        item.HasIndex("ItemKind", nameof(BaseItem.SortName));
        item.HasIndex("ItemKind", nameof(BaseItem.LastModified));

        item.HasOne(entity => entity.Parent)
            .WithMany(entity => entity.Children)
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        item.HasOne(entity => entity.TopParent)
            .WithMany()
            .HasForeignKey(entity => entity.TopParentId)
            .OnDelete(DeleteBehavior.Restrict);

        item.HasDiscriminator<BaseItemKind>("ItemKind")
            .HasValue<Audio>(BaseItemKind.Audio)
            .HasValue<AudioRecording>(BaseItemKind.AudioRecording)
            .HasValue<Music>(BaseItemKind.Music)
            .HasValue<MusicAlbum>(BaseItemKind.MusicAlbum)
            .HasValue<AudioBook>(BaseItemKind.AudioBook)
            .HasValue<AudioBookSeries>(BaseItemKind.AudioBookSeries)
            .HasValue<RadioEpisode>(BaseItemKind.RadioEpisode)
            .HasValue<Radio>(BaseItemKind.Radio)
            .HasValue<Video>(BaseItemKind.Video)
            .HasValue<VideoRecording>(BaseItemKind.VideoRecording)
            .HasValue<Movie>(BaseItemKind.Movie)
            .HasValue<MovieSeries>(BaseItemKind.MovieSeries)
            .HasValue<TvEpisode>(BaseItemKind.TvEpisode)
            .HasValue<TvSeason>(BaseItemKind.TvSeason)
            .HasValue<TvSeries>(BaseItemKind.TvSeries)
            .HasValue<MusicVideo>(BaseItemKind.MusicVideo)
            .HasValue<ImageItem>(BaseItemKind.Image)
            .HasValue<Photo>(BaseItemKind.Photo)
            .HasValue<PhotoAlbum>(BaseItemKind.PhotoAlbum)
            .HasValue<Illustration>(BaseItemKind.Illustration)
            .HasValue<IllustrationCollection>(BaseItemKind.Artbook)
            .HasValue<Comic>(BaseItemKind.Comic)
            .HasValue<ComicSeries>(BaseItemKind.ComicSeries)
            .HasValue<DocumentItem>(BaseItemKind.Document)
            .HasValue<Book>(BaseItemKind.Book)
            .HasValue<BookSeries>(BaseItemKind.BookSeries)
            .HasValue<SerialIssue>(BaseItemKind.SerialIssue)
            .HasValue<MagazineIssue>(BaseItemKind.MagazineIssue)
            .HasValue<Magazine>(BaseItemKind.Magazine)
            .HasValue<NewspaperIssue>(BaseItemKind.NewspaperIssue)
            .HasValue<Newspaper>(BaseItemKind.Newspaper)
            .HasValue<JournalIssue>(BaseItemKind.JournalIssue)
            .HasValue<Journal>(BaseItemKind.Journal);

        modelBuilder.Entity<Music>().Property(entity => entity.ReleaseDate).HasColumnName("MusicReleaseDate");
        modelBuilder.Entity<Audio>().Property(entity => entity.ArtistName).HasMaxLength(512);
        modelBuilder.Entity<Audio>().Property(entity => entity.AlbumTitle).HasMaxLength(512);
        modelBuilder.Entity<MusicAlbum>().Property(entity => entity.ReleaseDate).HasColumnName("MusicAlbumReleaseDate");
        modelBuilder.Entity<AudioBook>().Property(entity => entity.ReleaseDate).HasColumnName("AudioBookReleaseDate");
        modelBuilder.Entity<Book>().Property(entity => entity.PublicationDate).HasColumnName("BookPublicationDate");
        modelBuilder.Entity<SerialIssue>().Property(entity => entity.PublicationDate).HasColumnName("SerialPublicationDate");
        modelBuilder.Entity<Movie>().Property(entity => entity.PremiereDate).HasColumnName("MoviePremiereDate");
        modelBuilder.Entity<TvEpisode>().Property(entity => entity.PremiereDate).HasColumnName("TvEpisodePremiereDate");

        modelBuilder.Entity<TvSeason>()
            .Property(entity => entity.AirDays)
            .HasConversion(AirDaysConverter)
            .Metadata.SetValueComparer(AirDaysComparer);
    }

    private static void ConfigureLookupEntities(ModelBuilder modelBuilder)
    {
        ConfigureNamedLookup<Tag>(modelBuilder, "Tags");
        ConfigureNamedLookup<Genre>(modelBuilder, "Genres");
        ConfigureNamedLookup<Person>(modelBuilder, "People");
        ConfigureNamedLookup<Studio>(modelBuilder, "Studios");

        modelBuilder.Entity<Rating>(builder =>
        {
            builder.ToTable("Ratings");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Source).HasMaxLength(256).IsRequired();
            builder.HasIndex(entity => entity.Source).IsUnique();
        });
    }

    private static void ConfigureNamedLookup<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.ToTable(tableName);
            builder.HasKey("Id");
            builder.Property<string>("Name").HasMaxLength(256).IsRequired();
            builder.Property<string?>("SortName").HasMaxLength(256);
            builder.HasIndex("Name").IsUnique();
            builder.HasIndex("SortName");
        });
    }

    private static void ConfigureJoinEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemTag>(builder =>
        {
            builder.ToTable("ItemTags");
            builder.HasKey(entity => new { entity.ItemId, entity.TagId });
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.Tags).HasForeignKey(entity => entity.ItemId);
            builder.HasOne(entity => entity.Tag).WithMany(entity => entity.ItemLinks).HasForeignKey(entity => entity.TagId);
        });

        modelBuilder.Entity<ItemGenre>(builder =>
        {
            builder.ToTable("ItemGenres");
            builder.HasKey(entity => new { entity.ItemId, entity.GenreId });
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.Genres).HasForeignKey(entity => entity.ItemId);
            builder.HasOne(entity => entity.Genre).WithMany(entity => entity.ItemLinks).HasForeignKey(entity => entity.GenreId);
        });

        modelBuilder.Entity<ItemPerson>(builder =>
        {
            builder.ToTable("ItemPeople");
            builder.HasKey(entity => new { entity.ItemId, entity.PersonId, entity.Role, entity.SortOrder });
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.RelatedPeople).HasForeignKey(entity => entity.ItemId);
            builder.HasOne(entity => entity.Person).WithMany(entity => entity.ItemLinks).HasForeignKey(entity => entity.PersonId);
            builder.HasIndex(entity => entity.PersonId);
        });

        modelBuilder.Entity<ItemStudio>(builder =>
        {
            builder.ToTable("ItemStudios");
            builder.HasKey(entity => new { entity.ItemId, entity.StudioId, entity.SortOrder });
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.Studios).HasForeignKey(entity => entity.ItemId);
            builder.HasOne(entity => entity.Studio).WithMany(entity => entity.ItemLinks).HasForeignKey(entity => entity.StudioId);
            builder.HasIndex(entity => entity.StudioId);
        });

        modelBuilder.Entity<ItemRating>(builder =>
        {
            builder.ToTable("ItemRatings");
            builder.HasKey(entity => new { entity.ItemId, entity.RatingId });
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.Ratings).HasForeignKey(entity => entity.ItemId);
            builder.HasOne(entity => entity.Rating).WithMany(entity => entity.ItemLinks).HasForeignKey(entity => entity.RatingId);
            builder.HasIndex(entity => entity.RatingId);
        });
    }

    private static void ConfigureLinkedResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkedText>(builder =>
        {
            builder.ToTable("LinkedTexts");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Text).HasMaxLength(65536);
            builder.HasIndex(entity => new { entity.ItemId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"ItemId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.PersonId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"PersonId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.GenreId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"GenreId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.TagId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"TagId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.StudioId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"StudioId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.RatingId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"RatingId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.ImageId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"ImageId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.DocumentId, entity.TextType, entity.TextIndex })
                .IsUnique()
                .HasFilter("\"DocumentId\" IS NOT NULL");

            builder.HasOne(entity => entity.Item)
                .WithMany(entity => entity.Texts)
                .HasForeignKey(entity => entity.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(entity => entity.Person).WithMany(entity => entity.Texts).HasForeignKey(entity => entity.PersonId);
            builder.HasOne(entity => entity.Genre).WithMany(entity => entity.Texts).HasForeignKey(entity => entity.GenreId);
            builder.HasOne(entity => entity.Tag).WithMany(entity => entity.Texts).HasForeignKey(entity => entity.TagId);
            builder.HasOne(entity => entity.Studio).WithMany(entity => entity.Texts).HasForeignKey(entity => entity.StudioId);
            builder.HasOne(entity => entity.Rating).WithMany(entity => entity.Texts).HasForeignKey(entity => entity.RatingId);
            builder.HasOne(entity => entity.Image).WithMany().HasForeignKey(entity => entity.ImageId);
            builder.HasOne(entity => entity.Document).WithMany().HasForeignKey(entity => entity.DocumentId);
        });

        modelBuilder.Entity<LinkedImage>(builder =>
        {
            builder.ToTable("LinkedImages");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Path).HasMaxLength(2048);
            builder.Property(entity => entity.BlurHash).HasMaxLength(256);
            builder.HasIndex(entity => new { entity.ItemId, entity.ImageType, entity.ImageIndex })
                .IsUnique()
                .HasFilter("\"ItemId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.PersonId, entity.ImageType, entity.ImageIndex })
                .IsUnique()
                .HasFilter("\"PersonId\" IS NOT NULL");
            builder.HasIndex(entity => new { entity.StudioId, entity.ImageType, entity.ImageIndex })
                .IsUnique()
                .HasFilter("\"StudioId\" IS NOT NULL");

            builder.HasOne(entity => entity.Item)
                .WithMany(entity => entity.Images)
                .HasForeignKey(entity => entity.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(entity => entity.Person).WithMany(entity => entity.Images).HasForeignKey(entity => entity.PersonId);
            builder.HasOne(entity => entity.Studio).WithMany(entity => entity.Images).HasForeignKey(entity => entity.StudioId);
        });
    }

    private static void ConfigureMetadata(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentInfo>(builder =>
        {
            builder.ToTable("DocumentInfos");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Language).HasMaxLength(64);
            builder.Property(entity => entity.Format).HasMaxLength(64);
            builder.HasIndex(entity => entity.ItemId);
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.DocumentInfo).HasForeignKey(entity => entity.ItemId);
        });

        modelBuilder.Entity<ImageInfo>(builder =>
        {
            builder.ToTable("ImageInfos");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.ColorSpace).HasMaxLength(64);
            builder.Property(entity => entity.Format).HasMaxLength(64);
            builder.HasIndex(entity => entity.ItemId);
            builder.HasOne(entity => entity.Item).WithMany(entity => entity.ImageInfo).HasForeignKey(entity => entity.ItemId);
        });
    }

    private static void ConfigureMediaStreams(ModelBuilder modelBuilder)
    {
        var stream = modelBuilder.Entity<MediaStreamInfo>();
        stream.ToTable("MediaStreams");
        stream.HasKey(entity => entity.Id);
        stream.Ignore(entity => entity.Type);
        stream.Property<MediaStreamType>("StreamType");
        stream.Property(entity => entity.Codec).HasMaxLength(128);
        stream.Property(entity => entity.Language).HasMaxLength(64);
        stream.Property(entity => entity.Title).HasMaxLength(512);
        stream.HasIndex("StreamType");
        stream.HasIndex(entity => new { entity.ItemId, entity.StreamIndex }).IsUnique();
        stream.HasOne(entity => entity.Item).WithMany(entity => entity.MediaStreams).HasForeignKey(entity => entity.ItemId);

        stream.HasDiscriminator<MediaStreamType>("StreamType")
            .HasValue<AudioStreamInfo>(MediaStreamType.Audio)
            .HasValue<VideoStreamInfo>(MediaStreamType.Video);

        modelBuilder.Entity<AudioStreamInfo>()
            .Property(entity => entity.ChannelLayout)
            .HasMaxLength(128);
        modelBuilder.Entity<VideoStreamInfo>()
            .Property(entity => entity.CodecProfile)
            .HasMaxLength(128);
        modelBuilder.Entity<VideoStreamInfo>()
            .Property(entity => entity.PixelFormat)
            .HasMaxLength(128);
    }

    private static void ConfigureTypedRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Music>()
            .HasOne(entity => entity.Album)
            .WithMany()
            .HasForeignKey(entity => entity.AlbumId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AudioBook>()
            .HasOne(entity => entity.AudioBookSeries)
            .WithMany()
            .HasForeignKey(entity => entity.AudioBookSeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AudioBook>()
            .HasOne(entity => entity.LinkedBook)
            .WithMany()
            .HasForeignKey(entity => entity.LinkedBookId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AudioBookSeries>()
            .HasOne(entity => entity.LinkedBookSeries)
            .WithMany()
            .HasForeignKey(entity => entity.LinkedBookSeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Book>()
            .HasOne(entity => entity.BookSeries)
            .WithMany()
            .HasForeignKey(entity => entity.BookSeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Movie>()
            .HasOne(entity => entity.MovieSeries)
            .WithMany()
            .HasForeignKey(entity => entity.MovieSeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TvSeason>()
            .HasOne(entity => entity.Series)
            .WithMany()
            .HasForeignKey(entity => entity.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TvEpisode>()
            .HasOne(entity => entity.Series)
            .WithMany()
            .HasForeignKey(entity => entity.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TvEpisode>()
            .HasOne(entity => entity.Season)
            .WithMany()
            .HasForeignKey(entity => entity.SeasonId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RadioEpisode>()
            .HasOne(entity => entity.Radio)
            .WithMany()
            .HasForeignKey(entity => entity.RadioId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MusicVideo>()
            .HasOne(entity => entity.LinkedMusic)
            .WithMany()
            .HasForeignKey(entity => entity.LinkedMusicId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MagazineIssue>()
            .HasOne(entity => entity.Magazine)
            .WithMany()
            .HasForeignKey(entity => entity.MagazineId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<NewspaperIssue>()
            .HasOne(entity => entity.Newspaper)
            .WithMany()
            .HasForeignKey(entity => entity.NewspaperId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<JournalIssue>()
            .HasOne(entity => entity.Journal)
            .WithMany()
            .HasForeignKey(entity => entity.JournalId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureApplicationState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>(builder =>
        {
            builder.ToTable("AppSettings");
            builder.HasKey(entity => entity.Key);
            builder.Property(entity => entity.Key).HasMaxLength(256);
            builder.Property(entity => entity.Value).HasMaxLength(65536);
            builder.HasIndex(entity => entity.Key).IsUnique();
        });

        modelBuilder.Entity<MediaLibraryRoot>(builder =>
        {
            builder.ToTable("MediaLibraryRoots");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Name).HasMaxLength(512).IsRequired();
            builder.Property(entity => entity.Path).HasMaxLength(2048).IsRequired();
            builder.Property(entity => entity.NormalizedPath).HasMaxLength(2048).IsRequired();
            builder.Property(entity => entity.LastError).HasMaxLength(4096);
            builder.HasIndex(entity => entity.NormalizedPath).IsUnique();
            builder.HasIndex(entity => entity.IsEnabled);
            builder.HasIndex(entity => entity.LastScanStatus);
        });

        modelBuilder.Entity<MediaLibraryScan>(builder =>
        {
            builder.ToTable("MediaLibraryScans");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.ErrorMessage).HasMaxLength(4096);
            builder.HasIndex(entity => entity.LibraryRootId);
            builder.HasIndex(entity => entity.StartedAt);
            builder.HasIndex(entity => entity.Status);
            builder.HasOne(entity => entity.LibraryRoot)
                .WithMany(entity => entity.Scans)
                .HasForeignKey(entity => entity.LibraryRootId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePlaylists(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Playlist>(builder =>
        {
            builder.ToTable("Playlists");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Name).HasMaxLength(512).IsRequired();
            builder.Property(entity => entity.SortName).HasMaxLength(512);
            builder.Property(entity => entity.Description).HasMaxLength(4096);
            builder.HasIndex(entity => entity.Name);
            builder.HasIndex(entity => entity.SortName);
        });

        modelBuilder.Entity<PlaylistItem>(builder =>
        {
            builder.ToTable("PlaylistItems");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Note).HasMaxLength(4096);
            builder.HasIndex(entity => entity.ItemId);
            builder.HasIndex(entity => new { entity.PlaylistId, entity.ItemId }).IsUnique();
            builder.HasIndex(entity => new { entity.PlaylistId, entity.SortOrder }).IsUnique();
            builder.HasOne(entity => entity.Playlist)
                .WithMany(entity => entity.Items)
                .HasForeignKey(entity => entity.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(entity => entity.Item)
                .WithMany()
                .HasForeignKey(entity => entity.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureUserMediaState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserMediaState>(builder =>
        {
            builder.ToTable("UserMediaStates", table =>
            {
                table.HasCheckConstraint(
                    "CK_UserMediaStates_UserRating",
                    "\"UserRating\" IS NULL OR (\"UserRating\" >= 1 AND \"UserRating\" <= 5)");
                table.HasCheckConstraint(
                    "CK_UserMediaStates_PlayCount",
                    "\"PlayCount\" >= 0");
            });
            builder.HasKey(entity => entity.ItemId);
            builder.HasIndex(entity => entity.IsFavorite);
            builder.HasIndex(entity => entity.LastPlayedAt);
            builder.HasOne(entity => entity.Item)
                .WithOne()
                .HasForeignKey<UserMediaState>(entity => entity.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ApplyAuditTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseItem>())
        {
            entry.Entity.NormalizedPath = PathNormalizer.NormalizeNullable(entry.Entity.Path);

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.DateCreated == default)
                {
                    entry.Entity.DateCreated = now;
                }

                entry.Entity.DateLastSaved ??= now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.DateLastSaved = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<LinkedText>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.DateModified = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<LinkedImage>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.DateModified = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AppSetting>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.DateModified = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MediaLibraryRoot>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedPath = PathNormalizer.Normalize(entry.Entity.Path);
                entry.Entity.DateLastSaved = now;

                if (entry.State == EntityState.Added && entry.Entity.DateCreated == default)
                {
                    entry.Entity.DateCreated = now;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<Playlist>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.DateCreated == default)
                {
                    entry.Entity.DateCreated = now;
                }

                entry.Entity.DateLastSaved ??= now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.DateLastSaved = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PlaylistItem>())
        {
            if (entry.State == EntityState.Added && entry.Entity.DateAdded == default)
            {
                entry.Entity.DateAdded = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserMediaState>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.PlayCount = Math.Max(0, entry.Entity.PlayCount);
                entry.Entity.DateModified = now;
            }
        }
    }
}
