using Domain.Common;

namespace Domain.Catalog;

/// <summary>
/// A vehicle an item fits. Years are inclusive; a missing YearFrom means "up to YearTo",
/// a missing YearTo means "YearFrom onwards", both missing means every year of the model.
/// </summary>
public sealed class ItemFitment : AuditableEntity
{
    public Guid ItemId { get; set; }
    public Guid VehicleModelId { get; set; }
    public VehicleModel? VehicleModel { get; set; }
    public short? YearFrom { get; set; }
    public short? YearTo { get; set; }

    public bool Fits(int year) => (YearFrom is null || year >= YearFrom) && (YearTo is null || year <= YearTo);
}
