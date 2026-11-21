using Application.Catalog;
using Application.Common;
using Domain.Catalog;
using Infrastructure.Persistence;
using Infrastructure.Inventory;
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

    public async Task<PagedResult<PublicItemDto>> ItemsAsync(PublicItemQuery request, CancellationToken ct)
    {
        var items = ItemService.ApplyFilter(Visible, request.Q, new ItemFilter(
            PartBrand: request.Brand, MakeId: request.MakeId, ModelId: request.ModelId, Year: request.Year));
        if (!string.IsNullOrWhiteSpace(request.Category)) items = items.Where(x => x.Category!.Slug == request.Category);
        var query = Positions(items);
        if (request.MinPrice is { } min) query = query.Where(x => x.Price >= min);
        if (request.MaxPrice is { } max) query = query.Where(x => x.Price <= max);
        if (!string.IsNullOrEmpty(request.Availability)) query = query.Where(x => x.Availability == request.Availability);
        var total = await query.CountAsync(ct);
        var sorted = request.Sort switch
        {
            "name" => query.OrderBy(x => x.Item.Name),
            "price-asc" => query.OrderBy(x => x.Price == null).ThenBy(x => x.Price),
            "price-desc" => query.OrderBy(x => x.Price == null).ThenByDescending(x => x.Price),
            _ => query.OrderBy(x => x.Item.Category!.SortOrder),
        };
        var rows = await Cards(sorted.ThenBy(x => x.Item.Sku).ThenBy(x => x.Item.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(ct);
        return new(rows.Select(ToDto).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<PublicItemDetail> DetailAsync(Guid id, CancellationToken ct)
    {
        var row = await Cards(Positions(Visible.Where(x => x.Id == id))).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Part not found.");
        var item = await Visible.Where(x => x.Id == id).Include(x => x.Images)
            .Include(x => x.Fitments).ThenInclude(x => x.VehicleModel).ThenInclude(x => x!.Make)
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Part not found.");
        var images = item.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new ItemImageDto(x.Id, storage.GetPublicUrl(x.StorageKey), storage.GetPublicUrl(x.ThumbnailKey),
                x.Width, x.Height, x.IsPrimary, x.SortOrder, x.AltText)).ToList();
        var fitments = item.Fitments.OrderBy(x => x.VehicleModel!.Make!.Name).ThenBy(x => x.VehicleModel!.Name).ThenBy(x => x.YearFrom)
            .Select(x => new FitmentDto(x.Id, x.VehicleModelId, x.VehicleModel!.Make!.Name, x.VehicleModel.Name, x.YearFrom, x.YearTo)).ToList();
        var related = await Cards(Positions(Visible.Where(x => x.CategoryId == item.CategoryId && x.Id != id))
            .OrderBy(x => x.Item.Sku).ThenBy(x => x.Item.Id).Take(4)).ToListAsync(ct);
        return new(ToDto(row), images, fitments, related.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<PublicVehicleMake>> MakesAsync(CancellationToken ct) =>
        await db.VehicleMakes.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new PublicVehicleMake(x.Id, x.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<VehicleModelDto>> ModelsAsync(Guid makeId, CancellationToken ct) =>
        await db.VehicleModels.Where(x => x.MakeId == makeId).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new VehicleModelDto(x.Id, x.MakeId, x.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> SitemapIdsAsync(CancellationToken ct) =>
        await Visible.OrderBy(x => x.Id).Select(x => x.Id).Take(49995).ToListAsync(ct);

    public async Task<IReadOnlyList<PublicCartResult>> ValidateCartAsync(PublicCartRequest request, CancellationToken ct)
    {
        var ids = request.Lines.Select(x => x.ItemId).ToArray();
        var rows = await Cards(Positions(Visible.Where(x => ids.Contains(x.Id)))).ToDictionaryAsync(x => x.Id, ct);
        return request.Lines.Select(line =>
        {
            var row = rows.GetValueOrDefault(line.ItemId);
            return new PublicCartResult(line.ItemId, row is null ? null : ToDto(row),
                row?.Price is not null && row.Available >= line.Quantity);
        }).ToList();
    }

    private IQueryable<Position> Positions(IQueryable<Item> items)
    {
        var prices = StockLedger.CurrentPrices(db);
        var reserved = StockLedger.Reservations(db);
        return items.Select(item => new
        {
            Item = item,
            Price = prices.Where(x => x.ItemId == item.Id).Select(x => (decimal?)x.SellingPrice).FirstOrDefault(),
            Available = (db.StockMovements.Where(x => x.ItemId == item.Id).Sum(x => (decimal?)x.QtyChange) ?? 0)
                - (reserved.Where(x => x.ItemId == item.Id).Select(x => (decimal?)x.Quantity).FirstOrDefault() ?? 0),
        }).Select(x => new Position
        {
            Item = x.Item, Price = x.Price, Available = x.Available,
            Availability = x.Available <= 0 ? "OutOfStock" :
                x.Item.ReorderLevel != null && x.Available <= x.Item.ReorderLevel ? "LowStock" : "InStock",
        });
    }

    private static IQueryable<Card> Cards(IQueryable<Position> rows) => rows.Select(x => new Card(
        x.Item.Id, x.Item.Sku, x.Item.Name, x.Item.Description, x.Item.Category!.Name, x.Item.Category.Slug,
        x.Item.PartBrand, x.Item.Images.Where(i => i.IsPrimary).Select(i => i.StorageKey).FirstOrDefault(),
        x.Item.Images.Where(i => i.IsPrimary).Select(i => i.ThumbnailKey).FirstOrDefault(),
        x.Price, x.Availability, x.Available));

    private PublicItemDto ToDto(Card x) => new(x.Id, x.Sku, x.Name, x.Description, x.CategoryName, x.CategorySlug,
        x.Brand, x.Image is null ? null : storage.GetPublicUrl(x.Image),
        x.Thumbnail is null ? null : storage.GetPublicUrl(x.Thumbnail), x.Price, x.Availability);

    private sealed class Position
    {
        public Item Item { get; init; } = null!;
        public decimal? Price { get; init; }
        public decimal Available { get; init; }
        public string Availability { get; init; } = "";
    }

    private sealed record Card(Guid Id, string Sku, string Name, string? Description, string CategoryName,
        string CategorySlug, string? Brand, string? Image, string? Thumbnail, decimal? Price, string Availability, decimal Available);
}
