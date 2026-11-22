using System.Globalization;
using Application.Catalog;
using Application.Common;
using Application.Inventory;
using Domain.Catalog;
using Domain.Inventory;
using FluentValidation;
using FluentValidation.Results;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Inventory;

/// <summary>Stock adjustments and opening stock (same tables, different kind and rules).</summary>
public sealed class StockDocumentService(AppDbContext db, ICurrentActor actor) : IStockDocumentService
{
    public const int MaxImportRows = InventoryRules.MaxOpeningLines;

    public async Task<byte[]> ExportOpeningWorkbookAsync(CancellationToken ct)
    {
        var items = await db.Items.AsNoTracking().OrderBy(x => x.Sku)
            .Select(x => new OpeningStockWorkbook.ItemRow(x.Sku, x.Name, db.StockMovements.Any(m => m.ItemId == x.Id)))
            .ToListAsync(ct);
        return OpeningStockWorkbook.Write(items, ct);
    }

    public async Task<OpeningImportResult> ImportOpeningWorkbookAsync(Stream workbook, bool dryRun, CancellationToken ct)
    {
        var rows = OpeningStockWorkbook.Read(workbook, ct);
        return await ImportOpeningRowsAsync(rows, "Imported from Excel", dryRun, ct);
    }

    public async Task<PagedResult<StockDocumentSummaryDto>> ListAsync(StockDocumentKind kind, DocumentListRequest request, CancellationToken ct)
    {
        var query = db.StockDocuments.AsNoTracking().Where(x => x.Kind == kind);
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
            .Select(x => new StockDocumentSummaryDto(x.Id, x.Kind, x.Number, x.Reason, x.Status, x.Lines.Count, x.Lines.Sum(l => l.QtyChange), x.PostedAt, x.UpdatedAt))
            .ToListAsync(ct);
        return new(rows, request.Page, request.PageSize, total);
    }

    public Task<StockDocumentDto> GetAsync(StockDocumentKind kind, Guid id, CancellationToken ct) => DtoAsync(kind, id, ct);

    public async Task<StockDocumentDto> CreateAsync(StockDocumentKind kind, StockDocumentInput input, CancellationToken ct)
    {
        await CheckAsync(kind, input, ct);
        var document = new StockDocument { Kind = kind };
        db.StockDocuments.Add(document);
        Apply(document, input);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(kind, document.Id, ct);
    }

