using Domain.Common;

namespace Domain.Catalog;

public sealed class Item : AuditableEntity, ISoftDeletable
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    /// <summary>The part manufacturer (e.g. Bosch). Vehicle make/model lives in <see cref="Fitments"/>.</summary>
    public string? PartBrand { get; set; }
    public int? ReorderLevel { get; set; }
    public int? ReorderQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<ItemFitment> Fitments { get; set; } = [];
    public List<ItemImage> Images { get; set; } = [];

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
}
