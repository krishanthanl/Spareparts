using Domain.Common;

namespace Domain.Sales;

public enum SalesOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    /// <summary>Fully paid and fully issued. Goes back to Confirmed if a payment is voided.</summary>
    Completed = 3,
    Cancelled = 4,
}

public enum PaymentStatus
{
    Unpaid = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overpaid = 4,
}

public enum FulfillmentStatus
{
    NotIssued = 1,
    PartiallyIssued = 2,
    Issued = 3,
}

public enum SalesSource
{
    Counter = 1,
    Telephone = 2,
    Admin = 3,
    Online = 4,
}

public enum PaymentTerms
{
    Immediate = 1,
    /// <summary>Customer pays when the goods arrive. A collection arrangement, not a payment.</summary>
    OnDelivery = 2,
}

public enum DeliveryMethod
{
    Pickup = 1,
    Delivery = 2,
}

public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    BankTransfer = 3,
    Other = 4,
}

/// <summary>The shop is not GST-registered. Stored on every document so a future registration never rewrites history.</summary>
public enum TaxTreatment
{
    NotRegistered = 1,
}

/// <summary>Phone, walk-in and (from Phase 6) online customers. Orders keep their own snapshot of these details.</summary>
public sealed class Customer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? BillingAddress { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public uint Version { get; set; }
}

/// <summary>
/// One document from draft to invoice (ADR 0005, D1). It gets its INV- number on Confirm; after that,
/// lines, prices and snapshots are frozen by the database and only server-calculated state changes.
/// </summary>
public sealed class SalesOrder : AuditableEntity
{
    public string? InvoiceNumber { get; set; }
    public SalesSource Source { get; set; } = SalesSource.Counter;
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.NotIssued;
    public PaymentTerms PaymentTerms { get; set; } = PaymentTerms.Immediate;
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerCompany { get; set; }
    public string? BillingAddress { get; set; }
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Pickup;
    public string? DeliveryAddress { get; set; }
    public decimal DeliveryCharge { get; set; }
    // Delivery snapshot (ADR 0006, D4): what the quote was based on, kept even if zones change later.
    public string? DeliveryPostcode { get; set; }
    public bool DeliveryIsRural { get; set; }
    public string? DeliveryZoneName { get; set; }
    public string? DeliveryPromise { get; set; }
    /// <summary>Set when staff charged something other than the zone quote.</summary>
    public string? DeliveryChargeReason { get; set; }
    /// <summary>Sum of credit notes issued against this invoice.</summary>
    public decimal CreditTotal { get; set; }
    /// <summary>Sum of succeeded refunds.</summary>
    public decimal RefundedTotal { get; set; }
    /// <summary>Sum of quantity x unit price, per line rounded to cents.</summary>
    public decimal Subtotal { get; set; }
    public decimal LineDiscountTotal { get; set; }
    public decimal OrderDiscount { get; set; }
    public string? DiscountReason { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.NotRegistered;
    public decimal GstAmount { get; set; }
    public string Currency { get; set; } = "NZD";
    public string? Notes { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
    public uint Version { get; set; }
    public List<SalesOrderLine> Lines { get; } = [];

    /// <summary>What the customer must pay after credit notes.</summary>
    public decimal NetTotal => Total - CreditTotal;
    /// <summary>Money kept by the shop: receipts minus voids minus refunds paid out.</summary>
    public decimal NetPaid => AmountPaid - RefundedTotal;
    /// <summary>Positive: the customer owes the shop. Negative: the shop owes the customer.</summary>
    public decimal Balance => Status == SalesOrderStatus.Cancelled ? -NetPaid : NetTotal - NetPaid;
}

public sealed class SalesOrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    /// <summary>The item's selling price when the line was priced.</summary>
    public decimal ListPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    /// <summary>This line's cents of the order-level discount (largest-remainder allocation).</summary>
    public decimal OrderDiscountShare { get; set; }
    public decimal LineTotal { get; set; }
    public string? DiscountReason { get; set; }
}

/// <summary>Goods handed over or dispatched. Each line posts exactly one Issue movement. Append-only.</summary>
public sealed class StockIssue : AuditableEntity
{
    public string IssueNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    /// <summary>Set when the goods left as a shipment (ADR 0006, D5). Unique: a shipment issues stock once.</summary>
    public Guid? ShipmentId { get; set; }
    public List<StockIssueLine> Lines { get; } = [];
}

public sealed class StockIssueLine : AuditableEntity
{
    public Guid IssueId { get; set; }
    public Guid OrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public Guid MovementId { get; set; }
}

/// <summary>
/// Money received. Append-only: a mistake is corrected by a void row with the opposite amount
/// that points at the original (<see cref="VoidsPaymentId"/>).
/// </summary>
public sealed class Payment : AuditableEntity
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    /// <summary>Set by the database clock.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? VoidsPaymentId { get; set; }
    public string? VoidReason { get; set; }
}
