using Api.Identity;
using Api.Validation;
using Application.Catalog;
using Application.Identity;
using Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerDto>> List([FromQuery] CustomerListRequest request, CancellationToken ct) => customers.ListAsync(request, ct);

    [HttpGet("{id:guid}")]
    public Task<CustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = PermissionKeys.CustomersManage)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerInput input, CancellationToken ct)
    {
        var created = await customers.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionKeys.CustomersManage)]
    public Task<CustomerDto> Update(Guid id, CustomerInput input, CancellationToken ct) => customers.UpdateAsync(id, input, ct);
}

[ApiController]
[Route("api/orders")]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class OrdersController(ISalesOrderService orders, IInvoicePdfService pdf, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderSummaryDto>> List([FromQuery] OrderListRequest request, CancellationToken ct) => orders.ListAsync(request, ct);

    [HttpGet("{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    [HttpGet("items")]
    public Task<IReadOnlyList<SaleItemDto>> Items([FromQuery] string? q, CancellationToken ct) => orders.ItemLookupAsync(q, ct);

    [HttpPost("preview")]
    public Task<OrderPreviewDto> Preview(OrderInput input, CancellationToken ct) => orders.PreviewAsync(input, ct);

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(OrderInput input, CancellationToken ct)
    {
        var created = await orders.CreateAsync(input, await Has(PermissionKeys.SalesDiscount), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<OrderDto> Update(Guid id, OrderInput input, CancellationToken ct) =>
        await orders.UpdateAsync(id, input, await Has(PermissionKeys.SalesDiscount), ct);

    [HttpPost("{id:guid}/confirm")]
    [NoRequestBody]
    public Task<OrderDto> Confirm(Guid id, CancellationToken ct) => orders.ConfirmAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<OrderDto> Cancel(Guid id, CancelOrderInput input, CancellationToken ct) =>
        await orders.CancelAsync(id, input, await Has(PermissionKeys.SalesCancel), ct);

    [HttpPost("{id:guid}/duplicate")]
    [NoRequestBody]
    public async Task<ActionResult<OrderDto>> Duplicate(Guid id, CancellationToken ct)
    {
        var created = await orders.DuplicateAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/issues")]
    [Authorize(Policy = PermissionKeys.SalesIssue)]
    public Task<OrderDto> Issue(Guid id, IssueInput input, CancellationToken ct) => orders.IssueAsync(id, input, ct);

    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = PermissionKeys.PaymentsRecord)]
    public Task<OrderDto> Pay(Guid id, PaymentInput input, CancellationToken ct) => orders.RecordPaymentAsync(id, input, ct);

    [HttpPost("quick-sale")]
    [Authorize(Policy = PermissionKeys.SalesIssue)]
    [Authorize(Policy = PermissionKeys.PaymentsRecord)]
    public async Task<OrderDto> QuickSale(QuickSaleInput input, CancellationToken ct) =>
        await orders.QuickSaleAsync(input, await Has(PermissionKeys.SalesDiscount), ct);

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (name, content) = await pdf.RenderAsync(id, ct);
        return File(content, "application/pdf", name);
    }

    private async Task<bool> Has(string permission) => (await authorization.AuthorizeAsync(User, permission)).Succeeded;
}

[ApiController]
[Authorize(Policy = PermissionKeys.SalesInvoice)]
public sealed class SalesDocumentsController(ISalesOrderService orders) : ControllerBase
{
    [HttpGet("api/stock-issues/{id:guid}")]
    public Task<StockIssueDto> Issue(Guid id, CancellationToken ct) => orders.GetIssueAsync(id, ct);

    [HttpPost("api/payments/{id:guid}/void")]
    [Authorize(Policy = PermissionKeys.PaymentsVoid)]
    public Task<OrderDto> Void(Guid id, VoidPaymentInput input, CancellationToken ct) => orders.VoidPaymentAsync(id, input, ct);
}

[ApiController]
[Route("api/reports")]
[Authorize(Policy = PermissionKeys.SalesReports)]
public sealed class SalesReportsController(ITakingsReportService takings) : ControllerBase
{
    [HttpGet("daily-takings")]
    public async Task<IActionResult> DailyTakings([FromQuery] TakingsRequest request, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await takings.DailyAsync(request, ct);
        if (format != "csv") return Ok(report);
        var csv = new System.Text.StringBuilder("date,received_at,receipt,invoice,customer,method,amount,reference,received_by,void\n");
        // Quote every text cell and defuse spreadsheet formulas (=, +, -, @).
        static string Cell(string? v) => v is null ? "" : "\"" + (v.Length > 0 && "=+-@".Contains(v[0]) ? "'" + v : v).Replace("\"", "\"\"") + "\"";
        foreach (var day in report.Days)
            foreach (var p in day.Payments)
                csv.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{day.Date:yyyy-MM-dd},{p.ReceivedAt:O},{Cell(p.ReceiptNumber)},{Cell(p.InvoiceNumber)},{Cell(p.CustomerName)},{p.Method},{p.Amount:0.00},{Cell(p.Reference)},{Cell(p.ReceivedBy)},{(p.IsVoid ? "yes" : "no")}\n");
        // Refunds paid out are money leaving the till or bank: listed as negative rows (ADR 0006, D12).
        foreach (var day in report.Days)
            foreach (var r in day.Refunds ?? [])
                csv.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{day.Date:yyyy-MM-dd},{r.CompletedAt:O},{Cell(r.RefundNumber)},{Cell(r.InvoiceNumber)},{Cell(r.CustomerName)},{r.Method},{-r.Amount:0.00},{Cell(r.EvidenceReference)},{Cell(r.CompletedBy)},refund\n");
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"takings-{request.From:yyyy-MM-dd}-to-{request.To:yyyy-MM-dd}.csv");
    }
}
