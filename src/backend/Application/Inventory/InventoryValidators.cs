using Domain.Inventory;
using FluentValidation;

namespace Application.Inventory;

public static class InventoryRules
{
    public const int MaxLines = 200;
    public const int MaxQuantity = 100_000;
    public const decimal MaxMoney = 1_000_000m;

    public static bool HasCents(decimal value) => decimal.Round(value, 2) == value;

    /// <summary>Quantities allow up to 3 decimal places (e.g. 1.5 litres, 0.250 kg).</summary>
    public static bool ValidQuantityScale(decimal value) => decimal.Round(value, 3) == value;
}

public sealed class DocumentListRequestValidator : AbstractValidator<DocumentListRequest>
{
    public static readonly string[] Sorts = ["updatedAt", "-updatedAt", "postedAt", "-postedAt", "number", "-number"];

    public DocumentListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Sort).Must(x => x is null || Sorts.Contains(x)).WithMessage("Sort must be one of: " + string.Join(", ", Sorts) + ".");
    }
}

public sealed class GrnInputValidator : AbstractValidator<GrnInput>
{
    public GrnInputValidator()
    {
        RuleFor(x => x.SupplierName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SupplierReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count <= InventoryRules.MaxLines).WithMessage($"At most {InventoryRules.MaxLines} lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
            line.RuleFor(x => x.UnitCost).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.");
            line.RuleFor(x => x.SellingPrice!.Value).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney)
                .Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.").When(x => x.SellingPrice is not null).OverridePropertyName("SellingPrice");
        });
        // Supplier dates are information only, but a date far in the future is a typo.
        RuleFor(x => x.SupplierDocumentDate).Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)))
            .WithMessage("The supplier's date cannot be in the future.");
    }
}

public sealed class ReverseInputValidator : AbstractValidator<ReverseInput>
{
    public ReverseInputValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class StockDocumentInputValidator : AbstractValidator<StockDocumentInput>
{
    public StockDocumentInputValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count <= InventoryRules.MaxLines).WithMessage($"At most {InventoryRules.MaxLines} lines.")
            .Must(x => x.Select(l => l.ItemId).Distinct().Count() == x.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotEmpty();
            line.RuleFor(x => x.QtyChange).NotEqual(0).InclusiveBetween(-InventoryRules.MaxQuantity, InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
            line.RuleFor(x => x.UnitCost!.Value).InclusiveBetween(0, InventoryRules.MaxMoney).Must(InventoryRules.HasCents)
                .WithMessage("Use dollars and cents.").When(x => x.UnitCost is not null).OverridePropertyName("UnitCost");
            line.RuleFor(x => x.SellingPrice!.Value).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(InventoryRules.HasCents)
                .WithMessage("Use dollars and cents.").When(x => x.SellingPrice is not null).OverridePropertyName("SellingPrice");
        });
    }
}

public sealed class StocktakeInputValidator : AbstractValidator<StocktakeInput>
{
    public StocktakeInputValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count <= 1000).WithMessage("At most 1000 items per stocktake.")
            .Must(x => x.Select(l => l.ItemId).Distinct().Count() == x.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotEmpty();
            line.RuleFor(x => x.CountedQty).InclusiveBetween(0, InventoryRules.MaxQuantity)
                .Must(InventoryRules.ValidQuantityScale).WithMessage("Use at most 3 decimal places.");
        });
    }
}

public sealed class StockRequestValidator : AbstractValidator<StockRequest>
{
    public static readonly string[] Sorts = ["name", "-name", "sku", "-sku", "onHand", "-onHand", "value", "-value"];

    public StockRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Sort).Must(x => x is null || Sorts.Contains(x)).WithMessage("Sort must be one of: " + string.Join(", ", Sorts) + ".");
        RuleFor(x => x).Must(x => x.Date is null || x.AsOf is null).WithMessage("Use either date or asOf, not both.").OverridePropertyName("Date");
    }
}

public sealed class PriceChangeInputValidator : AbstractValidator<PriceChangeInput>
{
    public PriceChangeInputValidator()
    {
        RuleFor(x => x.SellingPrice).GreaterThan(0).LessThanOrEqualTo(InventoryRules.MaxMoney).Must(InventoryRules.HasCents).WithMessage("Use dollars and cents.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Kind-specific rules that need the document kind, applied by the service.</summary>
public static class StockDocumentRules
{
    public static IEnumerable<(string Property, string Message)> Check(StockDocumentKind kind, StockDocumentInput input)
    {
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (kind == StockDocumentKind.Opening)
            {
                if (line.QtyChange <= 0) yield return ($"Lines[{i}].QtyChange", "Opening stock must be more than zero.");
                if (line.UnitCost is null) yield return ($"Lines[{i}].UnitCost", "Enter the cost of the opening stock.");
            }
            else if (line.SellingPrice is not null)
                yield return ($"Lines[{i}].SellingPrice", "Change prices with Change price, not an adjustment.");
        }
        if (kind == StockDocumentKind.Opening && input.Reason != AdjustmentReason.Opening)
            yield return (nameof(input.Reason), "Opening stock uses the Opening reason.");
        if (kind == StockDocumentKind.Adjustment && input.Reason is AdjustmentReason.Opening or AdjustmentReason.Count)
            yield return (nameof(input.Reason), "Use opening stock or a stocktake for this reason.");
    }
}
