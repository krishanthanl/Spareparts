using Domain.Catalog;
using Domain.Identity;
using Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class GrnConfiguration : IEntityTypeConfiguration<Grn>
{
    public void Configure(EntityTypeBuilder<Grn> builder)
    {
        builder.ToTable("grns", t => t.HasCheckConstraint("ck_grns_posted", "(\"Status\" = 1 OR \"Status\" = 4) = (\"GrnNumber\" IS NULL)"));
        builder.Property(x => x.GrnNumber).HasMaxLength(20);
        builder.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SupplierReference).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.PostedBy).HasMaxLength(200);
        builder.Property(x => x.ReversedBy).HasMaxLength(200);
        builder.Property(x => x.ReversalReason).HasMaxLength(500);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GrnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.GrnNumber).IsUnique().HasFilter("\"GrnNumber\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.PostedAt });
        builder.HasIndex(x => x.SupplierName);
    }
}

public sealed class GrnLineConfiguration : IEntityTypeConfiguration<GrnLine>
{
    public void Configure(EntityTypeBuilder<GrnLine> builder)
    {
        builder.ToTable("grn_lines", t =>
        {
            t.HasCheckConstraint("ck_grn_lines_quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("ck_grn_lines_cost", "\"UnitCost\" >= 0");
            t.HasCheckConstraint("ck_grn_lines_price", "\"SellingPrice\" IS NULL OR \"SellingPrice\" > 0");
        });
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrnId, x.LineNo }).IsUnique();
        builder.HasIndex(x => x.ItemId);
    }
}

public sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        builder.ToTable("stock_documents", t => t.HasCheckConstraint("ck_stock_documents_posted", "(\"Status\" = 1 OR \"Status\" = 4) = (\"Number\" IS NULL)"));
        builder.Property(x => x.Number).HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.PostedBy).HasMaxLength(200);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Stocktake>().WithMany().HasForeignKey(x => x.StocktakeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
        builder.HasIndex(x => new { x.Kind, x.Status, x.PostedAt });
    }
}

public sealed class StockDocumentLineConfiguration : IEntityTypeConfiguration<StockDocumentLine>
{
    public void Configure(EntityTypeBuilder<StockDocumentLine> builder)
    {
        builder.ToTable("stock_document_lines", t =>
        {
            t.HasCheckConstraint("ck_stock_document_lines_qty", "\"QtyChange\" <> 0");
            t.HasCheckConstraint("ck_stock_document_lines_cost", "\"UnitCost\" IS NULL OR \"UnitCost\" >= 0");
            t.HasCheckConstraint("ck_stock_document_lines_price", "\"SellingPrice\" IS NULL OR \"SellingPrice\" > 0");
        });
        builder.Property(x => x.QtyChange).HasPrecision(18, 3);
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.LineNo }).IsUnique();
        builder.HasIndex(x => x.ItemId);
    }
}

public sealed class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder)
    {
        builder.ToTable("stocktakes");
        builder.Property(x => x.Number).HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.PostedBy).HasMaxLength(200);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StocktakeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
    }
}

public sealed class StocktakeLineConfiguration : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> builder)
    {
        builder.ToTable("stocktake_lines", t => t.HasCheckConstraint("ck_stocktake_lines_counted", "\"CountedQty\" >= 0"));
        builder.Property(x => x.CountedQty).HasPrecision(18, 3);
        builder.Property(x => x.SystemQtyAtPost).HasPrecision(18, 3);
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StocktakeId, x.ItemId }).IsUnique();
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t =>
        {
            t.HasCheckConstraint("ck_stock_movements_qty", "\"QtyChange\" <> 0");
            t.HasCheckConstraint("ck_stock_movements_cost", "\"UnitCost\" >= 0 AND \"AverageCostAfter\" >= 0");
        });
        builder.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        builder.Property(x => x.OccurredAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.QtyChange).HasPrecision(18, 3);
        builder.Property(x => x.RefType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PostingKey).HasMaxLength(100).IsRequired();
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(x => x.ReversesMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Sequence).IsUnique();
        builder.HasIndex(x => x.PostingKey).IsUnique();
        builder.HasIndex(x => x.ReversesMovementId).IsUnique().HasFilter("\"ReversesMovementId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ItemId, x.OccurredAt }).IncludeProperties(x => x.QtyChange);
        builder.HasIndex(x => new { x.ItemId, x.Sequence });
        // Opening stock can be posted once per item.
        builder.HasIndex(x => x.ItemId).IsUnique().HasFilter("\"MovementType\" = 1").HasDatabaseName("ux_stock_movements_one_opening");
        builder.HasIndex(x => new { x.RefType, x.RefId });
    }
}

public sealed class ItemPriceHistoryConfiguration : IEntityTypeConfiguration<ItemPriceHistory>
{
    public void Configure(EntityTypeBuilder<ItemPriceHistory> builder)
    {
        builder.ToTable("item_price_history", t =>
        {
            t.HasCheckConstraint("ck_item_price_history_price", "\"SellingPrice\" > 0");
            t.HasCheckConstraint("ck_item_price_history_cost", "\"CostPrice\" IS NULL OR \"CostPrice\" >= 0");
        });
        builder.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        builder.Property(x => x.EffectiveFrom).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Sequence).IsUnique();
        builder.HasIndex(x => new { x.ItemId, x.Sequence });
    }
}

public sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> builder)
    {
        builder.ToTable("document_counters", t => t.HasCheckConstraint("ck_document_counters_next", "\"NextValue\" >= 1"));
        builder.Property(x => x.DocumentType).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.DocumentType).IsUnique();
    }
}

public sealed class PermissionSeedConfiguration : IEntityTypeConfiguration<PermissionSeed>
{
    public void Configure(EntityTypeBuilder<PermissionSeed> builder)
    {
        builder.ToTable("permission_seeds");
        builder.Property(x => x.PermissionKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.PermissionKey).IsUnique();
    }
}
