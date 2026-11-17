using Domain.Common;

namespace Domain.Inventory;

public enum DocumentStatus
{
    Draft = 1,
    Posted = 2,
    Reversed = 3,
    Cancelled = 4,
}

public enum MovementType
{
    Opening = 1,
    Grn = 2,
    GrnReversal = 3,
    Adjustment = 4,
    Issue = 5,
    Return = 6,
    Reversal = 7,
}

public enum StockDocumentKind
{
    Adjustment = 1,
    Opening = 2,
}

public enum AdjustmentReason
{
    Damage = 1,
    Lost = 2,
    Found = 3,
    Count = 4,
    Correction = 5,
    Opening = 6,
    Other = 7,
}

public enum PriceSource
{
    Opening = 1,
    Grn = 2,
    PriceChange = 3,
}

/// <summary>Goods received note. Only posting changes stock; drafts can be edited or cancelled.</summary>
public sealed class Grn : AuditableEntity
{
    public string? GrnNumber { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    /// <summary>Supplier invoice/delivery number. Information only.</summary>
    public string? SupplierReference { get; set; }
    /// <summary>Date on the supplier's paperwork. Information only; never the posting time.</summary>
    public DateOnly? SupplierDocumentDate { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public uint Version { get; set; }
    public List<GrnLine> Lines { get; } = [];
}

public sealed class GrnLine : AuditableEntity
{
    public Guid GrnId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>New selling price from this receipt. Null keeps the current price.</summary>
    public decimal? SellingPrice { get; set; }
}

/// <summary>Stock adjustment or opening stock document.</summary>
public sealed class StockDocument : AuditableEntity
{
    public StockDocumentKind Kind { get; set; }
    public string? Number { get; set; }
    public AdjustmentReason Reason { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public Guid? StocktakeId { get; set; }
    public uint Version { get; set; }
    public List<StockDocumentLine> Lines { get; } = [];
}

public sealed class StockDocumentLine : AuditableEntity
{
    public Guid DocumentId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public decimal QtyChange { get; set; }
    /// <summary>Cost of stock added (opening stock, positive adjustments). Null uses the current average cost.</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Opening stock only: the item's first selling price.</summary>
    public decimal? SellingPrice { get; set; }
}

public sealed class Stocktake : AuditableEntity
{
    public string? Number { get; set; }
    public string? Notes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public uint Version { get; set; }
    public List<StocktakeLine> Lines { get; } = [];
}

public sealed class StocktakeLine : AuditableEntity
{
    public Guid StocktakeId { get; set; }
    public Guid ItemId { get; set; }
    public decimal CountedQty { get; set; }
    public decimal? SystemQtyAtPost { get; set; }
}

/// <summary>
/// Append-only stock ledger. Never updated or deleted (database triggers enforce it).
/// On-hand at any instant is SUM(QtyChange) of rows with OccurredAt before that instant.
/// </summary>
public sealed class StockMovement : AuditableEntity
{
    public long Sequence { get; set; }
    public Guid ItemId { get; set; }
    public MovementType MovementType { get; set; }
    public decimal QtyChange { get; set; }
    /// <summary>Set by the database to the posting time; any value sent by the application is replaced.</summary>
    public DateTimeOffset OccurredAt { get; set; }
    public decimal UnitCost { get; set; }
    public decimal AverageCostAfter { get; set; }
    public string RefType { get; set; } = string.Empty;
    public Guid RefId { get; set; }
    public Guid? RefLineId { get; set; }
    public Guid? ReversesMovementId { get; set; }
    public string PostingKey { get; set; } = string.Empty;
}

/// <summary>Append-only selling price history. The current price is the latest row.</summary>
public sealed class ItemPriceHistory : AuditableEntity
{
    public long Sequence { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>Set by the database to the posting time.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public PriceSource SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Gapless document numbers. Each row is locked while a number is taken.</summary>
public sealed class DocumentCounter : AuditableEntity
{
    public string DocumentType { get; set; } = string.Empty;
    public long NextValue { get; set; } = 1;
}
