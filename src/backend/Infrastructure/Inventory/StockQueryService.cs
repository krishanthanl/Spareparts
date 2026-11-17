using Application.Catalog;
using Application.Common;
using Application.Inventory;
using Domain.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Inventory;

/// <summary>
/// Stock is always derived from the ledger: on-hand at an instant is the sum of movements before it.
/// A calendar date means "end of that day in Pacific/Auckland" (exclusive next-midnight boundary).
/// </summary>
public sealed class StockQueryService(AppDbContext db, ShopTime shopTime, TimeProvider clock) : IStockQueryService
{
    private sealed record Row(Guid ItemId, string Sku, string Name, string CategoryName, int? ReorderLevel, int? ReorderQuantity,
        decimal OnHand, decimal AverageCost, decimal? SellingPrice);

    public DateTimeOffset? ResolveAsOf(StockRequest request) =>
        request.Date is { } date ? shopTime.GetUtcDayRange(date).EndExclusive : request.AsOf?.ToUniversalTime();

    public async Task<StockReport> StockAsync(StockRequest request, CancellationToken ct)
    {
        var asOf = ResolveAsOf(request);
        // History can be read, but the future cannot: there is no forecast stock.
        if (request.Date > DateOnly.FromDateTime(shopTime.ToLocal(clock.GetUtcNow()).DateTime) || request.AsOf > clock.GetUtcNow().AddMinutes(1))
            throw Invalid(request.Date is null ? "AsOf" : "Date", "Choose today or an earlier date.");
        var rows = await RowsAsync(asOf, request.Q, request.CategoryId, ct);
        if (request.OnlyInStock) rows = rows.Where(r => r.OnHand != 0).ToList();
        IEnumerable<Row> sorted = request.Sort switch
        {
            "-name" => rows.OrderByDescending(r => r.Name),
            "sku" => rows.OrderBy(r => r.Sku),
            "-sku" => rows.OrderByDescending(r => r.Sku),
            "onHand" => rows.OrderBy(r => r.OnHand).ThenBy(r => r.Name),
            "-onHand" => rows.OrderByDescending(r => r.OnHand).ThenBy(r => r.Name),
            "value" => rows.OrderBy(r => Value(r)).ThenBy(r => r.Name),
            "-value" => rows.OrderByDescending(r => Value(r)).ThenBy(r => r.Name),
            _ => rows.OrderBy(r => r.Name).ThenBy(r => r.Sku),
        };
        var page = sorted.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).Select(ToDto).ToList();
        return new StockReport(asOf, rows.Sum(r => r.OnHand), rows.Sum(Value), new PagedResult<StockRow>(page, request.Page, request.PageSize, rows.Count));
    }

    public async Task<IReadOnlyList<StockRow>> LowStockAsync(CancellationToken ct) =>
        (await RowsAsync(null, null, null, ct)).Where(r => r.ReorderLevel is { } level && r.OnHand <= level)
            .OrderBy(r => r.OnHand - r.ReorderLevel!.Value).ThenBy(r => r.Name).Select(ToDto).ToList();

    public async Task<StockCard> StockCardAsync(Guid itemId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId, ct) ?? throw new NotFoundException("Item not found.");
        var start = from is { } f ? shopTime.GetUtcDayRange(f).Start : (DateTimeOffset?)null;
        var end = to is { } t ? shopTime.GetUtcDayRange(t).EndExclusive : (DateTimeOffset?)null;
        var all = db.StockMovements.AsNoTracking().Where(m => m.ItemId == itemId);
        var opening = start is null ? 0 : await all.Where(m => m.OccurredAt < start).SumAsync(m => (decimal?)m.QtyChange, ct) ?? 0;
        var movements = await all.Where(m => (start == null || m.OccurredAt >= start) && (end == null || m.OccurredAt < end))
            .OrderBy(m => m.Sequence).Take(2000).ToListAsync(ct);
        var grnIds = movements.Where(m => m.RefType == "Grn").Select(m => m.RefId).Distinct().ToList();
        var docIds = movements.Where(m => m.RefType == "StockDocument").Select(m => m.RefId).Distinct().ToList();
        var numbers = (await db.Grns.Where(x => grnIds.Contains(x.Id)).Select(x => new { x.Id, Number = x.GrnNumber }).ToListAsync(ct))
            .Concat(await db.StockDocuments.Where(x => docIds.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Number);
        var balance = opening;
        var rows = movements.Select(m =>
        {
            balance += m.QtyChange;
            return new StockCardRow(m.Sequence, m.OccurredAt, m.MovementType, m.QtyChange, balance, m.UnitCost, m.AverageCostAfter, m.RefType, m.RefId,
                numbers.GetValueOrDefault(m.RefId), m.CreatedBy);
        }).ToList();
        var current = (await RowsAsync(null, null, null, ct, itemId)).SingleOrDefault();
        return new StockCard(item.Id, item.Sku, item.Name, opening, current?.OnHand ?? 0, current?.AverageCost ?? 0, current?.SellingPrice, rows);
    }

    private async Task<List<Row>> RowsAsync(DateTimeOffset? asOf, string? q, Guid? categoryId, CancellationToken ct, Guid? itemId = null)
    {
        var items = db.Items.AsNoTracking();
        if (itemId is { } only) items = db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == only);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = LikePattern(q);
            items = items.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") || EF.Functions.ILike(x.Sku, pattern, "\\"));
        }
        if (categoryId is { } category) items = items.Where(x => x.CategoryId == category);
        var movements = db.StockMovements.Where(m => asOf == null || m.OccurredAt < asOf);
        var prices = db.ItemPriceHistory.Where(p => asOf == null || p.EffectiveFrom < asOf);
        return await items.Select(i => new Row(i.Id, i.Sku, i.Name, i.Category!.Name, i.ReorderLevel, i.ReorderQuantity,
                movements.Where(m => m.ItemId == i.Id).Sum(m => (decimal?)m.QtyChange) ?? 0,
                movements.Where(m => m.ItemId == i.Id).OrderByDescending(m => m.Sequence).Select(m => (decimal?)m.AverageCostAfter).FirstOrDefault() ?? 0,
                prices.Where(p => p.ItemId == i.Id).OrderByDescending(p => p.Sequence).Select(p => (decimal?)p.SellingPrice).FirstOrDefault()))
            .ToListAsync(ct);
    }

    private static decimal Value(Row r) => StockLedger.Money(r.OnHand * r.AverageCost);

    private static StockRow ToDto(Row r) => new(r.ItemId, r.Sku, r.Name, r.CategoryName, r.OnHand, r.ReorderLevel, r.ReorderQuantity,
        r.SellingPrice, r.AverageCost, Value(r));
}

