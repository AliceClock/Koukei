using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class MediaLibraryRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string NormalizedPath { get; set; } = string.Empty;

    public SourceType SourceType { get; set; } = SourceType.FileSystem;

    public bool IncludeSubdirectories { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DateLastSaved { get; set; }

    public DateTimeOffset? LastScanStartedAt { get; set; }

    public DateTimeOffset? LastScanCompletedAt { get; set; }

    public LibraryScanStatus LastScanStatus { get; set; } = LibraryScanStatus.NeverScanned;

    public string? LastError { get; set; }

    public ICollection<MediaLibraryScan> Scans { get; set; } = [];
}
