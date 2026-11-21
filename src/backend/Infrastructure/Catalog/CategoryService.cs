using Application.Catalog;
using Application.Common;
using Domain.Catalog;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Catalog;

public sealed class CategoryService(AppDbContext db, IFileStorage storage, IImageProcessor images) : ICategoryService
{
    private const string NameTaken = "A category with this name already exists.";

    public async Task<PagedResult<CategoryDto>> ListAsync(PageRequest request, CancellationToken ct)
    {
        var query = request.IncludeDeleted ? db.Categories.IgnoreQueryFilters() : db.Categories;
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        query = request.Sort switch
        {
            "-name" => query.OrderByDescending(x => x.Name),
            "updatedAt" => query.OrderBy(x => x.UpdatedAt),
            "-updatedAt" => query.OrderByDescending(x => x.UpdatedAt),
            "name" => query.OrderBy(x => x.Name),
            _ => query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new { Category = x, Count = x.Items.Count(i => !i.IsDeleted) }).ToListAsync(ct);
        return new(rows.Select(x => ToDto(x.Category, x.Count)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken ct) => await DtoAsync(await FindAsync(id, includeDeleted: true, ct), ct);

    public async Task<CategoryDto> CreateAsync(CategoryInput input, CancellationToken ct)
    {
        var category = new Category();
        Apply(category, input);
        db.Categories.Add(category);
        await SaveAsync(db, NameTaken, ct);
        return ToDto(category, 0);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, CategoryInput input, CancellationToken ct)
    {
        var category = await FindAsync(id, includeDeleted: false, ct);
        ExpectVersion(db, category, input.Version);
        Apply(category, input);
        await SaveAsync(db, NameTaken, ct);
        return await DtoAsync(category, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await FindAsync(id, includeDeleted: false, ct);
        var items = await db.Items.CountAsync(x => x.CategoryId == id, ct);
        if (items > 0) throw new ConflictException($"Move or delete the {items} item(s) in this category first.");
        db.Categories.Remove(category);
        await SaveAsync(db, NameTaken, ct);
    }

    public async Task<CategoryDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var category = await FindAsync(id, includeDeleted: true, ct);
        if (!category.IsDeleted) return await DtoAsync(category, ct);
        if (await db.Categories.AnyAsync(x => x.Slug == category.Slug, ct))
            throw new ConflictException("Another category now uses this name. Rename that category first.");
        category.IsDeleted = false;
        category.DeletedAt = null;
        category.DeletedBy = null;
        await SaveAsync(db, "Another category now uses this name. Rename that category first.", ct);
        return await DtoAsync(category, ct);
    }

    public async Task<CategoryDto> SetImageAsync(Guid id, Stream content, CancellationToken ct)
    {
        var category = await FindAsync(id, includeDeleted: false, ct);
        var stored = await StoredImage.WriteAsync(storage, images, $"categories/{category.Id:N}", content, ct);
        var (oldKey, oldThumb) = (category.ImageKey, category.ThumbnailKey);
        category.ImageKey = stored.Key;
        category.ThumbnailKey = stored.ThumbnailKey;
        try
        {
            await SaveAsync(db, NameTaken, ct);
        }
        catch
        {
            await stored.DiscardAsync();
            throw;
        }
        await DeleteFilesAsync(oldKey, oldThumb);
        return await DtoAsync(category, ct);
    }

    public async Task<CategoryDto> RemoveImageAsync(Guid id, CancellationToken ct)
    {
        var category = await FindAsync(id, includeDeleted: false, ct);
        var (oldKey, oldThumb) = (category.ImageKey, category.ThumbnailKey);
        category.ImageKey = null;
        category.ThumbnailKey = null;
        await SaveAsync(db, NameTaken, ct);
        await DeleteFilesAsync(oldKey, oldThumb);
        return await DtoAsync(category, ct);
    }

    private async Task DeleteFilesAsync(params string?[] keys)
    {
        foreach (var key in keys.OfType<string>()) await storage.DeleteAsync(key, CancellationToken.None);
    }

    private static void Apply(Category category, CategoryInput input)
    {
        category.Name = input.Name.Trim();
        category.Slug = Slug.From(input.Name);
        category.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        category.SortOrder = input.SortOrder;
        category.IsActive = input.IsActive;
    }

    private async Task<Category> FindAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var query = includeDeleted ? db.Categories.IgnoreQueryFilters() : db.Categories;
        return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category not found.");
    }

    private async Task<CategoryDto> DtoAsync(Category category, CancellationToken ct) =>
        ToDto(category, await db.Items.CountAsync(x => x.CategoryId == category.Id, ct));

    private CategoryDto ToDto(Category x, int itemCount) => new(x.Id, x.Name, x.Slug, x.Description,
        x.ImageKey is null ? null : storage.GetPublicUrl(x.ImageKey), x.ThumbnailKey is null ? null : storage.GetPublicUrl(x.ThumbnailKey),
        x.SortOrder, x.IsActive, x.IsDeleted, itemCount, x.Version, x.UpdatedAt);
}