public sealed class PriceService(AppDbContext db) : IPriceService
{
    public async Task<IReadOnlyList<PriceHistoryRow>> HistoryAsync(Guid itemId, CancellationToken ct)
    {
        if (!await db.Items.IgnoreQueryFilters().AnyAsync(x => x.Id == itemId, ct)) throw new NotFoundException("Item not found.");
        return await db.ItemPriceHistory.AsNoTracking().Where(x => x.ItemId == itemId).OrderByDescending(x => x.Sequence).Take(200)
            .Select(x => new PriceHistoryRow(x.Sequence, x.EffectiveFrom, x.SellingPrice, x.CostPrice, x.SourceType, x.SourceId, x.Reason, x.CreatedBy))
            .ToListAsync(ct);
    }

    public async Task<PriceHistoryRow> ChangeAsync(Guid itemId, PriceChangeInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ledger = new StockLedger(db);
        await ledger.LockAsync([itemId], ct);
        if (ledger[itemId].SellingPrice == input.SellingPrice) throw Invalid(nameof(input.SellingPrice), "That is already the current price.");
        ledger.SetPrice(itemId, input.SellingPrice, PriceSource.PriceChange, null, input.Reason.Trim());
        await StockLedger.CommitAsync(db, transaction, ct);
        return (await HistoryAsync(itemId, ct))[0];
    }
}
