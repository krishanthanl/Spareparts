using Domain.Common;

namespace Domain.Catalog;

public sealed class VehicleModel : AuditableEntity
{
    public Guid MakeId { get; set; }
    public VehicleMake? Make { get; set; }
    public string Name { get; set; } = string.Empty;
}
