using Domain.Common;

namespace Domain.Sales;

// ---------------- Delivery (ADR 0006, D1-D4) ----------------

/// <summary>Single row of shop-wide delivery options. Edits never change orders already priced.</summary>
public sealed class DeliverySettings : AuditableEntity
{
    public bool DeliveryEnabled { get; set; } = true;
    public bool PickupEnabled { get; set; } = true;
    /// <summary>Goods value (after discounts) at or above which delivery is free, unless the zone sets its own.</summary>
    public decimal? FreeDeliveryThreshold { get; set; }
    public uint Version { get; set; }
}

public sealed class DeliveryZone : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Charge { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public decimal RuralSurcharge { get; set; }
    public string? ServicePromise { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }
    public List<DeliveryZonePostcode> Postcodes { get; } = [];
}

/// <summary>An inclusive range of 4-digit NZ postcodes. A single postcode has From = To.</summary>
public sealed class DeliveryZonePostcode : AuditableEntity
{
    public Guid ZoneId { get; set; }
    public int From { get; set; }
    public int To { get; set; }
}

// ---------------- Shipments (D5) ----------------

public enum ShipmentStatus
{
    Pending = 1,
    Dispatched = 2,
    Delivered = 3,
    Cancelled = 4,
}

/// <summary>Goods sent from the shop. Dispatch posts the stock issue; delivery only records the date.</summary>
public sealed class Shipment : AuditableEntity
{
    public string ShipmentNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string? Carrier { get; set; }
    public string? TrackingReference { get; set; }
    public string? Notes { get; set; }
    public Guid? StockIssueId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? DispatchedBy { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? DeliveredBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public uint Version { get; set; }
    public List<ShipmentLine> Lines { get; } = [];
}

public sealed class ShipmentLine : AuditableEntity
{
    public Guid ShipmentId { get; set; }
    public Guid OrderLineId { get; set; }
    public decimal Quantity { get; set; }
}

// ---------------- Returns (D6) ----------------

public enum ReturnDisposition
{
    /// <summary>Back on the shelf: a Return movement adds it to saleable stock.</summary>
    Restock = 1,
    /// <summary>Damaged or not resaleable: no stock movement.</summary>
    WriteOff = 2,
}

/// <summary>Goods brought back against one invoice. Append-only; always produces a credit note.</summary>
public sealed class SalesReturn : AuditableEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset PostedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public List<SalesReturnLine> Lines { get; } = [];
}

public sealed class SalesReturnLine : AuditableEntity
{
    public Guid ReturnId { get; set; }
    public Guid OrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public ReturnDisposition Disposition { get; set; }
    /// <summary>The average cost at which the goods were issued; restock comes back at this cost.</summary>
    public decimal UnitCost { get; set; }
    public Guid? MovementId { get; set; }
}

// ---------------- Credit notes (D7, D8) ----------------

/// <summary>Lowers what the customer owes on an invoice. Not a cash refund. Append-only.</summary>
public sealed class CreditNote : AuditableEntity
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid? SalesReturnId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.NotRegistered;
    public decimal GstAmount { get; set; }
    public string Currency { get; set; } = "NZD";
    public DateTimeOffset IssuedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public List<CreditNoteLine> Lines { get; } = [];
}

public sealed class CreditNoteLine : AuditableEntity
{
    public Guid CreditNoteId { get; set; }
    /// <summary>Null for the delivery-charge line.</summary>
    public Guid? OrderLineId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Units credited because they came back; zero for a price adjustment.</summary>
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}

// ---------------- Refunds (D9, D10) ----------------

public enum RefundStatus
{
    Requested = 1,
    Approved = 2,
    /// <summary>Reserved for gateway refunds in Phase 6.</summary>
    Processing = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
}

/// <summary>Money paid back to a customer, by hand in Phase 4. Only the status columns change after creation.</summary>
public sealed class Refund : AuditableEntity
{
    public string RefundNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid? CreditNoteId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Requested;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? EvidenceReference { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CompletionKey { get; set; }
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.NotRegistered;
    public string Currency { get; set; } = "NZD";
    public uint Version { get; set; }
    public List<RefundAllocation> Allocations { get; } = [];
}

/// <summary>Which original receipt the refunded money came from.</summary>
public sealed class RefundAllocation : AuditableEntity
{
    public Guid RefundId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
}
