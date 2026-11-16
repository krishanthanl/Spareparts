using Domain.Common;

namespace Domain.Catalog;

public sealed class VehicleMake : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public List<VehicleModel> Models { get; set; } = [];
}
