using Api.Validation;
using Application.Catalog;
using Application.Identity;
using Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// Phase 4b (ADR 0006). Settings and fulfilment are authorised separately; refunds split create / approve / execute.

[ApiController]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class DeliveryController(IDeliveryService delivery) : ControllerBase
{
    [HttpGet("api/delivery-settings")]
    public Task<DeliverySettingsDto> Settings(CancellationToken ct) => delivery.GetSettingsAsync(ct);

    [HttpPut("api/delivery-settings")]
    [Authorize(Policy = PermissionKeys.DeliveryConfigure)]
    public Task<DeliverySettingsDto> UpdateSettings(DeliverySettingsInput input, CancellationToken ct) => delivery.UpdateSettingsAsync(input, ct);

    [HttpGet("api/delivery-zones")]
    public Task<IReadOnlyList<DeliveryZoneDto>> Zones(CancellationToken ct) => delivery.ListZonesAsync(ct);

    [HttpPost("api/delivery-zones")]
    [Authorize(Policy = PermissionKeys.DeliveryConfigure)]
    public Task<DeliveryZoneDto> CreateZone(DeliveryZoneInput input, CancellationToken ct) => delivery.CreateZoneAsync(input, ct);

    [HttpPut("api/delivery-zones/{id:guid}")]
    [Authorize(Policy = PermissionKeys.DeliveryConfigure)]
    public Task<DeliveryZoneDto> UpdateZone(Guid id, DeliveryZoneInput input, CancellationToken ct) => delivery.UpdateZoneAsync(id, input, ct);

    [HttpPost("api/delivery-quote")]
    public Task<DeliveryQuoteDto> Quote(DeliveryQuoteInput input, CancellationToken ct) => delivery.QuoteAsync(input, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class OrderAfterSaleController(IAfterSaleService afterSale) : ControllerBase
{
    [HttpGet("api/orders/{id:guid}/after-sale")]
    public Task<OrderAfterSaleDto> Get(Guid id, CancellationToken ct) => afterSale.GetForOrderAsync(id, ct);

    [HttpGet("api/orders/{id:guid}/balance")]
    public Task<InvoiceBalanceDto> Balance(Guid id, CancellationToken ct) => afterSale.BalanceAsync(id, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.FulfillmentUpdate)]
public sealed class ShipmentsController(IAfterSaleService afterSale) : ControllerBase
{
    [HttpGet("api/shipments")]
    public Task<PagedResult<ShipmentDto>> List([FromQuery] ShipmentListRequest request, CancellationToken ct) => afterSale.ListShipmentsAsync(request, ct);

    [HttpGet("api/shipments/{id:guid}")]
    public Task<ShipmentDto> Get(Guid id, CancellationToken ct) => afterSale.GetShipmentAsync(id, ct);

    [HttpPost("api/orders/{id:guid}/shipments")]
    public Task<ShipmentDto> Create(Guid id, ShipmentInput input, CancellationToken ct) => afterSale.CreateShipmentAsync(id, input, ct);

    [HttpPost("api/shipments/{id:guid}/dispatch")]
    public Task<ShipmentDto> Dispatch(Guid id, DispatchInput input, CancellationToken ct) => afterSale.DispatchAsync(id, input, ct);

    [HttpPost("api/shipments/{id:guid}/deliver")]
    [NoRequestBody]
    public Task<ShipmentDto> Deliver(Guid id, CancellationToken ct) => afterSale.DeliverAsync(id, ct);

    [HttpPost("api/shipments/{id:guid}/cancel")]
    [NoRequestBody]
    public Task<ShipmentDto> Cancel(Guid id, CancellationToken ct) => afterSale.CancelShipmentAsync(id, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.SalesReturn)]
public sealed class SalesReturnsController(IAfterSaleService afterSale) : ControllerBase
{
    [HttpGet("api/sales-returns")]
    public Task<PagedResult<SalesReturnDto>> List([FromQuery] ReturnListRequest request, CancellationToken ct) => afterSale.ListReturnsAsync(request, ct);

    [HttpGet("api/sales-returns/{id:guid}")]
    public Task<SalesReturnDto> Get(Guid id, CancellationToken ct) => afterSale.GetReturnAsync(id, ct);

    /// <summary>Records the return and its credit note together.</summary>
    [HttpPost("api/orders/{id:guid}/returns")]
    public Task<SalesReturnDto> Create(Guid id, SalesReturnInput input, CancellationToken ct) => afterSale.CreateReturnAsync(id, input, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class CreditNotesController(IAfterSaleService afterSale) : ControllerBase
{
    [HttpGet("api/credit-notes")]
    public Task<PagedResult<CreditNoteDto>> List([FromQuery] CreditNoteListRequest request, CancellationToken ct) => afterSale.ListCreditNotesAsync(request, ct);

    [HttpGet("api/credit-notes/{id:guid}")]
    public Task<CreditNoteDto> Get(Guid id, CancellationToken ct) => afterSale.GetCreditNoteAsync(id, ct);

    [HttpGet("api/credit-notes/{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (name, content) = await afterSale.CreditNotePdfAsync(id, ct);
        return File(content, "application/pdf", name);
    }

    [HttpPost("api/orders/{id:guid}/credit-notes")]
    [Authorize(Policy = PermissionKeys.CreditNotesIssue)]
    public Task<CreditNoteDto> Create(Guid id, CreditNoteInput input, CancellationToken ct) => afterSale.CreateCreditNoteAsync(id, input, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.RefundsCreate)]
public sealed class RefundsController(IAfterSaleService afterSale) : ControllerBase
{
    [HttpGet("api/refunds")]
    public Task<PagedResult<RefundDto>> List([FromQuery] RefundListRequest request, CancellationToken ct) => afterSale.ListRefundsAsync(request, ct);

    [HttpGet("api/refunds/{id:guid}")]
    public Task<RefundDto> Get(Guid id, CancellationToken ct) => afterSale.GetRefundAsync(id, ct);

    [HttpPost("api/orders/{id:guid}/refunds")]
    public Task<RefundDto> Create(Guid id, RefundInput input, CancellationToken ct) => afterSale.RequestRefundAsync(id, input, ct);

    [HttpPost("api/refunds/{id:guid}/approve")]
    [Authorize(Policy = PermissionKeys.RefundsApprove)]
    [NoRequestBody]
    public Task<RefundDto> Approve(Guid id, CancellationToken ct) => afterSale.ApproveRefundAsync(id, ct);

    [HttpPost("api/refunds/{id:guid}/complete")]
    [Authorize(Policy = PermissionKeys.RefundsExecute)]
    public Task<RefundDto> Complete(Guid id, CompleteRefundInput input, CancellationToken ct) => afterSale.CompleteRefundAsync(id, input, ct);

    [HttpPost("api/refunds/{id:guid}/fail")]
    [Authorize(Policy = PermissionKeys.RefundsExecute)]
    public Task<RefundDto> Fail(Guid id, RefundReasonInput input, CancellationToken ct) => afterSale.FailRefundAsync(id, input, ct);

    [HttpPost("api/refunds/{id:guid}/cancel")]
    [Authorize(Policy = PermissionKeys.RefundsApprove)]
    public Task<RefundDto> Cancel(Guid id, RefundReasonInput input, CancellationToken ct) => afterSale.CancelRefundAsync(id, input, ct);
}
