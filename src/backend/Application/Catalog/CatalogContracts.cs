namespace Application.Catalog;

public sealed record PageRequest(int Page = 1, int PageSize = 20, string? Sort = null, string? Q = null, bool IncludeDeleted = false)
{
    public const int MaxPageSize = 100;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, string? ImageUrl, string? ThumbnailUrl,
    int SortOrder, bool IsActive, bool IsDeleted, int ItemCount, uint Version, DateTimeOffset UpdatedAt);

public sealed record CategoryInput(string Name, string? Description, int SortOrder, bool IsActive, uint? Version);

public sealed record ItemImageDto(Guid Id, string Url, string ThumbnailUrl, int Width, int Height, bool IsPrimary, int SortOrder, string? AltText);

public sealed record FitmentDto(Guid Id, Guid VehicleModelId, string Make, string Model, short? YearFrom, short? YearTo);

public sealed record FitmentInput(Guid VehicleModelId, short? YearFrom, short? YearTo);

public sealed record ItemSummaryDto(Guid Id, string Sku, string Name, Guid CategoryId, string CategoryName, string? PartBrand,
    string? ThumbnailUrl, bool IsActive, bool IsDeleted, DateTimeOffset UpdatedAt);

public sealed record ItemDto(Guid Id, string Sku, string Name, string? Description, Guid CategoryId, string CategoryName, string? PartBrand,
    int? ReorderLevel, int? ReorderQuantity, bool IsActive, bool IsDeleted, uint Version, DateTimeOffset UpdatedAt,
    IReadOnlyList<FitmentDto> Fitments, IReadOnlyList<ItemImageDto> Images);

public sealed record ItemInput(string Sku, string Name, string? Description, Guid CategoryId, string? PartBrand,
    int? ReorderLevel, int? ReorderQuantity, bool IsActive, IReadOnlyList<FitmentInput>? Fitments, uint? Version);

public sealed record ItemFilter(Guid? CategoryId = null, string? PartBrand = null, Guid? MakeId = null, Guid? ModelId = null, int? Year = null, bool? IsActive = null);

public sealed record VehicleMakeDto(Guid Id, string Name, IReadOnlyList<VehicleModelDto> Models);
public sealed record VehicleModelDto(Guid Id, Guid MakeId, string Name);
public sealed record NameInput(string Name);

/// <summary>Public storefront shapes. Never include cost, stock internals, audit actors or inactive/deleted data.</summary>
public sealed record PublicCategoryDto(Guid Id, string Name, string Slug, string? Description, string? ThumbnailUrl, int ItemCount);
public sealed record PublicItemDto(Guid Id, string Sku, string Name, string? Description, string CategoryName, string CategorySlug,
    string? PartBrand, string? ImageUrl, string? ThumbnailUrl);

public interface ICategoryService
{
    Task<PagedResult<CategoryDto>> ListAsync(PageRequest request, CancellationToken ct);
    Task<CategoryDto> GetAsync(Guid id, CancellationToken ct);
    Task<CategoryDto> CreateAsync(CategoryInput input, CancellationToken ct);
    Task<CategoryDto> UpdateAsync(Guid id, CategoryInput input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<CategoryDto> RestoreAsync(Guid id, CancellationToken ct);
    Task<CategoryDto> SetImageAsync(Guid id, Stream content, CancellationToken ct);
    Task<CategoryDto> RemoveImageAsync(Guid id, CancellationToken ct);
}

public interface IItemService
{
    Task<PagedResult<ItemSummaryDto>> ListAsync(PageRequest request, ItemFilter filter, CancellationToken ct);
    Task<ItemDto> GetAsync(Guid id, CancellationToken ct);
    Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct);
    Task<ItemDto> UpdateAsync(Guid id, ItemInput input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<ItemDto> RestoreAsync(Guid id, CancellationToken ct);
    Task<ItemDto> AddImageAsync(Guid id, Stream content, string? altText, CancellationToken ct);
    Task<ItemDto> SetPrimaryImageAsync(Guid id, Guid imageId, CancellationToken ct);
    Task<ItemDto> RemoveImageAsync(Guid id, Guid imageId, CancellationToken ct);
}

public interface IVehicleService
{
    Task<IReadOnlyList<VehicleMakeDto>> ListAsync(CancellationToken ct);
    Task<VehicleMakeDto> CreateMakeAsync(NameInput input, CancellationToken ct);
    Task<VehicleModelDto> CreateModelAsync(Guid makeId, NameInput input, CancellationToken ct);
}

public interface IPublicCatalogService
{
    Task<IReadOnlyList<PublicCategoryDto>> CategoriesAsync(CancellationToken ct);
    Task<PagedResult<PublicItemDto>> ItemsAsync(string? categorySlug, string? q, int page, int pageSize, CancellationToken ct);
}
