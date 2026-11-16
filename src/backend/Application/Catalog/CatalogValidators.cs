using FluentValidation;

namespace Application.Catalog;

public static class CatalogSorts
{
    public static readonly string[] Allowed = ["name", "-name", "sku", "-sku", "updatedAt", "-updatedAt"];
}

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.Sort).Must(x => x is null || CatalogSorts.Allowed.Contains(x))
            .WithMessage("Sort must be one of: " + string.Join(", ", CatalogSorts.Allowed) + ".");
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

public sealed class CategoryInputValidator : AbstractValidator<CategoryInput>
{
    public CategoryInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .Must(x => Domain.Catalog.Slug.From(x ?? "").Length > 0).WithMessage("Name must contain letters or numbers.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10000);
    }
}

public sealed class FitmentInputValidator : AbstractValidator<FitmentInput>
{
    public const short MinYear = 1950;
    public static short MaxYear(TimeProvider clock) => (short)(clock.GetUtcNow().Year + 1);

    public FitmentInputValidator(TimeProvider clock)
    {
        var max = MaxYear(clock);
        RuleFor(x => x.VehicleModelId).NotEmpty();
        RuleFor(x => x.YearFrom).InclusiveBetween(MinYear, max).When(x => x.YearFrom is not null);
        RuleFor(x => x.YearTo).InclusiveBetween(MinYear, max).When(x => x.YearTo is not null);
        RuleFor(x => x.YearTo).Must((x, to) => x.YearFrom is null || to is null || x.YearFrom <= to)
            .WithMessage("Year from must be the same as or before year to.");
    }
}

public sealed class ItemInputValidator : AbstractValidator<ItemInput>
{
    public ItemInputValidator(TimeProvider clock)
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50)
            .Matches("^\\s*[A-Za-z0-9][A-Za-z0-9._/-]*\\s*$").WithMessage("SKU may contain letters, numbers, dot, underscore, slash and dash only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.PartBrand).MaximumLength(100);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0).When(x => x.ReorderLevel is not null);
        RuleFor(x => x.ReorderQuantity).GreaterThan(0).When(x => x.ReorderQuantity is not null);
        RuleFor(x => x.Fitments).Must(x => x is null || x.Count <= 50).WithMessage("An item can have at most 50 fitments.");
        RuleFor(x => x.Fitments).Must(x => x is null || x.Distinct().Count() == x.Count).WithMessage("Fitments must not repeat.");
        RuleForEach(x => x.Fitments).SetValidator(new FitmentInputValidator(clock));
    }
}

public sealed class NameInputValidator : AbstractValidator<NameInput>
{
    public NameInputValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
}
