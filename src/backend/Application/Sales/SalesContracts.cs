using Application.Catalog;
using Domain.Sales;
using System.Text.Json.Serialization;

namespace Application.Sales;

// Inputs reject unknown members, so a client cannot slip in GST or total fields (D11): the server calculates them.

// ---- Customers ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerInput(string Name, string? Phone, string? Email, string? Company, string? BillingAddress, string? DeliveryAddress,
    string? Notes, bool IsActive = true, uint? Version = null);
public sealed record CustomerDto(Guid Id, string Name, string? Phone, string? Email, string? Company, string? BillingAddress, string? DeliveryAddress,
    string? Notes, bool IsActive, uint Version, DateTimeOffset UpdatedAt);
public sealed record CustomerListRequest(int Page = 1, int PageSize = 25, string? Q = null, bool IncludeInactive = false);

// ---- Orders / invoices ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderLineInput(Guid ItemId, decimal Quantity, decimal? UnitPrice = null, decimal? DiscountAmount = null,
    decimal? DiscountPercent = null, string? DiscountReason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderInput(
    SalesSource Source,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? CustomerCompany,
    string? BillingAddress,
    PaymentTerms PaymentTerms,
    DeliveryMethod DeliveryMethod,
    string? DeliveryAddress,
    decimal DeliveryCharge,
    decimal OrderDiscount,
    string? DiscountReason,
    string? Notes,
    IReadOnlyList<OrderLineInput> Lines,
    uint? Version = null,
    string? DeliveryPostcode = null,
    bool DeliveryIsRural = false,
    string? DeliveryChargeReason = null);

public sealed record OrderLineDto(Guid Id, int LineNo, Guid ItemId, string Sku, string Description, decimal Quantity, decimal ListPrice, decimal UnitPrice,
    decimal LineDiscount, decimal OrderDiscountShare, decimal LineTotal, string? DiscountReason, decimal QtyIssued, decimal QtyToIssue, decimal? Available);
public sealed record StockIssueLineDto(Guid OrderLineId, Guid ItemId, string Sku, decimal Quantity);
public sealed record StockIssueDto(Guid Id, string IssueNumber, Guid OrderId, DateTimeOffset PostedAt, string PostedBy, IReadOnlyList<StockIssueLineDto> Lines);
public sealed record PaymentDto(Guid Id, string ReceiptNumber, Guid OrderId, PaymentMethod Method, decimal Amount, string? Reference,
    DateTimeOffset ReceivedAt, string ReceivedBy, Guid? VoidsPaymentId, string? VoidReason, bool IsVoided);
public sealed record OrderDto(
    Guid Id, string? InvoiceNumber, SalesSource Source, SalesOrderStatus Status, PaymentStatus PaymentStatus, FulfillmentStatus FulfillmentStatus,
    PaymentTerms PaymentTerms, Guid? CustomerId, string? CustomerName, string? CustomerPhone, string? CustomerEmail, string? CustomerCompany,
    string? BillingAddress, DeliveryMethod DeliveryMethod, string? DeliveryAddress, decimal DeliveryCharge, decimal Subtotal, decimal LineDiscountTotal,
    decimal OrderDiscount, string? DiscountReason, decimal Total, decimal AmountPaid, decimal BalanceDue, TaxTreatment TaxTreatment, decimal GstAmount,
    string Currency, string? Notes, DateTimeOffset? ConfirmedAt, string? ConfirmedBy, DateTimeOffset? CancelledAt, string? CancelReason,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<StockIssueDto> Issues, IReadOnlyList<PaymentDto> Payments, uint Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, decimal CreditTotal = 0, decimal RefundedTotal = 0, string? DeliveryPostcode = null, bool DeliveryIsRural = false,
    string? DeliveryZoneName = null, string? DeliveryPromise = null, string? DeliveryChargeReason = null);
public sealed record OrderSummaryDto(Guid Id, string? InvoiceNumber, SalesSource Source, SalesOrderStatus Status, PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus, string? CustomerName, string? CustomerPhone, decimal Total, decimal AmountPaid, decimal BalanceDue,
    DateTimeOffset? ConfirmedAt, DateTimeOffset UpdatedAt);
public sealed record OrderListRequest(int Page = 1, int PageSize = 25, string? Q = null, SalesOrderStatus? Status = null, PaymentStatus? PaymentStatus = null,
    FulfillmentStatus? FulfillmentStatus = null, SalesSource? Source = null, DateOnly? From = null, DateOnly? To = null, string? Sort = null);

/// <summary>Server-calculated totals for an unsaved draft, so the editor never does its own money maths.</summary>
public sealed record OrderPreviewDto(IReadOnlyList<PricedLineDto> Lines, decimal Subtotal, decimal LineDiscountTotal, decimal OrderDiscount,
    decimal DeliveryCharge, decimal Total, bool NeedsDiscountPermission, DeliveryQuoteDto? DeliveryQuote = null);
public sealed record PricedLineDto(Guid ItemId, string Sku, string Description, decimal Quantity, decimal ListPrice, decimal UnitPrice,
    decimal LineDiscount, decimal OrderDiscountShare, decimal LineTotal, decimal Available);

