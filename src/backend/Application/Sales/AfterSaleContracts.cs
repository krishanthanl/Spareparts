using Application.Catalog;
using Domain.Sales;
using System.Text.Json.Serialization;

namespace Application.Sales;

// Inputs reject unknown members (as in 4a), so a client cannot send GST, totals or credit amounts the server calculates.

// ---- Delivery ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliverySettingsInput(bool DeliveryEnabled, bool PickupEnabled, decimal? FreeDeliveryThreshold, uint? Version = null);
public sealed record DeliverySettingsDto(bool DeliveryEnabled, bool PickupEnabled, decimal? FreeDeliveryThreshold, bool IsGstRegistered,
    TaxTreatment TaxTreatment, uint Version, DateTimeOffset? UpdatedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostcodeRangeInput(string From, string? To = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryZoneInput(string Name, decimal Charge, decimal? FreeDeliveryThreshold, decimal RuralSurcharge, string? ServicePromise,
    bool IsActive, IReadOnlyList<PostcodeRangeInput> Postcodes, uint? Version = null);
public sealed record PostcodeRangeDto(string From, string To);
public sealed record DeliveryZoneDto(Guid Id, string Name, decimal Charge, decimal? FreeDeliveryThreshold, decimal RuralSurcharge, string? ServicePromise,
    bool IsActive, IReadOnlyList<PostcodeRangeDto> Postcodes, uint Version, DateTimeOffset UpdatedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryQuoteInput(string Postcode, bool IsRural, decimal GoodsValue);
public sealed record DeliveryQuoteDto(bool Delivers, string? ZoneName, decimal? Charge, bool Free, decimal RuralSurcharge, string? ServicePromise, string? Message);

// ---- Shipments ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShipmentInput(string? Carrier, string? TrackingReference, string? Notes, IReadOnlyList<IssueLineInput>? Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DispatchInput(string? Carrier, string? TrackingReference, string IdempotencyKey);
public sealed record ShipmentLineDto(Guid OrderLineId, string Sku, string Description, decimal Quantity);
public sealed record ShipmentDto(Guid Id, string ShipmentNumber, Guid OrderId, string? InvoiceNumber, string? CustomerName, string? DeliveryAddress,
    ShipmentStatus Status, string? Carrier, string? TrackingReference, string? Notes, Guid? StockIssueId, string? IssueNumber, DateTimeOffset? DispatchedAt,
    string? DispatchedBy, DateTimeOffset? DeliveredAt, string? DeliveredBy, DateTimeOffset? CancelledAt, IReadOnlyList<ShipmentLineDto> Lines, uint Version,
    DateTimeOffset CreatedAt);
public sealed record ShipmentListRequest(int Page = 1, int PageSize = 25, ShipmentStatus? Status = null, Guid? OrderId = null, string? Q = null);

// ---- Returns ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnLineInput(Guid OrderLineId, decimal Quantity, ReturnDisposition Disposition);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesReturnInput(string Reason, IReadOnlyList<ReturnLineInput> Lines, string IdempotencyKey);
public sealed record SalesReturnLineDto(Guid OrderLineId, Guid ItemId, string Sku, string Description, decimal Quantity, ReturnDisposition Disposition,
    decimal UnitCost, decimal Credit);
public sealed record SalesReturnDto(Guid Id, string ReturnNumber, Guid OrderId, string? InvoiceNumber, string? CustomerName, string Reason,
    DateTimeOffset PostedAt, string PostedBy, Guid? CreditNoteId, string? CreditNoteNumber, decimal CreditTotal, IReadOnlyList<SalesReturnLineDto> Lines);
public sealed record ReturnListRequest(int Page = 1, int PageSize = 25, Guid? OrderId = null, DateOnly? From = null, DateOnly? To = null, string? Q = null);

/// <summary>What can still come back on each invoice line.</summary>
public sealed record ReturnableLineDto(Guid OrderLineId, int LineNo, string Sku, string Description, decimal Ordered, decimal Issued, decimal Returned,
    decimal Returnable, decimal LineTotal, decimal Credited);

// ---- Credit notes ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditLineInput(Guid OrderLineId, decimal Amount);
/// <summary>A price adjustment or goodwill credit with no goods coming back. Delivery credit is optional.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditNoteInput(string Reason, IReadOnlyList<CreditLineInput> Lines, decimal DeliveryAmount, string IdempotencyKey);
public sealed record CreditNoteLineDto(Guid? OrderLineId, string Description, decimal Quantity, decimal Amount);
public sealed record CreditNoteDto(Guid Id, string CreditNoteNumber, Guid OrderId, string? InvoiceNumber, string? CustomerName, Guid? SalesReturnId,
    string? ReturnNumber, string Reason, decimal Total, TaxTreatment TaxTreatment, decimal GstAmount, string Currency, DateTimeOffset IssuedAt,
    string IssuedBy, IReadOnlyList<CreditNoteLineDto> Lines);
public sealed record CreditNoteListRequest(int Page = 1, int PageSize = 25, Guid? OrderId = null, DateOnly? From = null, DateOnly? To = null, string? Q = null);

// ---- Refunds ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefundInput(decimal Amount, PaymentMethod Method, string Reason, Guid? CreditNoteId, bool ForCancellation, string IdempotencyKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteRefundInput(PaymentMethod Method, string EvidenceReference, string IdempotencyKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefundReasonInput(string Reason);
public sealed record RefundAllocationDto(Guid PaymentId, string ReceiptNumber, decimal Amount);
public sealed record RefundDto(Guid Id, string RefundNumber, Guid OrderId, string? InvoiceNumber, string? CustomerName, Guid? CreditNoteId,
    string? CreditNoteNumber, decimal Amount, PaymentMethod Method, string Reason, RefundStatus Status, string RequestedBy, Guid RequestedById,
    DateTimeOffset RequestedAt, string? ApprovedBy, DateTimeOffset? ApprovedAt, string? CompletedBy, DateTimeOffset? CompletedAt,
    string? EvidenceReference, string? FailureReason, IReadOnlyList<RefundAllocationDto> Allocations, uint Version);
public sealed record RefundListRequest(int Page = 1, int PageSize = 25, RefundStatus? Status = null, Guid? OrderId = null, string? Q = null);

// ---- Invoice balance (D13) ----
public sealed record InvoiceBalanceDto(Guid OrderId, string? InvoiceNumber, SalesOrderStatus Status, decimal Total, decimal Credits, decimal NetTotal,
    decimal Received, decimal Refunded, decimal NetPaid, decimal BalanceDue, decimal OwedToCustomer, decimal PendingRefunds, decimal Refundable,
    decimal RefundableOnCancel, bool CanCancel);

/// <summary>Everything after the sale for one invoice, for the order detail screen.</summary>
public sealed record OrderAfterSaleDto(InvoiceBalanceDto Balance, IReadOnlyList<ShipmentDto> Shipments, IReadOnlyList<ReturnableLineDto> Returnable,
    IReadOnlyList<SalesReturnDto> Returns, IReadOnlyList<CreditNoteDto> CreditNotes, IReadOnlyList<RefundDto> Refunds);

public interface IDeliveryService
{
    Task<DeliverySettingsDto> GetSettingsAsync(CancellationToken ct);
    Task<DeliverySettingsDto> UpdateSettingsAsync(DeliverySettingsInput input, CancellationToken ct);
    Task<IReadOnlyList<DeliveryZoneDto>> ListZonesAsync(CancellationToken ct);
    Task<DeliveryZoneDto> CreateZoneAsync(DeliveryZoneInput input, CancellationToken ct);
    Task<DeliveryZoneDto> UpdateZoneAsync(Guid id, DeliveryZoneInput input, CancellationToken ct);
    Task<DeliveryQuoteDto> QuoteAsync(DeliveryQuoteInput input, CancellationToken ct);
}

public interface IAfterSaleService
{
    Task<OrderAfterSaleDto> GetForOrderAsync(Guid orderId, CancellationToken ct);
    Task<InvoiceBalanceDto> BalanceAsync(Guid orderId, CancellationToken ct);

    Task<PagedResult<ShipmentDto>> ListShipmentsAsync(ShipmentListRequest request, CancellationToken ct);
    Task<ShipmentDto> GetShipmentAsync(Guid id, CancellationToken ct);
    Task<ShipmentDto> CreateShipmentAsync(Guid orderId, ShipmentInput input, CancellationToken ct);
    Task<ShipmentDto> DispatchAsync(Guid id, DispatchInput input, CancellationToken ct);
    Task<ShipmentDto> DeliverAsync(Guid id, CancellationToken ct);
    Task<ShipmentDto> CancelShipmentAsync(Guid id, CancellationToken ct);

    Task<PagedResult<SalesReturnDto>> ListReturnsAsync(ReturnListRequest request, CancellationToken ct);
    Task<SalesReturnDto> GetReturnAsync(Guid id, CancellationToken ct);
    Task<SalesReturnDto> CreateReturnAsync(Guid orderId, SalesReturnInput input, CancellationToken ct);

    Task<PagedResult<CreditNoteDto>> ListCreditNotesAsync(CreditNoteListRequest request, CancellationToken ct);
    Task<CreditNoteDto> GetCreditNoteAsync(Guid id, CancellationToken ct);
    Task<CreditNoteDto> CreateCreditNoteAsync(Guid orderId, CreditNoteInput input, CancellationToken ct);
    Task<(string FileName, byte[] Content)> CreditNotePdfAsync(Guid id, CancellationToken ct);

    Task<PagedResult<RefundDto>> ListRefundsAsync(RefundListRequest request, CancellationToken ct);
    Task<RefundDto> GetRefundAsync(Guid id, CancellationToken ct);
    Task<RefundDto> RequestRefundAsync(Guid orderId, RefundInput input, CancellationToken ct);
    Task<RefundDto> ApproveRefundAsync(Guid id, CancellationToken ct);
    Task<RefundDto> CompleteRefundAsync(Guid id, CompleteRefundInput input, CancellationToken ct);
    Task<RefundDto> FailRefundAsync(Guid id, RefundReasonInput input, CancellationToken ct);
    Task<RefundDto> CancelRefundAsync(Guid id, RefundReasonInput input, CancellationToken ct);
}
