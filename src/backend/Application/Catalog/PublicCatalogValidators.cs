using FluentValidation;

namespace Application.Catalog;

public sealed class PublicItemQueryValidator : AbstractValidator<PublicItemQuery>
{
    public PublicItemQueryValidator()
    {
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Category).MaximumLength(160);
        RuleFor(x => x.Brand).MaximumLength(120);
        RuleFor(x => x.Page).InclusiveBetween(1, 100000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 48);
        RuleFor(x => x.Year).InclusiveBetween(1886, 2100);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue);
        RuleFor(x => x.Availability).Must(x => x is null or "" or "InStock" or "LowStock" or "OutOfStock");
        RuleFor(x => x.Sort).Must(x => x is null or "" or "price-asc" or "price-desc" or "name");
    }
}

public sealed class PublicCartRequestValidator : AbstractValidator<PublicCartRequest>
{
    public PublicCartRequestValidator()
    {
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop).NotEmpty().Must(x => x.Count <= 25)
            .Must(x => x.All(l => l is not null) && x.Select(l => l.ItemId).Distinct().Count() == x.Count);
        RuleForEach(x => x.Lines).NotNull().ChildRules(line =>
        {
            line.RuleFor(x => x.ItemId).NotEmpty();
            line.RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
        });
    }
}