public sealed record SaleItemDto(Guid ItemId, string Sku, string Name, decimal? SellingPrice, decimal OnHand, decimal Reserved, decimal Available);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelOrderInput(string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IssueLineInput(Guid OrderLineId, decimal Quantity);
/// <summary>No lines means "issue everything still to issue".</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IssueInput(IReadOnlyList<IssueLineInput>? Lines, string? IdempotencyKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentInput(PaymentMethod Method, decimal Amount, string? Reference, string IdempotencyKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidPaymentInput(string Reason, string IdempotencyKey);
/// <summary>Counter sale: create, confirm, issue everything and take payment in one transaction. Amount defaults to the total.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuickSaleInput(OrderInput Order, PaymentMethod Method, decimal? Amount, string? Reference, string IdempotencyKey);

// ---- Daily takings ----
public sealed record TakingsRequest(DateOnly From, DateOnly To);
public sealed record TakingsPaymentRow(Guid PaymentId, string ReceiptNumber, string? InvoiceNumber, Guid OrderId, string? CustomerName, PaymentMethod Method,
    decimal Amount, string? Reference, DateTimeOffset ReceivedAt, string ReceivedBy, bool IsVoid);
/// <summary>Net = received - voided - refunds paid out by this method.</summary>
public sealed record TakingsMethodTotal(PaymentMethod Method, decimal Received, decimal Voided, decimal Net, decimal Refunded = 0);
/// <summary>A refund paid out (succeeded) on this shop day (ADR 0006, D12).</summary>
public sealed record TakingsRefundRow(Guid RefundId, string RefundNumber, string? InvoiceNumber, Guid OrderId, string? CustomerName, PaymentMethod Method,
    decimal Amount, string? EvidenceReference, DateTimeOffset CompletedAt, string CompletedBy);
/// <summary>Credit notes are listed for reconciliation only: they are not money in or out.</summary>
public sealed record TakingsDay(DateOnly Date, IReadOnlyList<TakingsMethodTotal> Methods, decimal Received, decimal Voided, decimal RefundsPaid, decimal Net,
    IReadOnlyList<TakingsPaymentRow> Payments, IReadOnlyList<TakingsRefundRow>? Refunds = null, decimal CreditsIssued = 0);
public sealed record TakingsReport(DateOnly From, DateOnly To, IReadOnlyList<TakingsDay> Days, IReadOnlyList<TakingsMethodTotal> Methods, decimal Received,
    decimal Voided, decimal RefundsPaid, decimal Net, decimal CreditsIssued = 0);

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(CustomerListRequest request, CancellationToken ct);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct);
    Task<CustomerDto> CreateAsync(CustomerInput input, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(Guid id, CustomerInput input, CancellationToken ct);
}

public interface ISalesOrderService
{
    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListRequest request, CancellationToken ct);
    Task<OrderDto> GetAsync(Guid id, CancellationToken ct);
    Task<OrderPreviewDto> PreviewAsync(OrderInput input, CancellationToken ct);
    Task<IReadOnlyList<SaleItemDto>> ItemLookupAsync(string? q, CancellationToken ct);
    Task<OrderDto> CreateAsync(OrderInput input, bool canDiscount, CancellationToken ct);
    Task<OrderDto> UpdateAsync(Guid id, OrderInput input, bool canDiscount, CancellationToken ct);
    Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct);
    /// <summary>Anyone who can invoice may discard a draft; cancelling a confirmed invoice needs sales.cancel.</summary>
    Task<OrderDto> CancelAsync(Guid id, CancelOrderInput input, bool canCancelConfirmed, CancellationToken ct);
    Task<OrderDto> DuplicateAsync(Guid id, CancellationToken ct);
    Task<OrderDto> IssueAsync(Guid id, IssueInput input, CancellationToken ct);
    Task<OrderDto> RecordPaymentAsync(Guid id, PaymentInput input, CancellationToken ct);
    Task<OrderDto> VoidPaymentAsync(Guid paymentId, VoidPaymentInput input, CancellationToken ct);
    Task<OrderDto> QuickSaleAsync(QuickSaleInput input, bool canDiscount, CancellationToken ct);
    Task<StockIssueDto> GetIssueAsync(Guid id, CancellationToken ct);
}

public interface ITakingsReportService
{
    Task<TakingsReport> DailyAsync(TakingsRequest request, CancellationToken ct);
}

public interface IInvoicePdfService
{
    /// <summary>Renders a confirmed invoice from its stored snapshot.</summary>
    Task<(string FileName, byte[] Content)> RenderAsync(Guid orderId, CancellationToken ct);
}

/// <summary>Shop details printed on invoices (configuration section "Shop").</summary>
public sealed class ShopSettings
{
    public const string SectionName = "Shop";
    public string TradingName { get; set; } = "Spare Parts";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? BankAccount { get; set; }
    public string PaymentTerms { get; set; } = "Payment due on receipt.";
}
