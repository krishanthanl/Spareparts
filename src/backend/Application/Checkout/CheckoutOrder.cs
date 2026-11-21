using System.Text.Json.Serialization;
using Domain.Sales;
using FluentValidation;

namespace Application.Checkout;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckoutOrderRequest(CheckoutQuoteRequest Quote, Guid RequestKey, string Name, string Email,
    string? Phone, string BillingAddress, string? DeliveryAddress, IReadOnlyList<decimal> AcceptedUnitPrices,
    decimal AcceptedDeliveryCharge);

public sealed record CheckoutOrderReceipt(Guid OrderId, SalesOrderStatus Status, PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus, decimal Total, string Currency, DateTimeOffset HoldUntil, string? InvoiceNumber,
    string? BankInstructions = null, IReadOnlyList<CheckoutShipment>? Shipments = null);

public sealed record CheckoutShipment(ShipmentStatus Status, string? Carrier, string? TrackingReference);

public interface ICheckoutOrders
{
    Task<CheckoutOrderReceipt> PlaceAsync(CheckoutOrderRequest request, CancellationToken ct);
    Task<CheckoutOrderReceipt> TrackAsync(Guid id, Guid requestKey, CancellationToken ct);
}

public sealed class CheckoutOrderRequestValidator : AbstractValidator<CheckoutOrderRequest>
{
    public CheckoutOrderRequestValidator()
    {
        RuleFor(x => x.Quote).NotNull().SetValidator(new CheckoutQuoteRequestValidator());
        RuleFor(x => x.RequestKey).NotEmpty().Must(x => x.Version == 4).WithMessage("Use a randomly generated checkout key.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.BillingAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.DeliveryAddress).NotEmpty().When(x => x.Quote?.Fulfillment == CheckoutFulfillment.Delivery);
        RuleFor(x => x.AcceptedUnitPrices).NotEmpty().Must((request, prices) => prices?.Count == request.Quote?.Lines?.Count);
        RuleForEach(x => x.AcceptedUnitPrices).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AcceptedDeliveryCharge).GreaterThanOrEqualTo(0);
    }
}
