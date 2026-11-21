using Application.Inventory;
using FluentValidation;

namespace Application.Sales;

public static class SalesRules
{
    public const int MaxLines = 200;
    public static readonly string[] OrderSorts = ["updatedAt", "-updatedAt", "confirmedAt", "-confirmedAt", "number", "-number", "total", "-total"];
    public const int MaxTakingsDays = 92;
}

public sealed class CustomerInputValidator : AbstractValidator<CustomerInput>
{
    public CustomerInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Company).MaximumLength(200);
        RuleFor(x => x.BillingAddress).MaximumLength(500);
        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CustomerListRequestValidator : AbstractValidator<CustomerListRequest>
{
    public CustomerListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

public sealed class OrderInputValidator : AbstractValidator<OrderInput>
{
    public OrderInputValidator()
    {
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.PaymentTerms).IsInEnum();
        RuleFor(x => x.DeliveryMethod).IsInEnum();
        RuleFor(x => x.CustomerName).MaximumLength(200);
        RuleFor(x => x.CustomerPhone).MaximumLength(50);
        RuleFor(x => x.CustomerEmail).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.CustomerCompany).MaximumLength(200);
        RuleFor(x => x.BillingAddress).MaximumLength(500);
        RuleFor(x => x.DeliveryAddress).MaximumLength(500);
        RuleFor(x => x.DeliveryAddress).NotEmpty().When(x => x.DeliveryMethod == Domain.Sales.DeliveryMethod.Delivery)
            .WithMessage("Enter the delivery address.");
        RuleFor(x => x.DeliveryCharge).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.DeliveryCharge).Equal(0).When(x => x.DeliveryMethod == Domain.Sales.DeliveryMethod.Pickup)
            .WithMessage("Pickup orders have no delivery charge.");
        RuleFor(x => x.OrderDiscount).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.DiscountReason).MaximumLength(500);
        RuleFor(x => x.DiscountReason).NotEmpty().When(x => x.OrderDiscount > 0).WithMessage("Give a reason for the discount.");
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.DeliveryPostcode).Must(p => AfterSaleMath.TryParsePostcode(p, out _)).WithMessage("Use a 4-digit NZ postcode.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeliveryPostcode));
        RuleFor(x => x.DeliveryChargeReason).MaximumLength(500);
        RuleFor(x => x.PaymentTerms).Equal(Domain.Sales.PaymentTerms.Immediate)
            .When(x => x.DeliveryMethod == Domain.Sales.DeliveryMethod.Pickup && x.Source == Domain.Sales.SalesSource.Counter)
            .WithMessage("Counter sales are paid at the counter.");
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count <= SalesRules.MaxLines).WithMessage($"At most {SalesRules.MaxLines} lines.");
        RuleFor(x => x.Lines).Must(x => x.Select(l => l.ItemId).Distinct().Count() == x.Count)
            .WithMessage("Each item can be on the order only once. Change the quantity instead.").When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
            line.RuleFor(x => x.UnitPrice!.Value).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents)
                .WithMessage("Use dollars and cents.").When(x => x.UnitPrice is not null).OverridePropertyName("UnitPrice");
            line.RuleFor(x => x.DiscountAmount!.Value).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents)
                .WithMessage("Use dollars and cents.").When(x => x.DiscountAmount is not null).OverridePropertyName("DiscountAmount");
            line.RuleFor(x => x.DiscountPercent!.Value).InclusiveBetween(0, 100).Must(v => decimal.Round(v, 2) == v)
                .WithMessage("Use at most 2 decimal places.").When(x => x.DiscountPercent is not null).OverridePropertyName("DiscountPercent");
            line.RuleFor(x => x).Must(x => x.DiscountAmount is null || x.DiscountPercent is null)
                .WithMessage("Give the discount as an amount or a percentage, not both.").OverridePropertyName("DiscountPercent");
            line.RuleFor(x => x.DiscountReason).MaximumLength(500);
        });
    }
}

public sealed class OrderListRequestValidator : AbstractValidator<OrderListRequest>
{
    public OrderListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.PaymentStatus).IsInEnum();
        RuleFor(x => x.FulfillmentStatus).IsInEnum();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Sort).Must(x => x is null || SalesRules.OrderSorts.Contains(x))
            .WithMessage("Sort must be one of: " + string.Join(", ", SalesRules.OrderSorts) + ".");
    }
}

public sealed class CancelOrderInputValidator : AbstractValidator<CancelOrderInput>
{
    public CancelOrderInputValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class IssueInputValidator : AbstractValidator<IssueInput>
{
    public IssueInputValidator()
    {
        RuleFor(x => x.IdempotencyKey).MaximumLength(100);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.OrderLineId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
        });
        RuleFor(x => x.Lines).Must(l => l is null || l.Select(x => x.OrderLineId).Distinct().Count() == l.Count)
            .WithMessage("Each order line can appear once.");
    }
}

public sealed class PaymentInputValidator : AbstractValidator<PaymentInput>
{
    public PaymentInputValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class VoidPaymentInputValidator : AbstractValidator<VoidPaymentInput>
{
    public VoidPaymentInputValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class QuickSaleInputValidator : AbstractValidator<QuickSaleInput>
{
    public QuickSaleInputValidator()
    {
        RuleFor(x => x.Order).NotNull().SetValidator(new OrderInputValidator());
        RuleFor(x => x.Order.Lines).NotEmpty().WithMessage("Add at least one item.").When(x => x.Order is not null);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Amount!.Value).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(InventoryRules.HasCents)
            .WithMessage("Use dollars and cents.").When(x => x.Amount is not null).OverridePropertyName("Amount");
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class TakingsRequestValidator : AbstractValidator<TakingsRequest>
{
    public TakingsRequestValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber < SalesRules.MaxTakingsDays)
            .WithMessage($"Choose at most {SalesRules.MaxTakingsDays} days.").OverridePropertyName("To");
    }
}
