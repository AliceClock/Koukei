namespace Koukei.Data.Entities;

public class DocumentInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemId { get; set; }

    public Document.Document Item { get; set; } = null!;

    public int? PageCount { get; set; }

    public int? WordCount { get; set; }

    public string? Language { get; set; }

    public string? Format { get; set; }

    public bool? IsScanned { get; set; } = false;
}
