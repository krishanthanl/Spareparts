using Application.Checkout;
using Application.Common;
using Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Route("api/checkout")]
[AllowAnonymous]
[EnableRateLimiting(CheckoutController.RateLimit)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CheckoutQuoteService checkout, ICheckoutOrders orders, IConfiguration configuration, IInvoicePdfService pdf) : ControllerBase
{
    public const string RateLimit = "checkout";
    private string? BankInstructions => configuration["Checkout:BankTransferInstructions"];
    private bool BankTransferEnabled => configuration.GetValue<bool>("Checkout:BankTransferEnabled") && !string.IsNullOrWhiteSpace(BankInstructions);

    [HttpGet("options")]
    public IActionResult Options() => Ok(new { bankTransfer = BankTransferEnabled });

    [HttpPost("quote")]
    public Task<CheckoutQuote> Quote(CheckoutQuoteRequest request, CancellationToken ct) => checkout.QuoteAsync(request, ct);

    [HttpPost("orders")]
    public async Task<ActionResult<CheckoutOrderReceipt>> Place(CheckoutOrderRequest request, CancellationToken ct)
    {
        if (!BankTransferEnabled)
            return Problem(statusCode: 503, title: "Online ordering is currently unavailable.");
        return (await orders.PlaceAsync(request, ct)) with { BankInstructions = BankInstructions };
    }

    [HttpPost("orders/{id:guid}/access")]
    public async Task<CheckoutOrderReceipt> Track(Guid id, [FromHeader(Name = "Idempotency-Key")] Guid requestKey, CancellationToken ct) =>
        (await orders.TrackAsync(id, requestKey, ct)) with { BankInstructions = BankInstructions };

    [HttpPost("orders/{id:guid}/invoice")]
    public async Task<IActionResult> Invoice(Guid id, [FromHeader(Name = "Idempotency-Key")] Guid requestKey, CancellationToken ct)
    {
        var order = await orders.TrackAsync(id, requestKey, ct);
        if (order.InvoiceNumber is null) throw new NotFoundException("Invoice not found.");
        var (name, content) = await pdf.RenderAsync(id, ct);
        return File(content, "application/pdf", name);
    }
}