    public async Task<StockDocumentDto> UpdateAsync(StockDocumentKind kind, Guid id, StockDocumentInput input, CancellationToken ct)
    {
        var document = await db.StockDocuments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct) ?? throw NotFound(kind);
        if (document.Status != DocumentStatus.Draft || document.StocktakeId is not null) throw new ConflictException("Only draft documents can be edited.");
        ExpectVersion(db, document, input.Version);
        await CheckAsync(kind, input, ct);
        foreach (var line in document.Lines.ToList()) { db.StockDocumentLines.Remove(line); document.Lines.Remove(line); }
        Apply(document, input);
        db.Entry(document).State = EntityState.Modified;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(kind, id, ct);
    }

    public async Task<StockDocumentDto> PostAsync(StockDocumentKind kind, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM stock_documents WHERE \"Id\" = {id} FOR UPDATE", ct);
        var document = await db.StockDocuments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct) ?? throw NotFound(kind);
        if (document.Status == DocumentStatus.Posted) return await DtoAsync(kind, id, ct);
        if (document.Status != DocumentStatus.Draft) throw new ConflictException("This document cannot be posted.");
        if (document.Lines.Count == 0) throw Invalid("Lines", "Add at least one line before posting.");
        await PostLinesAsync(db, actor, document, ct);
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(kind, id, ct);
    }

    /// <summary>Posts a draft document's lines into the ledger. Caller owns the transaction and commit.</summary>
    internal static async Task PostLinesAsync(AppDbContext db, ICurrentActor actor, StockDocument document, CancellationToken ct)
    {
        var ledger = new StockLedger(db);
        await ledger.LockAsync(document.Lines.Select(x => x.ItemId), ct);
        foreach (var line in document.Lines.OrderBy(x => x.LineNo))
        {
            var reference = new MovementRef("StockDocument", document.Id, line.Id, $"stock-line:{line.Id}");
            if (document.Kind == StockDocumentKind.Opening)
            {
                if (ledger[line.ItemId].HasMovements)
                    throw new ConflictException($"{ledger[line.ItemId].Sku} already has stock history. Use an adjustment instead of opening stock.");
                ledger.Receive(line.ItemId, MovementType.Opening, line.QtyChange, line.UnitCost ?? 0, reference);
                if (line.SellingPrice is { } price) ledger.SetPrice(line.ItemId, price, PriceSource.Opening, document.Id, null);
            }
            else if (line.QtyChange > 0)
                ledger.Receive(line.ItemId, MovementType.Adjustment, line.QtyChange, line.UnitCost ?? ledger[line.ItemId].AverageCost, reference);
            else
                ledger.Remove(line.ItemId, MovementType.Adjustment, -line.QtyChange, reference);
        }
        ledger.EnsureNotNegative();
        if (document.StocktakeId is null) ledger.EnsureCoversReservations();
        document.Number = await DocumentNumbers.NextAsync(db, document.Kind == StockDocumentKind.Opening ? "OPN" : "ADJ", actor.ActorId, ct);
        document.Status = DocumentStatus.Posted;
        document.PostedBy = actor.ActorId;
    }

    public async Task<StockDocumentDto> CancelAsync(StockDocumentKind kind, Guid id, CancellationToken ct)
    {
        var document = await db.StockDocuments.SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct) ?? throw NotFound(kind);
        if (document.Status == DocumentStatus.Cancelled) return await DtoAsync(kind, id, ct);
        if (document.Status != DocumentStatus.Draft) throw new ConflictException("Only drafts can be cancelled.");
        document.Status = DocumentStatus.Cancelled;
        await SaveAsync(db, "This record was changed by someone else.", ct);
        return await DtoAsync(kind, id, ct);
    }

    /// <summary>
    /// Reads "sku,quantity,unit_cost,selling_price" rows (header optional). A dry run only reports;
    /// otherwise a draft opening-stock document is created for review and posting.
    /// </summary>
    public async Task<OpeningImportResult> ImportOpeningAsync(string csv, bool dryRun, CancellationToken ct)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > MaxImportRows + 1) throw Invalid("File", $"At most {MaxImportRows} rows per import.");
        var parsed = new List<(int Line, string Sku, string[] Cells)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Trim();
            if (text.Length == 0) continue;
            var cells = text.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (i == 0 && cells[0].Equals("sku", StringComparison.OrdinalIgnoreCase)) continue;
            parsed.Add((i + 1, Item.NormalizeSku(cells[0]), cells));
        }
        return await ImportOpeningRowsAsync(parsed, "Imported from CSV", dryRun, ct);
    }

    private async Task<OpeningImportResult> ImportOpeningRowsAsync(
        IReadOnlyList<(int Line, string Sku, string[] Cells)> parsed, string notes, bool dryRun, CancellationToken ct)
    {
        if (parsed.Count > MaxImportRows) throw Invalid("File", $"At most {MaxImportRows} filled rows per import.");
        var skus = parsed.Select(x => x.Sku).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(x => skus.Contains(x.Sku)).ToDictionaryAsync(x => x.Sku, x => new { x.Id, x.Name }, ct);
        var itemIds = items.Values.Select(v => v.Id).ToArray();
        var withStock = (await db.StockMovements.Where(m => itemIds.Contains(m.ItemId)).Select(m => m.ItemId).Distinct().ToListAsync(ct)).ToHashSet();
        var seen = new HashSet<string>();
        var rows = new List<OpeningImportRow>();
        foreach (var (line, sku, cells) in parsed)
        {
            decimal? Number(int index) => cells.Length > index && decimal.TryParse(cells[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            var (qty, cost, price) = (Number(1), Number(2), cells.Length > 3 && cells[3].Length > 0 ? Number(3) : null);
            string? error = null;
            if (cells.Length < 3) error = "Expected: sku, quantity, unit cost, selling price (optional).";
            else if (!items.TryGetValue(sku, out _)) error = "No item with this SKU.";
            else if (!seen.Add(sku)) error = "This SKU appears more than once.";
            else if (withStock.Contains(items[sku].Id)) error = "This item already has stock history.";
            else if (qty is not > 0 || !InventoryRules.ValidQuantityScale(qty.Value) || qty > InventoryRules.MaxQuantity) error = "Quantity must be more than 0, at most 3 decimals.";
            else if (cost is not >= 0 || cost > InventoryRules.MaxMoney || !InventoryRules.HasCents(cost.Value)) error = $"Unit cost must be between 0 and {InventoryRules.MaxMoney}, in dollars and cents.";
            else if (cells.Length > 3 && cells[3].Length > 0 && (price is not > 0 || price > InventoryRules.MaxMoney || !InventoryRules.HasCents(price.Value))) error = $"Selling price must be more than 0 and at most {InventoryRules.MaxMoney}, in dollars and cents.";
            var found = items.TryGetValue(sku, out var it) ? it : null;
            rows.Add(new OpeningImportRow(line, sku, qty, cost, price, found?.Id, found?.Name, error));
        }
        var valid = rows.Count(r => r.Error is null);
        var invalid = rows.Count - valid;
        if (dryRun || invalid > 0 || valid == 0) return new OpeningImportResult(true, valid, invalid, rows, null);
        var draft = await CreateAsync(StockDocumentKind.Opening, new StockDocumentInput(AdjustmentReason.Opening, notes,
            rows.Select(r => new StockDocumentLineInput(r.ItemId!.Value, r.Quantity!.Value, r.UnitCost, r.SellingPrice)).ToList(), null), ct);
        return new OpeningImportResult(false, valid, 0, rows, draft.Id);
    }

    private async Task CheckAsync(StockDocumentKind kind, StockDocumentInput input, CancellationToken ct)
    {
        var problems = StockDocumentRules.Check(kind, input).ToList();
        if (problems.Count > 0) throw new ValidationException(problems.Select(p => new ValidationFailure(p.Property, p.Message)));
        var wanted = input.Lines.Select(x => x.ItemId).Distinct().ToList();
        if (await db.Items.CountAsync(x => wanted.Contains(x.Id), ct) != wanted.Count) throw Invalid("Lines", "One or more items do not exist.");
    }

    private void Apply(StockDocument document, StockDocumentInput input)
    {
        document.Reason = input.Reason;
        document.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            var row = new StockDocumentLine { DocumentId = document.Id, LineNo = lineNo++, ItemId = line.ItemId, QtyChange = line.QtyChange, UnitCost = line.UnitCost, SellingPrice = line.SellingPrice };
            document.Lines.Add(row);
            db.StockDocumentLines.Add(row);
        }
    }

    private static NotFoundException NotFound(StockDocumentKind kind) =>
        new(kind == StockDocumentKind.Opening ? "Opening stock document not found." : "Stock adjustment not found.");

    private async Task<StockDocumentDto> DtoAsync(StockDocumentKind kind, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var d = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct) ?? throw NotFound(kind);
        var ids = d.Lines.Select(x => x.ItemId).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.Sku, x.Name }, ct);
        return new StockDocumentDto(d.Id, d.Kind, d.Number, d.Reason, d.Notes, d.Status, d.PostedAt, d.PostedBy, d.StocktakeId,
            d.Lines.OrderBy(x => x.LineNo).Select(l => new StockDocumentLineDto(l.Id, l.LineNo, l.ItemId, items[l.ItemId].Sku, items[l.ItemId].Name,
                l.QtyChange, l.UnitCost, l.SellingPrice)).ToList(), d.Version, d.UpdatedAt);
    }
}
