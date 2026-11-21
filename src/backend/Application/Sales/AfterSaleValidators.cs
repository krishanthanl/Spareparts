using Application.Inventory;
using FluentValidation;

namespace Application.Sales;

internal static class AfterSaleRules
{
    public static bool Postcode(string? v) => AfterSaleMath.TryParsePostcode(v, out _);
    public static bool Cents(decimal v) => InventoryRules.HasCents(v);
}

public sealed class DeliverySettingsInputValidator : AbstractValidator<DeliverySettingsInput>
{
    public DeliverySettingsInputValidator() =>
        RuleFor(x => x.FreeDeliveryThreshold!.Value).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents)
            .WithMessage("Use dollars and cents.").When(x => x.FreeDeliveryThreshold is not null).OverridePropertyName("FreeDeliveryThreshold");
}

public sealed class DeliveryZoneInputValidator : AbstractValidator<DeliveryZoneInput>
{
    public DeliveryZoneInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Charge).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.RuralSurcharge).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.FreeDeliveryThreshold!.Value).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents)
            .WithMessage("Use dollars and cents.").When(x => x.FreeDeliveryThreshold is not null).OverridePropertyName("FreeDeliveryThreshold");
        RuleFor(x => x.ServicePromise).MaximumLength(200);
        RuleFor(x => x.Postcodes).NotNull().Must(p => p.Count is > 0 and <= 500).WithMessage("Add between 1 and 500 postcodes or ranges.");
        RuleForEach(x => x.Postcodes).ChildRules(p =>
        {
            p.RuleFor(x => x.From).Must(AfterSaleRules.Postcode).WithMessage("Use a 4-digit NZ postcode.");
            p.RuleFor(x => x.To).Must(AfterSaleRules.Postcode).WithMessage("Use a 4-digit NZ postcode.").When(x => !string.IsNullOrWhiteSpace(x.To));
            p.RuleFor(x => x).Must(x => string.IsNullOrWhiteSpace(x.To) || !AfterSaleRules.Postcode(x.From) || !AfterSaleRules.Postcode(x.To)
                    || int.Parse(x.To.Trim()) >= int.Parse(x.From.Trim()))
                .WithMessage("The end of a range must not be before its start.").OverridePropertyName("To");
        });
    }
}

public sealed class DeliveryQuoteInputValidator : AbstractValidator<DeliveryQuoteInput>
{
    public DeliveryQuoteInputValidator()
    {
        RuleFor(x => x.Postcode).Must(AfterSaleRules.Postcode).WithMessage("Use a 4-digit NZ postcode.");
        RuleFor(x => x.GoodsValue).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
    }
}

public sealed class ShipmentInputValidator : AbstractValidator<ShipmentInput>
{
    public ShipmentInputValidator()
    {
        RuleFor(x => x.Carrier).MaximumLength(100);
        RuleFor(x => x.TrackingReference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.OrderLineId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
        });
        RuleFor(x => x.Lines).Must(l => l is null || l.Select(x => x.OrderLineId).Distinct().Count() == l.Count).WithMessage("Each order line can appear once.");
    }
}

public sealed class DispatchInputValidator : AbstractValidator<DispatchInput>
{
    public DispatchInputValidator()
    {
        RuleFor(x => x.Carrier).MaximumLength(100);
        RuleFor(x => x.TrackingReference).MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class SalesReturnInputValidator : AbstractValidator<SalesReturnInput>
{
    public SalesReturnInputValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Choose at least one line to return.")
            .Must(l => l is null || l.Select(x => x.OrderLineId).Distinct().Count() == l.Count).WithMessage("Each order line can appear once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.OrderLineId).NotEmpty();
            line.RuleFor(x => x.Disposition).IsInEnum();
            line.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
        });
    }
}

public sealed class CreditNoteInputValidator : AbstractValidator<CreditNoteInput>
{
    public CreditNoteInputValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotNull()
            .Must(l => l is null || l.Select(x => x.OrderLineId).Distinct().Count() == l.Count).WithMessage("Each order line can appear once.");
        RuleFor(x => x.DeliveryAmount).InclusiveBetween(0, InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
        RuleFor(x => x).Must(x => x.DeliveryAmount > 0 || (x.Lines?.Count ?? 0) > 0).WithMessage("Credit at least one line or the delivery charge.")
            .OverridePropertyName("Lines");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.OrderLineId).NotEmpty();
            line.RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
        });
    }
}

public sealed class RefundInputValidator : AbstractValidator<RefundInput>
{
    public RefundInputValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(AfterSaleRules.Cents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class CompleteRefundInputValidator : AbstractValidator<CompleteRefundInput>
{
    public CompleteRefundInputValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.EvidenceReference).NotEmpty().WithMessage("Enter the bank reference, receipt or note that proves the money was paid back.")
            .MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public sealed class RefundReasonInputValidator : AbstractValidator<RefundReasonInput>
{
    public RefundReasonInputValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class ListRequestsValidator : AbstractValidator<RefundListRequest>
{
    public ListRequestsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

public sealed class ShipmentListRequestValidator : AbstractValidator<ShipmentListRequest>
{
    public ShipmentListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

public sealed class ReturnListRequestValidator : AbstractValidator<ReturnListRequest>
{
    public ReturnListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
    }
}

public sealed class CreditNoteListRequestValidator : AbstractValidator<CreditNoteListRequest>
{
    public CreditNoteListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
    }
}
