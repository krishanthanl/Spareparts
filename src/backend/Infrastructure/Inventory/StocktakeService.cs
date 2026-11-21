using Application.Catalog;
using Application.Common;
using Application.Inventory;
using Domain.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Inventory;

public sealed class StocktakeService(AppDbContext db, ICurrentActor actor, TimeProvider clock) : IStocktakeService
{
    public async Task<PagedResult<StocktakeSummaryDto>> ListAsync(DocumentListRequest request, CancellationToken ct)
    {
        var query = db.Stocktakes.AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => (x.Number != null && EF.Functions.ILike(x.Number, pattern, "\\")) || (x.Notes != null && EF.Functions.ILike(x.Notes, pattern, "\\")));
        }
        query = (request.Sort switch
        {
            "updatedAt" => query.OrderBy(x => x.UpdatedAt),
            "postedAt" => query.OrderBy(x => x.PostedAt),
            "-postedAt" => query.OrderByDescending(x => x.PostedAt),
            "number" => query.OrderBy(x => x.Number),
            "-number" => query.OrderByDescending(x => x.Number),
            _ => query.OrderByDescending(x => x.UpdatedAt),
        }).ThenBy(x => x.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new StocktakeSummaryDto(x.Id, x.Number, x.Status, x.Lines.Count, x.StartedAt, x.PostedAt)).ToListAsync(ct);
        return new(rows, request.Page, request.PageSize, total);
    }

    public Task<StocktakeDto> GetAsync(Guid id, CancellationToken ct) => DtoAsync(id, ct);

    public async Task<StocktakeDto> CreateAsync(StocktakeInput input, CancellationToken ct)
    {
        await EnsureItemsAsync(input, ct);
        var stocktake = new Stocktake { StartedAt = clock.GetUtcNow() };
        db.Stocktakes.Add(stocktake);
        Apply(stocktake, input);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(stocktake.Id, ct);
    }

    public async Task<StocktakeDto> UpdateAsync(Guid id, StocktakeInput input, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Stocktake not found.");
        if (stocktake.Status != DocumentStatus.Draft) throw new ConflictException("Only draft stocktakes can be edited.");
        ExpectVersion(db, stocktake, input.Version);
        await EnsureItemsAsync(input, ct);
        foreach (var line in stocktake.Lines.ToList()) { db.StocktakeLines.Remove(line); stocktake.Lines.Remove(line); }
        Apply(stocktake, input);
        db.Entry(stocktake).State = EntityState.Modified;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(id, ct);
    }

    /// <summary>
    /// Compares counts with on-hand at this moment and posts one Count adjustment for the differences.
    /// Steps are saved in order because posted lines are locked by database triggers.
    /// </summary>
    public async Task<StocktakeDto> PostAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM stocktakes WHERE \"Id\" = {id} FOR UPDATE", ct);
        var stocktake = await db.Stocktakes.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Stocktake not found.");
        if (stocktake.Status == DocumentStatus.Posted) return await DtoAsync(id, ct);
        if (stocktake.Status != DocumentStatus.Draft) throw new ConflictException("This stocktake cannot be posted.");
        if (stocktake.Lines.Count == 0) throw Invalid("Lines", "Add at least one item before posting.");

        // 1. Freeze what the system had, under the item locks the adjustment will also use.
        var ledger = new StockLedger(db);
        await ledger.LockAsync(stocktake.Lines.Select(x => x.ItemId), ct);
        foreach (var line in stocktake.Lines) line.SystemQtyAtPost = ledger[line.ItemId].OnHand;
        await db.SaveChangesAsync(ct);

        // 2. Draft adjustment for the differences (none if every count matches).
        var differences = stocktake.Lines.Where(l => l.CountedQty != l.SystemQtyAtPost).OrderBy(l => l.Id).ToList();
        if (differences.Count > 0)
        {
            var adjustment = new StockDocument { Kind = StockDocumentKind.Adjustment, Reason = AdjustmentReason.Count, StocktakeId = stocktake.Id, Notes = "Stocktake differences" };
            db.StockDocuments.Add(adjustment);
            var lineNo = 1;
            foreach (var line in differences)
                db.StockDocumentLines.Add(new StockDocumentLine
                {
                    DocumentId = adjustment.Id, LineNo = lineNo++, ItemId = line.ItemId, QtyChange = line.CountedQty - line.SystemQtyAtPost!.Value,
                });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            var draft = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == adjustment.Id, ct);
            await StockDocumentService.PostLinesAsync(db, actor, draft, ct);
            stocktake = await db.Stocktakes.SingleAsync(x => x.Id == id, ct);
        }

        // 3. Close the stocktake.
        stocktake.Number = await DocumentNumbers.NextAsync(db, "STK", actor.ActorId, ct);
        stocktake.Status = DocumentStatus.Posted;
        stocktake.PostedBy = actor.ActorId;
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    public async Task<StocktakeDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var stocktake = await db.Stocktakes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Stocktake not found.");
        if (stocktake.Status == DocumentStatus.Cancelled) return await DtoAsync(id, ct);
        if (stocktake.Status != DocumentStatus.Draft) throw new ConflictException("Only drafts can be cancelled.");
        stocktake.Status = DocumentStatus.Cancelled;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(id, ct);
    }

    private void Apply(Stocktake stocktake, StocktakeInput input)
    {
        stocktake.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        foreach (var line in input.Lines)
        {
            var row = new StocktakeLine { StocktakeId = stocktake.Id, ItemId = line.ItemId, CountedQty = line.CountedQty };
            stocktake.Lines.Add(row);
            db.StocktakeLines.Add(row);
        }
    }

    private async Task EnsureItemsAsync(StocktakeInput input, CancellationToken ct)
    {
        var wanted = input.Lines.Select(x => x.ItemId).Distinct().ToList();
        if (await db.Items.CountAsync(x => wanted.Contains(x.Id), ct) != wanted.Count) throw Invalid("Lines", "One or more items do not exist.");
    }

    private async Task<StocktakeDto> DtoAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var s = await db.Stocktakes.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Stocktake not found.");
        var ids = s.Lines.Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.Sku, x.Name }, ct);
        var onHand = await db.StockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.QtyChange) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var moved = (await db.StockMovements.Where(m => ids.Contains(m.ItemId) && m.OccurredAt > s.StartedAt)
            .Select(m => m.ItemId).Distinct().ToListAsync(ct)).ToHashSet();
        var adjustmentId = await db.StockDocuments.Where(x => x.StocktakeId == id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var lines = s.Lines.OrderBy(l => items[l.ItemId].Name).Select(l =>
        {
            var now = onHand.GetValueOrDefault(l.ItemId);
            var basis = l.SystemQtyAtPost ?? now;
            return new StocktakeLineDto(l.Id, l.ItemId, items[l.ItemId].Sku, items[l.ItemId].Name, l.CountedQty, now, l.SystemQtyAtPost,
                l.CountedQty - basis, s.Status == DocumentStatus.Draft && moved.Contains(l.ItemId));
        }).ToList();
        return new StocktakeDto(s.Id, s.Number, s.Notes, s.Status, s.StartedAt, s.PostedAt, adjustmentId, lines, s.Version, s.UpdatedAt);
    }
}
