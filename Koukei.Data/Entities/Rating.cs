namespace Koukei.Data.Entities;

public class Rating
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Source { get; set; } = string.Empty;

    public ICollection<LinkedText> Texts { get; set; } = [];

    public ICollection<ItemRating> ItemLinks { get; set; } = [];
}