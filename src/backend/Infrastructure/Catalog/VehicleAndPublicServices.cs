using Application.Catalog;
using Application.Common;
using Domain.Catalog;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Catalog;

public sealed class VehicleService(AppDbContext db) : IVehicleService
{
    public async Task<IReadOnlyList<VehicleMakeDto>> ListAsync(CancellationToken ct) =>
        await db.VehicleMakes.OrderBy(x => x.Name)
            .Select(x => new VehicleMakeDto(x.Id, x.Name, x.Models.OrderBy(m => m.Name).Select(m => new VehicleModelDto(m.Id, m.MakeId, m.Name)).ToList()))
            .ToListAsync(ct);

    public async Task<VehicleMakeDto> CreateMakeAsync(NameInput input, CancellationToken ct)
    {
        var make = new VehicleMake { Name = input.Name.Trim() };
        db.VehicleMakes.Add(make);
        await SaveAsync(db, "This vehicle make already exists.", ct);
        return new(make.Id, make.Name, []);
    }

    public async Task<VehicleModelDto> CreateModelAsync(Guid makeId, NameInput input, CancellationToken ct)
    {
        if (!await db.VehicleMakes.AnyAsync(x => x.Id == makeId, ct)) throw new NotFoundException("Vehicle make not found.");
        var model = new VehicleModel { MakeId = makeId, Name = input.Name.Trim() };
        db.VehicleModels.Add(model);
        await SaveAsync(db, "This model already exists for the make.", ct);
        return new(model.Id, model.MakeId, model.Name);
    }
}

/// <summary>Read-only storefront queries: active, non-deleted items in active, non-deleted categories only.</summary>
public sealed class PublicCatalogService(AppDbContext db, IFileStorage storage) : IPublicCatalogService
{
    public const int MaxPageSize = 48;

    private IQueryable<Item> Visible => db.Items.AsNoTracking().Where(x => x.IsActive && x.Category!.IsActive && !x.Category.IsDeleted);

    public async Task<IReadOnlyList<PublicCategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        var rows = await db.Categories.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id, x.Name, x.Slug, x.Description, x.ThumbnailKey,
                Count = x.Items.Count(i => i.IsActive),
                Fallback = x.Items.Where(i => i.IsActive).OrderBy(i => i.Sku)
                    .SelectMany(i => i.Images.Where(m => m.IsPrimary).Select(m => m.ThumbnailKey)).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Where(x => x.Count > 0).Select(x => new PublicCategoryDto(x.Id, x.Name, x.Slug, x.Description,
            (x.ThumbnailKey ?? x.Fallback) is { } key ? storage.GetPublicUrl(key) : null, x.Count)).ToList();
    }

    public async Task<PagedResult<PublicItemDto>> ItemsAsync(string? categorySlug, string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = ItemService.ApplyFilter(Visible, q?.Length > 100 ? q[..100] : q, new ItemFilter());
        if (!string.IsNullOrWhiteSpace(categorySlug)) query = query.Where(x => x.Category!.Slug == categorySlug);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Category!.SortOrder).ThenBy(x => x.Sku).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Sku, x.Name, x.Description, CategoryName = x.Category!.Name, CategorySlug = x.Category.Slug, x.PartBrand,
                Image = x.Images.Where(i => i.IsPrimary).Select(i => new { i.StorageKey, i.ThumbnailKey }).FirstOrDefault(),
            }).ToListAsync(ct);
        return new(rows.Select(x => new PublicItemDto(x.Id, x.Sku, x.Name, x.Description, x.CategoryName, x.CategorySlug, x.PartBrand,
            x.Image is null ? null : storage.GetPublicUrl(x.Image.StorageKey),
            x.Image is null ? null : storage.GetPublicUrl(x.Image.ThumbnailKey))).ToList(), page, pageSize, total);
    }
}
