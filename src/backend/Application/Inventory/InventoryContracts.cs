using Application.Catalog;
using Domain.Inventory;
using System.Text.Json.Serialization;

namespace Application.Inventory;

// ---- Goods received ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GrnLineInput(Guid ItemId, decimal Quantity, decimal UnitCost, decimal? SellingPrice);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GrnInput(string SupplierName, string? SupplierReference, DateOnly? SupplierDocumentDate, string? Notes,
    IReadOnlyList<GrnLineInput> Lines, uint? Version);
public sealed record GrnLineDto(Guid Id, int LineNo, Guid ItemId, string Sku, string ItemName, decimal Quantity, decimal UnitCost,
    decimal? SellingPrice, decimal? CurrentPrice, decimal LineTotal);
public sealed record GrnDto(Guid Id, string? GrnNumber, string SupplierName, string? SupplierReference, DateOnly? SupplierDocumentDate,
    string? Notes, DocumentStatus Status, DateTimeOffset? PostedAt, string? PostedBy, DateTimeOffset? ReversedAt, string? ReversalReason,
    decimal Total, IReadOnlyList<GrnLineDto> Lines, uint Version, DateTimeOffset UpdatedAt);
public sealed record GrnSummaryDto(Guid Id, string? GrnNumber, string SupplierName, string? SupplierReference, DocumentStatus Status,
    int LineCount, decimal Units, decimal Total, DateTimeOffset? PostedAt, DateTimeOffset UpdatedAt);
