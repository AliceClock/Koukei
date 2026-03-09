namespace Koukei.Data.Entities;

public class Studio
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public ICollection<LinkedText> Texts { get; set; } = [];

    public ICollection<LinkedImage> Images { get; set; } = [];

    public ICollection<ItemStudio> ItemLinks { get; set; } = [];
}