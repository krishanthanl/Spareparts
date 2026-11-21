using Application.Common;
using Domain.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Inventory;

/// <summary>Current position of one item while a posting is being built.</summary>
internal sealed class ItemPosition
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = "";
    public decimal OnHand { get; set; }
    public decimal AverageCost { get; set; }
    public decimal? SellingPrice { get; set; }
    public bool HasMovements { get; set; }
    /// <summary>Held for confirmed sales orders and not yet issued (ADR 0005, D4).</summary>
    public decimal Reserved { get; set; }
    public decimal Available => OnHand - Reserved;
}

/// <summary>
/// Builds ledger postings inside the caller's transaction. Call <see cref="LockAsync"/> first: it locks
/// the items' rows in id order so concurrent postings for the same item run one after another,
/// even across several API instances (ADR 0004).
/// </summary>
internal sealed class StockLedger(AppDbContext db)
{
    private readonly Dictionary<Guid, ItemPosition> positions = [];

    public IReadOnlyDictionary<Guid, ItemPosition> Positions => positions;

    public async Task LockAsync(IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToArray();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Stock postings need a transaction.");
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM items WHERE \"Id\" = ANY({0}) ORDER BY \"Id\" FOR UPDATE", [ids], ct);
        var items = await db.Items.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Sku }).ToListAsync(ct);
        var missing = ids.Except(items.Select(x => x.Id)).ToList();
        if (missing.Count > 0) throw new ConflictException("One or more items no longer exist or were deleted.");
        var stock = await db.StockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                OnHand = g.Sum(m => m.QtyChange),
                Average = g.OrderByDescending(m => m.Sequence).Select(m => m.AverageCostAfter).First(),
            }).ToListAsync(ct);
        var prices = await CurrentPrices(db, ids).ToListAsync(ct);
        var reserved = await Reservations(db, ids).ToDictionaryAsync(x => x.ItemId, x => x.Quantity, ct);
        foreach (var item in items)
        {
            var s = stock.SingleOrDefault(x => x.ItemId == item.Id);
            positions[item.Id] = new ItemPosition
            {
                ItemId = item.Id, Sku = item.Sku, OnHand = s?.OnHand ?? 0, AverageCost = s?.Average ?? 0, HasMovements = s is not null,
                SellingPrice = prices.SingleOrDefault(p => p.ItemId == item.Id)?.SellingPrice,
                Reserved = reserved.GetValueOrDefault(item.Id),
            };
        }
    }

    public ItemPosition this[Guid itemId] => positions.TryGetValue(itemId, out var p) ? p
        : throw new InvalidOperationException("Item was not locked before posting.");

    /// <summary>Adds stock at a cost; recalculates the weighted average (rounded to cents, D3).</summary>
    public StockMovement Receive(Guid itemId, MovementType type, decimal qty, decimal unitCost, MovementRef reference)
    {
        var p = this[itemId];
        var total = p.OnHand + qty;
        p.AverageCost = p.OnHand <= 0 || total <= 0 ? unitCost : Money((p.OnHand * p.AverageCost + qty * unitCost) / total);
        p.OnHand = total;
        return Add(p, type, qty, unitCost, reference);
    }

    /// <summary>Takes stock out at the current average cost. The average does not change.</summary>
    public StockMovement Remove(Guid itemId, MovementType type, decimal qty, MovementRef reference)
    {
        var p = this[itemId];
        p.OnHand -= qty;
        return Add(p, type, -qty, p.AverageCost, reference);
    }

    /// <summary>Takes back stock that was received at <paramref name="unitCost"/> (GRN reversal).</summary>
    public StockMovement Unreceive(Guid itemId, decimal qty, decimal unitCost, MovementRef reference)
    {
        var p = this[itemId];
        var remaining = p.OnHand - qty;
        if (remaining > 0)
        {
            var average = (p.OnHand * p.AverageCost - qty * unitCost) / remaining;
            if (average >= 0) p.AverageCost = Money(average);
        }
        p.OnHand = remaining;
        return Add(p, MovementType.GrnReversal, -qty, unitCost, reference);
    }

    /// <summary>Records a selling price when it differs from the current one.</summary>
    public void SetPrice(Guid itemId, decimal price, PriceSource source, Guid? sourceId, string? reason)
    {
        var p = this[itemId];
        if (p.SellingPrice == price) return;
        db.ItemPriceHistory.Add(new ItemPriceHistory
        {
            ItemId = itemId, SellingPrice = price, CostPrice = p.AverageCost, SourceType = source, SourceId = sourceId, Reason = reason,
        });
        p.SellingPrice = price;
    }

    /// <summary>Rejects the posting if any item would go below zero. The database re-checks at commit.</summary>
    public void EnsureNotNegative()
    {
        var short_ = positions.Values.FirstOrDefault(p => p.OnHand < 0);
        if (short_ is not null)
            throw new ConflictException($"Not enough stock of {short_.Sku}: this would leave {short_.OnHand:0.###}.");
    }

    /// <summary>
    /// Rejects removals that would leave less stock than confirmed orders are holding. Used by manual
    /// adjustments and GRN reversals; a stocktake records what is physically there, so it is not blocked.
    /// </summary>
    public void EnsureCoversReservations()
    {
        var short_ = positions.Values.FirstOrDefault(p => p.Available < 0 && p.Reserved > 0);
        if (short_ is not null)
            throw new ConflictException($"{short_.Sku}: {short_.Reserved:0.###} are reserved for confirmed orders, which would leave only {short_.OnHand:0.###} on hand. Issue or cancel those orders first.");
    }

    /// <summary>
    /// Reserved quantity per item: what confirmed orders still have to issue. Derived, never stored, so it
    /// cannot drift; postings read it under the same item locks that guard on-hand.
    /// </summary>
    public static IQueryable<ReservedQuantity> Reservations(AppDbContext db, IReadOnlyCollection<Guid>? ids = null) =>
        db.SalesOrderLines
            .Where(l => ids == null || ids.Contains(l.ItemId))
            .Where(l => db.SalesOrders.Any(o => o.Id == l.OrderId
                && (o.Status == Domain.Sales.SalesOrderStatus.Confirmed || o.Status == Domain.Sales.SalesOrderStatus.Completed)))
            .Select(l => new { l.ItemId, Open = l.Quantity - (db.StockIssueLines.Where(i => i.OrderLineId == l.Id).Sum(i => (decimal?)i.Quantity) ?? 0) })
            .Where(x => x.Open > 0)
            .GroupBy(x => x.ItemId)
            .Select(g => new ReservedQuantity { ItemId = g.Key, Quantity = g.Sum(x => x.Open) });

    private StockMovement Add(ItemPosition p, MovementType type, decimal qtyChange, decimal unitCost, MovementRef reference)
    {
        p.HasMovements = true;
        var movement = new StockMovement
        {
            ItemId = p.ItemId, MovementType = type, QtyChange = qtyChange, UnitCost = Money(unitCost), AverageCostAfter = p.AverageCost,
            RefType = reference.Type, RefId = reference.Id, RefLineId = reference.LineId, ReversesMovementId = reference.Reverses,
            PostingKey = reference.PostingKey,
        };
        db.StockMovements.Add(movement);
        return movement;
    }

    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static IQueryable<CurrentPrice> CurrentPrices(AppDbContext db, IReadOnlyCollection<Guid>? ids = null) =>
        db.ItemPriceHistory.Where(p => ids == null || ids.Contains(p.ItemId))
            .Where(p => !db.ItemPriceHistory.Any(q => q.ItemId == p.ItemId && q.Sequence > p.Sequence))
            .Select(p => new CurrentPrice { ItemId = p.ItemId, SellingPrice = p.SellingPrice });

    /// <summary>Saves and commits, turning database stock-rule failures into clear 409s.</summary>
    public static async Task CommitAsync(AppDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (Find(ex) is { } pg)
        {
            throw pg.SqlState switch
            {
                "23514" => new ConflictException("Not enough stock: the posting would make stock negative."),
                PostgresErrorCodes.UniqueViolation => new ConflictException("This document was already posted."),
                _ => new ConflictException("The posting was rejected by a stock rule."),
            };
        }
    }

    private static PostgresException? Find(Exception ex) => ex switch
    {
        PostgresException pg when pg.SqlState is "23514" or PostgresErrorCodes.UniqueViolation or "P0001" => pg,
        { InnerException: { } inner } => Find(inner),
        _ => null,
    };
}

internal sealed record CurrentPrice
{
    public Guid ItemId { get; init; }
    public decimal SellingPrice { get; init; }
}

internal sealed record ReservedQuantity
{
    public Guid ItemId { get; init; }
    public decimal Quantity { get; init; }
}

internal sealed record MovementRef(string Type, Guid Id, Guid? LineId, string PostingKey, Guid? Reverses = null);

/// <summary>Gapless document numbers. The counter row stays locked until the caller's transaction ends.</summary>
internal static class DocumentNumbers
{
    public static async Task<string> NextAsync(AppDbContext db, string type, string actor, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO document_counters ("Id", "DocumentType", "NextValue", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy")
            VALUES ({Guid.NewGuid()}, {type}, 1, now(), {actor}, now(), {actor})
            ON CONFLICT ("DocumentType") DO NOTHING
            """, ct);
        var value = await db.Database.SqlQuery<long>($"""
            UPDATE document_counters SET "NextValue" = "NextValue" + 1, "UpdatedAt" = now(), "UpdatedBy" = {actor}
            WHERE "DocumentType" = {type} RETURNING "NextValue" - 1 AS "Value"
            """).ToListAsync(ct);
        return $"{type}-{value.Single():D6}";
    }
}