public sealed record DocumentListRequest(int Page = 1, int PageSize = 25, string? Q = null, DocumentStatus? Status = null, string? Sort = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseInput(string Reason);

// ---- Adjustments and opening stock ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockDocumentLineInput(Guid ItemId, decimal QtyChange, decimal? UnitCost, decimal? SellingPrice);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockDocumentInput(AdjustmentReason Reason, string? Notes, IReadOnlyList<StockDocumentLineInput> Lines, uint? Version);
public sealed record StockDocumentLineDto(Guid Id, int LineNo, Guid ItemId, string Sku, string ItemName, decimal QtyChange, decimal? UnitCost, decimal? SellingPrice);
public sealed record StockDocumentDto(Guid Id, StockDocumentKind Kind, string? Number, AdjustmentReason Reason, string? Notes, DocumentStatus Status,
    DateTimeOffset? PostedAt, string? PostedBy, Guid? StocktakeId, IReadOnlyList<StockDocumentLineDto> Lines, uint Version, DateTimeOffset UpdatedAt);
public sealed record StockDocumentSummaryDto(Guid Id, StockDocumentKind Kind, string? Number, AdjustmentReason Reason, DocumentStatus Status,
    int LineCount, decimal NetUnits, DateTimeOffset? PostedAt, DateTimeOffset UpdatedAt);

public sealed record OpeningImportRow(int Line, string Sku, decimal? Quantity, decimal? UnitCost, decimal? SellingPrice, Guid? ItemId, string? ItemName, string? Error);
public sealed record OpeningImportResult(bool DryRun, int Valid, int Invalid, IReadOnlyList<OpeningImportRow> Rows, Guid? DraftId);

// ---- Stocktake ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StocktakeLineInput(Guid ItemId, decimal CountedQty);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StocktakeInput(string? Notes, IReadOnlyList<StocktakeLineInput> Lines, uint? Version);
public sealed record StocktakeLineDto(Guid Id, Guid ItemId, string Sku, string ItemName, decimal CountedQty, decimal OnHandNow, decimal? SystemQtyAtPost,
    decimal Difference, bool MovedSinceStart);
public sealed record StocktakeDto(Guid Id, string? Number, string? Notes, DocumentStatus Status, DateTimeOffset StartedAt, DateTimeOffset? PostedAt,
    Guid? AdjustmentId, IReadOnlyList<StocktakeLineDto> Lines, uint Version, DateTimeOffset UpdatedAt);
public sealed record StocktakeSummaryDto(Guid Id, string? Number, DocumentStatus Status, int LineCount, DateTimeOffset StartedAt, DateTimeOffset? PostedAt);

// ---- Stock reports ----
public sealed record StockRequest(int Page = 1, int PageSize = 50, string? Q = null, Guid? CategoryId = null,
    DateOnly? Date = null, DateTimeOffset? AsOf = null, string? Sort = null, bool OnlyInStock = false);
public sealed record StockRow(Guid ItemId, string Sku, string Name, string CategoryName, decimal OnHand, int? ReorderLevel, int? ReorderQuantity,
    decimal? SellingPrice, decimal AverageCost, decimal StockValue, decimal Reserved = 0, decimal Available = 0);
public sealed record StockReport(DateTimeOffset? AsOfUtc, decimal TotalUnits, decimal TotalValue, PagedResult<StockRow> Items);
public sealed record StockCardRow(long Sequence, DateTimeOffset OccurredAt, MovementType MovementType, decimal QtyChange, decimal Balance,
    decimal UnitCost, decimal AverageCostAfter, string RefType, Guid RefId, string? DocumentNumber, string CreatedBy);
public sealed record StockCard(Guid ItemId, string Sku, string Name, decimal OpeningBalance, decimal OnHand, decimal AverageCost, decimal? SellingPrice,
    IReadOnlyList<StockCardRow> Movements);

// ---- Prices ----
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PriceChangeInput(decimal SellingPrice, string Reason);
public sealed record PriceHistoryRow(long Sequence, DateTimeOffset EffectiveFrom, decimal SellingPrice, decimal? CostPrice, PriceSource SourceType,
    Guid? SourceId, string? Reason, string CreatedBy);

public interface IGrnService
{
    Task<PagedResult<GrnSummaryDto>> ListAsync(DocumentListRequest request, CancellationToken ct);
    Task<GrnDto> GetAsync(Guid id, CancellationToken ct);
    Task<GrnDto> CreateAsync(GrnInput input, CancellationToken ct);
    Task<GrnDto> UpdateAsync(Guid id, GrnInput input, CancellationToken ct);
    Task<GrnDto> PostAsync(Guid id, CancellationToken ct);
    Task<GrnDto> CancelAsync(Guid id, CancellationToken ct);
    Task<GrnDto> ReverseAsync(Guid id, ReverseInput input, CancellationToken ct);
    Task<IReadOnlyList<string>> SupplierSuggestionsAsync(string? q, CancellationToken ct);
}

public interface IStockDocumentService
{
    Task<PagedResult<StockDocumentSummaryDto>> ListAsync(StockDocumentKind kind, DocumentListRequest request, CancellationToken ct);
    Task<StockDocumentDto> GetAsync(StockDocumentKind kind, Guid id, CancellationToken ct);
    Task<StockDocumentDto> CreateAsync(StockDocumentKind kind, StockDocumentInput input, CancellationToken ct);
    Task<StockDocumentDto> UpdateAsync(StockDocumentKind kind, Guid id, StockDocumentInput input, CancellationToken ct);
    Task<StockDocumentDto> PostAsync(StockDocumentKind kind, Guid id, CancellationToken ct);
    Task<StockDocumentDto> CancelAsync(StockDocumentKind kind, Guid id, CancellationToken ct);
    Task<OpeningImportResult> ImportOpeningAsync(string csv, bool dryRun, CancellationToken ct);
    Task<byte[]> ExportOpeningWorkbookAsync(CancellationToken ct);
    Task<OpeningImportResult> ImportOpeningWorkbookAsync(Stream workbook, bool dryRun, CancellationToken ct);
}

public interface IStocktakeService
{
    Task<PagedResult<StocktakeSummaryDto>> ListAsync(DocumentListRequest request, CancellationToken ct);
    Task<StocktakeDto> GetAsync(Guid id, CancellationToken ct);
    Task<StocktakeDto> CreateAsync(StocktakeInput input, CancellationToken ct);
    Task<StocktakeDto> UpdateAsync(Guid id, StocktakeInput input, CancellationToken ct);
    Task<StocktakeDto> PostAsync(Guid id, CancellationToken ct);
    Task<StocktakeDto> CancelAsync(Guid id, CancellationToken ct);
}

public interface IStockQueryService
{
    Task<StockReport> StockAsync(StockRequest request, CancellationToken ct);
    Task<IReadOnlyList<StockRow>> LowStockAsync(CancellationToken ct);
    Task<StockCard> StockCardAsync(Guid itemId, DateOnly? from, DateOnly? to, CancellationToken ct);
}

public interface IPriceService
{
    Task<IReadOnlyList<PriceHistoryRow>> HistoryAsync(Guid itemId, CancellationToken ct);
    Task<PriceHistoryRow> ChangeAsync(Guid itemId, PriceChangeInput input, CancellationToken ct);
}
