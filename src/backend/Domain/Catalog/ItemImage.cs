using Domain.Common;

namespace Domain.Catalog;

public sealed class ItemImage : AuditableEntity
{
    public Guid ItemId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ThumbnailKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int ByteSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public string? AltText { get; set; }
}
