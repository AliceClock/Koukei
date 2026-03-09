using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class MediaLibraryScan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LibraryRootId { get; set; }

    public MediaLibraryRoot LibraryRoot { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public LibraryScanStatus Status { get; set; } = LibraryScanStatus.Running;

    public int ItemsDiscovered { get; set; }

    public int ItemsAdded { get; set; }

    public int ItemsUpdated { get; set; }

    public int ItemsSkipped { get; set; }

    public int ItemsFailed { get; set; }

    public string? ErrorMessage { get; set; }
}
