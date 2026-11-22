using System.Text.Json.Serialization;
using Application.Catalog;
using Application.Common;
using Application.Sales;
using FluentValidation;

namespace Application.Checkout;

public enum CheckoutFulfillment { Pickup, Delivery }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckoutQuoteRequest(IReadOnlyList<PublicCartLine> Lines,
    CheckoutFulfillment Fulfillment, string? Postcode, bool IsRural)
{
    [JsonRequired]
    public CheckoutFulfillment Fulfillment { get; init; } = Fulfillment;
}

public sealed record CheckoutQuoteLine(Guid ItemId, string Name, int Quantity, decimal UnitPrice, decimal Total);

public sealed record CheckoutQuote(IReadOnlyList<CheckoutQuoteLine> Lines, decimal Subtotal,
    decimal DeliveryCharge, decimal Total, string Currency, string? DeliveryEstimate);

public sealed class CheckoutQuoteRequestValidator : AbstractValidator<CheckoutQuoteRequest>
{
    public CheckoutQuoteRequestValidator()
    {
        RuleFor(x => new PublicCartRequest(x.Lines)).SetValidator(new PublicCartRequestValidator());
        RuleFor(x => x.Fulfillment).IsInEnum();
        RuleFor(x => x.Postcode).Must(value => AfterSaleMath.TryParsePostcode(value, out _))
            .WithMessage("Use a 4-digit NZ postcode.").When(x => x.Fulfillment == CheckoutFulfillment.Delivery);
    }
}

public sealed class CheckoutQuoteService(IPublicCatalogService catalog, IDeliveryService delivery)
{
    public async Task<CheckoutQuote> QuoteAsync(CheckoutQuoteRequest request, CancellationToken ct)
    {
        var results = await catalog.ValidateCartAsync(new PublicCartRequest(request.Lines), ct);
        var lines = new List<CheckoutQuoteLine>(request.Lines.Count);
        foreach (var (line, result) in request.Lines.Zip(results))
        {
            if (!result.Accepted || result.Item is not { Price: decimal price } item)
                throw new ConflictException("The cart has unavailable items. Review it before requesting another quote.");

            lines.Add(new CheckoutQuoteLine(item.Id, item.Name, line.Quantity, price, SalesMath.Money(price * line.Quantity)));
        }

        var subtotal = lines.Sum(x => x.Total);
        if (request.Fulfillment == CheckoutFulfillment.Pickup)
        {
            if (!(await delivery.GetSettingsAsync(ct)).PickupEnabled)
                throw new ConflictException("Pickup is currently unavailable.");

            return new CheckoutQuote(lines, subtotal, 0, subtotal, "NZD", null);
        }

        var quote = await delivery.QuoteAsync(new DeliveryQuoteInput(request.Postcode!, request.IsRural, subtotal), ct);
        if (!quote.Delivers || quote.Charge is not decimal charge)
            throw new ConflictException(quote.Message ?? "Delivery is unavailable for this address.");

        return new CheckoutQuote(lines, subtotal, charge, SalesMath.Money(subtotal + charge), "NZD", quote.ServicePromise);
    }
}
