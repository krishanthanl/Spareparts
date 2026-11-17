using Application.Catalog;
using Application.Common;
using Domain.Catalog;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Catalog;

public sealed class ItemService(AppDbContext db, IFileStorage storage, IImageProcessor images) : IItemService
{
    private const string SkuTaken = "Another item already uses this SKU.";
    public const int MaxImagesPerItem = 12;

    public async Task<PagedResult<ItemSummaryDto>> ListAsync(PageRequest request, ItemFilter filter, CancellationToken ct)
    {
        var query = request.IncludeDeleted ? db.Items.IgnoreQueryFilters() : db.Items;
        query = ApplyFilter(query, request.Q, filter);
        query = request.Sort switch
        {
            "-name" => query.OrderByDescending(x => x.Name),
            "sku" => query.OrderBy(x => x.Sku),
            "-sku" => query.OrderByDescending(x => x.Sku),
            "updatedAt" => query.OrderBy(x => x.UpdatedAt),
            "-updatedAt" => query.OrderByDescending(x => x.UpdatedAt),
            _ => query.OrderBy(x => x.Name).ThenBy(x => x.Sku),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.Sku, x.Name, x.CategoryId, CategoryName = x.Category!.Name, x.PartBrand, x.IsActive, x.IsDeleted, x.UpdatedAt,
                Thumb = x.Images.Where(i => i.IsPrimary).Select(i => i.ThumbnailKey).FirstOrDefault(),
                OnHand = db.StockMovements.Where(m => m.ItemId == x.Id).Sum(m => (decimal?)m.QtyChange) ?? 0,
                Price = db.ItemPriceHistory.Where(p => p.ItemId == x.Id).OrderByDescending(p => p.Sequence).Select(p => (decimal?)p.SellingPrice).FirstOrDefault(),
            }).ToListAsync(ct);
        return new(rows.Select(x => new ItemSummaryDto(x.Id, x.Sku, x.Name, x.CategoryId, x.CategoryName, x.PartBrand,
            x.Thumb is null ? null : storage.GetPublicUrl(x.Thumb), x.IsActive, x.IsDeleted, x.UpdatedAt, x.OnHand, x.Price)).ToList(),
            request.Page, request.PageSize, total);
    }

    internal static IQueryable<Item> ApplyFilter(IQueryable<Item> query, string? q, ItemFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = LikePattern(q);
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") || EF.Functions.ILike(x.Sku, pattern, "\\")
                || (x.PartBrand != null && EF.Functions.ILike(x.PartBrand, pattern, "\\")));
        }
        if (filter.CategoryId is { } categoryId) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(filter.PartBrand))
        {
            var brand = filter.PartBrand.Trim().ToLower();
            query = query.Where(x => x.PartBrand != null && x.PartBrand.ToLower() == brand);
        }
        if (filter.IsActive is { } active) query = query.Where(x => x.IsActive == active);
        if (filter.MakeId is not null || filter.ModelId is not null || filter.Year is not null)
        {
            var (makeId, modelId, year) = (filter.MakeId, filter.ModelId, filter.Year);
            query = query.Where(x => x.Fitments.Any(f =>
                (modelId == null || f.VehicleModelId == modelId)
                && (makeId == null || f.VehicleModel!.MakeId == makeId)
                && (year == null || ((f.YearFrom == null || f.YearFrom <= year) && (f.YearTo == null || f.YearTo >= year)))));
        }
        return query;
    }

    public async Task<ItemDto> GetAsync(Guid id, CancellationToken ct) => await DtoAsync(await LoadAsync(id, includeDeleted: true, ct), ct);

    public async Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct)
    {
        var item = new Item();
        await ApplyAsync(item, input, ct);
        db.Items.Add(item);
        await SaveAsync(db, SkuTaken, ct);
        return await GetAsync(item.Id, ct);
    }

    public async Task<ItemDto> UpdateAsync(Guid id, ItemInput input, CancellationToken ct)
    {
        var item = await LoadAsync(id, includeDeleted: false, ct);
        ExpectVersion(db, item, input.Version);
        await ApplyAsync(item, input, ct);
        await SaveAsync(db, SkuTaken, ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Items.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Item not found.");
        db.Items.Remove(item);
        await SaveAsync(db, SkuTaken, ct);
    }

    public async Task<ItemDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Items.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Item not found.");
        if (!item.IsDeleted) return await GetAsync(id, ct);
        if (!await db.Categories.AnyAsync(x => x.Id == item.CategoryId, ct))
            throw new ConflictException("Restore this item's category first, or move the item to another category after restoring.");
        if (await db.Items.AnyAsync(x => x.Sku == item.Sku, ct))
            throw new ConflictException("Another item now uses this SKU. Change that item's SKU first.");
        item.IsDeleted = false;
        item.DeletedAt = null;
        item.DeletedBy = null;
        await SaveAsync(db, "Another item now uses this SKU. Change that item's SKU first.", ct);
        return await GetAsync(id, ct);
    }

    public async Task<ItemDto> AddImageAsync(Guid id, Stream content, string? altText, CancellationToken ct)
    {
        var item = await LoadAsync(id, includeDeleted: false, ct);
        if (item.Images.Count >= MaxImagesPerItem) throw Invalid("file", $"An item can have at most {MaxImagesPerItem} images.");
        if (altText?.Length > 200) throw Invalid("altText", "Alt text must be 200 characters or fewer.");
        var stored = await StoredImage.WriteAsync(storage, images, $"items/{item.Id:N}", content, ct);
        var image = new ItemImage
        {
            ItemId = item.Id, StorageKey = stored.Key, ThumbnailKey = stored.ThumbnailKey, ContentType = ProcessedImage.ContentType,
            ByteSize = stored.Image.Full.Length, Width = stored.Image.Width, Height = stored.Image.Height,
            SortOrder = item.Images.Count == 0 ? 0 : item.Images.Max(x => x.SortOrder) + 1,
            IsPrimary = item.Images.Count == 0, AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
        };
        // Ids are set in the entity, so a row reached only through a navigation looks existing to EF. Add it explicitly.
        db.ItemImages.Add(image);
        item.Images.Add(image);
        try
        {
            await SaveAsync(db, SkuTaken, ct);
        }
        catch
        {
            await stored.DiscardAsync();
            throw;
        }
        return await DtoAsync(item, ct);
    }

    public async Task<ItemDto> SetPrimaryImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var item = await LoadAsync(id, includeDeleted: false, ct);
        var image = item.Images.SingleOrDefault(x => x.Id == imageId) ?? throw new NotFoundException("Image not found.");
        foreach (var other in item.Images.Where(x => x.IsPrimary && x != image)) other.IsPrimary = false;
        // Clear the old primary first so the one-primary unique index never sees two.
        await SaveAsync(db, SkuTaken, ct);
        image.IsPrimary = true;
        await SaveAsync(db, SkuTaken, ct);
        return await DtoAsync(item, ct);
    }

    public async Task<ItemDto> RemoveImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var item = await LoadAsync(id, includeDeleted: false, ct);
        var image = item.Images.SingleOrDefault(x => x.Id == imageId) ?? throw new NotFoundException("Image not found.");
        item.Images.Remove(image);
        db.ItemImages.Remove(image);
        await SaveAsync(db, SkuTaken, ct);
        if (image.IsPrimary && item.Images.OrderBy(x => x.SortOrder).FirstOrDefault() is { } next)
        {
            next.IsPrimary = true;
            await SaveAsync(db, SkuTaken, ct);
        }
        await storage.DeleteAsync(image.StorageKey, CancellationToken.None);
        await storage.DeleteAsync(image.ThumbnailKey, CancellationToken.None);
        return await DtoAsync(item, ct);
    }

    private async Task ApplyAsync(Item item, ItemInput input, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId, ct))
            throw Invalid(nameof(ItemInput.CategoryId), "Choose an existing category.");
        var fitments = input.Fitments ?? [];
        var modelIds = fitments.Select(x => x.VehicleModelId).Distinct().ToList();
        if (modelIds.Count > 0 && await db.VehicleModels.CountAsync(x => modelIds.Contains(x.Id), ct) != modelIds.Count)
            throw Invalid(nameof(ItemInput.Fitments), "One or more vehicle models do not exist.");

        item.Sku = Item.NormalizeSku(input.Sku);
        item.Name = input.Name.Trim();
        item.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        item.CategoryId = input.CategoryId;
        item.PartBrand = string.IsNullOrWhiteSpace(input.PartBrand) ? null : input.PartBrand.Trim();
        item.ReorderLevel = input.ReorderLevel;
        item.ReorderQuantity = input.ReorderQuantity;
        item.IsActive = input.IsActive;

        var wanted = fitments.Select(x => (x.VehicleModelId, x.YearFrom, x.YearTo)).ToHashSet();
        foreach (var existing in item.Fitments.Where(x => !wanted.Contains((x.VehicleModelId, x.YearFrom, x.YearTo))).ToList())
        {
            item.Fitments.Remove(existing);
            db.ItemFitments.Remove(existing);
        }
        var have = item.Fitments.Select(x => (x.VehicleModelId, x.YearFrom, x.YearTo)).ToHashSet();
        foreach (var add in wanted.Where(x => !have.Contains(x)))
        {
            // Added explicitly: see AddImageAsync.
            var fitment = new ItemFitment { ItemId = item.Id, VehicleModelId = add.VehicleModelId, YearFrom = add.YearFrom, YearTo = add.YearTo };
            db.ItemFitments.Add(fitment);
            item.Fitments.Add(fitment);
        }
    }

    private async Task<Item> LoadAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var query = includeDeleted ? db.Items.IgnoreQueryFilters() : db.Items;
        return await query.Include(x => x.Images).Include(x => x.Fitments).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Item not found.");
    }

    private async Task<ItemDto> DtoAsync(Item item, CancellationToken ct)
    {
        var category = await db.Categories.IgnoreQueryFilters().Where(x => x.Id == item.CategoryId).Select(x => x.Name).SingleAsync(ct);
        var modelIds = item.Fitments.Select(x => x.VehicleModelId).ToList();
        var models = await db.VehicleModels.Where(x => modelIds.Contains(x.Id))
            .Select(x => new { x.Id, Model = x.Name, Make = x.Make!.Name }).ToDictionaryAsync(x => x.Id, ct);
        return new ItemDto(item.Id, item.Sku, item.Name, item.Description, item.CategoryId, category, item.PartBrand,
            item.ReorderLevel, item.ReorderQuantity, item.IsActive, item.IsDeleted, item.Version, item.UpdatedAt,
            item.Fitments.Select(f => new FitmentDto(f.Id, f.VehicleModelId, models[f.VehicleModelId].Make, models[f.VehicleModelId].Model, f.YearFrom, f.YearTo))
                .OrderBy(f => f.Make).ThenBy(f => f.Model).ThenBy(f => f.YearFrom).ToList(),
            item.Images.OrderBy(x => x.SortOrder).Select(x => new ItemImageDto(x.Id, storage.GetPublicUrl(x.StorageKey),
                storage.GetPublicUrl(x.ThumbnailKey), x.Width, x.Height, x.IsPrimary, x.SortOrder, x.AltText)).ToList());
    }
}
