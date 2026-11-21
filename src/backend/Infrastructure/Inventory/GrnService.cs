using Application.Catalog;
using Application.Common;
using Application.Inventory;
using Domain.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Inventory;

public sealed class GrnService(AppDbContext db, ICurrentActor actor) : IGrnService
{
    public async Task<PagedResult<GrnSummaryDto>> ListAsync(DocumentListRequest request, CancellationToken ct)
    {
        var query = db.Grns.AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.SupplierName, pattern, "\\") || (x.GrnNumber != null && EF.Functions.ILike(x.GrnNumber, pattern, "\\"))
                || (x.SupplierReference != null && EF.Functions.ILike(x.SupplierReference, pattern, "\\")));
        }
        query = (request.Sort switch
        {
            "updatedAt" => query.OrderBy(x => x.UpdatedAt),
            "postedAt" => query.OrderBy(x => x.PostedAt),
            "-postedAt" => query.OrderByDescending(x => x.PostedAt),
            "number" => query.OrderBy(x => x.GrnNumber),
            "-number" => query.OrderByDescending(x => x.GrnNumber),
            _ => query.OrderByDescending(x => x.UpdatedAt),
        }).ThenBy(x => x.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new GrnSummaryDto(x.Id, x.GrnNumber, x.SupplierName, x.SupplierReference, x.Status, x.Lines.Count,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.Quantity * l.UnitCost), x.PostedAt, x.UpdatedAt))
            .ToListAsync(ct);
        return new(rows.Select(r => r with { Total = StockLedger.Money(r.Total) }).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<GrnDto> GetAsync(Guid id, CancellationToken ct) => await DtoAsync(id, ct);

    public async Task<GrnDto> CreateAsync(GrnInput input, CancellationToken ct)
    {
        await EnsureItemsExistAsync(input.Lines.Select(x => x.ItemId), ct);
        var grn = new Grn();
        db.Grns.Add(grn);
        Apply(grn, input);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(grn.Id, ct);
    }

    public async Task<GrnDto> UpdateAsync(Guid id, GrnInput input, CancellationToken ct)
    {
        var grn = await db.Grns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Goods received note not found.");
        if (grn.Status != DocumentStatus.Draft) throw new ConflictException("Only draft goods received notes can be edited.");
        ExpectVersion(db, grn, input.Version);
        await EnsureItemsExistAsync(input.Lines.Select(x => x.ItemId), ct);
        foreach (var line in grn.Lines.ToList()) { db.GrnLines.Remove(line); grn.Lines.Remove(line); }
        Apply(grn, input);
        db.Entry(grn).State = EntityState.Modified;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(grn.Id, ct);
    }

    public async Task<GrnDto> PostAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var grn = await LockAsync(id, ct);
        if (grn.Status == DocumentStatus.Posted) return await DtoAsync(id, ct); // already posted: nothing to do
        if (grn.Status != DocumentStatus.Draft) throw new ConflictException("This goods received note cannot be posted.");
        if (grn.Lines.Count == 0) throw Invalid("Lines", "Add at least one line before posting.");

        var ledger = new StockLedger(db);
        await ledger.LockAsync(grn.Lines.Select(x => x.ItemId), ct);
        foreach (var line in grn.Lines.OrderBy(x => x.LineNo))
        {
            ledger.Receive(line.ItemId, MovementType.Grn, line.Quantity, line.UnitCost,
                new MovementRef("Grn", grn.Id, line.Id, $"grn-line:{line.Id}"));
            if (line.SellingPrice is { } price) ledger.SetPrice(line.ItemId, price, PriceSource.Grn, grn.Id, null);
            else if (ledger[line.ItemId].SellingPrice is null)
                throw Invalid($"Lines[{line.LineNo - 1}].SellingPrice", $"{ledger[line.ItemId].Sku} has no selling price yet. Enter one on this line.");
            // Freeze the effective receipt price even when the draft left it blank.
            line.SellingPrice ??= ledger[line.ItemId].SellingPrice;
        }
        // Persist line snapshots while the parent is still Draft. The database locks
        // posted lines, and EF may otherwise update the header before its children.
        await db.SaveChangesAsync(ct);
        grn.GrnNumber = await DocumentNumbers.NextAsync(db, "GRN", actor.ActorId, ct);
        grn.Status = DocumentStatus.Posted;
        grn.PostedBy = actor.ActorId;
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    public async Task<GrnDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var grn = await db.Grns.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Goods received note not found.");
        if (grn.Status == DocumentStatus.Cancelled) return await DtoAsync(id, ct);
        if (grn.Status != DocumentStatus.Draft) throw new ConflictException("Only drafts can be cancelled. Reverse a posted goods received note instead.");
        grn.Status = DocumentStatus.Cancelled;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(id, ct);
    }

    public async Task<GrnDto> ReverseAsync(Guid id, ReverseInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var grn = await LockAsync(id, ct);
        if (grn.Status == DocumentStatus.Reversed) return await DtoAsync(id, ct);
        if (grn.Status != DocumentStatus.Posted) throw new ConflictException("Only posted goods received notes can be reversed.");
        var originals = await db.StockMovements.Where(m => m.RefType == "Grn" && m.RefId == grn.Id && m.MovementType == MovementType.Grn)
            .OrderBy(m => m.Sequence).ToListAsync(ct);
        var ledger = new StockLedger(db);
        await ledger.LockAsync(originals.Select(x => x.ItemId), ct);
        foreach (var m in originals)
            ledger.Unreceive(m.ItemId, m.QtyChange, m.UnitCost, new MovementRef("Grn", grn.Id, m.RefLineId, $"grn-reversal:{m.Id}", m.Id));
        ledger.EnsureNotNegative();
        ledger.EnsureCoversReservations();
        grn.Status = DocumentStatus.Reversed;
        grn.ReversedBy = actor.ActorId;
        grn.ReversalReason = input.Reason.Trim();
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    public async Task<IReadOnlyList<string>> SupplierSuggestionsAsync(string? q, CancellationToken ct)
    {
        var query = db.Grns.AsNoTracking().Where(x => x.Status != DocumentStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.SupplierName, LikePattern(q), "\\"));
        return await query.GroupBy(x => x.SupplierName).OrderByDescending(g => g.Max(x => x.UpdatedAt)).Select(g => g.Key).Take(10).ToListAsync(ct);
    }

    private async Task<Grn> LockAsync(Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM grns WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await db.Grns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Goods received note not found.");
    }

    private void Apply(Grn grn, GrnInput input)
    {
        grn.SupplierName = input.SupplierName.Trim();
        grn.SupplierReference = string.IsNullOrWhiteSpace(input.SupplierReference) ? null : input.SupplierReference.Trim();
        grn.SupplierDocumentDate = input.SupplierDocumentDate;
        grn.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            var row = new GrnLine { GrnId = grn.Id, LineNo = lineNo++, ItemId = line.ItemId, Quantity = line.Quantity, UnitCost = line.UnitCost, SellingPrice = line.SellingPrice };
            grn.Lines.Add(row);
            db.GrnLines.Add(row); // ids are set on construction, so add explicitly (ADR 0002)
        }
    }

    private async Task EnsureItemsExistAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var found = await db.Items.CountAsync(x => wanted.Contains(x.Id), ct);
        if (found != wanted.Count) throw Invalid("Lines", "One or more items do not exist.");
    }

    private async Task<GrnDto> DtoAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var grn = await db.Grns.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Goods received note not found.");
        var ids = grn.Lines.Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.Sku, x.Name }, ct);
        var prices = await StockLedger.CurrentPrices(db, ids).ToDictionaryAsync(x => x.ItemId, x => x.SellingPrice, ct);
        var lines = grn.Lines.OrderBy(x => x.LineNo).Select(l => new GrnLineDto(l.Id, l.LineNo, l.ItemId, items[l.ItemId].Sku, items[l.ItemId].Name,
            l.Quantity, l.UnitCost, l.SellingPrice, prices.TryGetValue(l.ItemId, out var p) ? p : null, StockLedger.Money(l.Quantity * l.UnitCost))).ToList();
        return new GrnDto(grn.Id, grn.GrnNumber, grn.SupplierName, grn.SupplierReference, grn.SupplierDocumentDate, grn.Notes, grn.Status,
            grn.PostedAt, grn.PostedBy, grn.ReversedAt, grn.ReversalReason, lines.Sum(x => x.LineTotal), lines, grn.Version, grn.UpdatedAt);
    }
}
