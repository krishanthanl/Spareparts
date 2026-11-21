using Domain.Inventory;
using Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class DeliverySettingsConfiguration : IEntityTypeConfiguration<DeliverySettings>
{
    public void Configure(EntityTypeBuilder<DeliverySettings> builder)
    {
        builder.ToTable("delivery_settings", t => t.HasCheckConstraint("ck_delivery_settings_money", "\"FreeDeliveryThreshold\" IS NULL OR \"FreeDeliveryThreshold\" >= 0"));
        builder.Property(x => x.Version).IsRowVersion();
    }
}

public sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("delivery_zones", t => t.HasCheckConstraint("ck_delivery_zones_money",
            "\"Charge\" >= 0 AND \"RuralSurcharge\" >= 0 AND (\"FreeDeliveryThreshold\" IS NULL OR \"FreeDeliveryThreshold\" >= 0)"));
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ServicePromise).HasMaxLength(200);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasMany(x => x.Postcodes).WithOne().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class DeliveryZonePostcodeConfiguration : IEntityTypeConfiguration<DeliveryZonePostcode>
{
    public void Configure(EntityTypeBuilder<DeliveryZonePostcode> builder)
    {
        builder.ToTable("delivery_zone_postcodes", t => t.HasCheckConstraint("ck_delivery_zone_postcodes_range",
            "\"From\" BETWEEN 0 AND 9999 AND \"To\" BETWEEN \"From\" AND 9999"));
        builder.HasIndex(x => new { x.ZoneId, x.From });
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");
        builder.Property(x => x.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Carrier).HasMaxLength(100);
        builder.Property(x => x.TrackingReference).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.DispatchedBy).HasMaxLength(200);
        builder.Property(x => x.DeliveredBy).HasMaxLength(200);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockIssue>().WithMany().HasForeignKey(x => x.StockIssueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ShipmentNumber).IsUnique();
        builder.HasIndex(x => x.StockIssueId).IsUnique().HasFilter("\"StockIssueId\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.UpdatedAt });
        builder.HasIndex(x => x.OrderId);
    }
}

public sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.ToTable("shipment_lines", t => t.HasCheckConstraint("ck_shipment_lines_qty", "\"Quantity\" > 0"));
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ShipmentId, x.OrderLineId }).IsUnique();
    }
}

public sealed class StockIssueShipmentConfiguration : IEntityTypeConfiguration<StockIssue>
{
    // Extends StockIssueConfiguration: EF merges configurations of the same entity type.
    public void Configure(EntityTypeBuilder<StockIssue> builder) =>
        builder.HasIndex(x => x.ShipmentId).IsUnique().HasFilter("\"ShipmentId\" IS NOT NULL");
}

public sealed class SalesOrderAfterSaleConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.Property(x => x.DeliveryPostcode).HasMaxLength(4);
        builder.Property(x => x.DeliveryZoneName).HasMaxLength(100);
        builder.Property(x => x.DeliveryPromise).HasMaxLength(200);
        builder.Property(x => x.DeliveryChargeReason).HasMaxLength(500);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_sales_orders_credit", "\"CreditTotal\" >= 0 AND \"CreditTotal\" <= \"Total\"");
            t.HasCheckConstraint("ck_sales_orders_refunded", "\"RefundedTotal\" >= 0");
        });
    }
}

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("sales_returns");
        builder.Property(x => x.ReturnNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PostedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReturnNumber).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.PostedAt);
    }
}

public sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.ToTable("sales_return_lines", t =>
        {
            t.HasCheckConstraint("ck_sales_return_lines_qty", "\"Quantity\" > 0 AND \"UnitCost\" >= 0");
            // Restocked lines post exactly one movement; written-off lines post none.
            t.HasCheckConstraint("ck_sales_return_lines_movement", "(\"Disposition\" = 1) = (\"MovementId\" IS NOT NULL)");
        });
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OrderLineId);
        builder.HasIndex(x => x.MovementId).IsUnique().HasFilter("\"MovementId\" IS NOT NULL");
    }
}

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", t =>
        {
            t.HasCheckConstraint("ck_credit_notes_total", "\"Total\" > 0");
            t.HasCheckConstraint("ck_credit_notes_no_gst", "\"TaxTreatment\" = 1 AND \"GstAmount\" = 0");
            t.HasCheckConstraint("ck_credit_notes_currency", "\"Currency\" = 'NZD'");
        });
        builder.Property(x => x.CreditNoteNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.IssuedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesReturn>().WithMany().HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CreditNoteNumber).IsUnique();
        builder.HasIndex(x => x.SalesReturnId).IsUnique().HasFilter("\"SalesReturnId\" IS NOT NULL");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.IssuedAt);
    }
}

public sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.ToTable("credit_note_lines", t => t.HasCheckConstraint("ck_credit_note_lines_amount", "\"Amount\" > 0 AND \"Quantity\" >= 0"));
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OrderLineId);
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", t =>
        {
            t.HasCheckConstraint("ck_refunds_amount", "\"Amount\" > 0");
            t.HasCheckConstraint("ck_refunds_not_own", "\"ApprovedBy\" IS NULL OR \"ApprovedBy\" <> \"RequestedBy\"");
            t.HasCheckConstraint("ck_refunds_evidence", "\"Status\" <> 4 OR (\"EvidenceReference\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");
            t.HasCheckConstraint("ck_refunds_no_gst", "\"TaxTreatment\" = 1 AND \"Currency\" = 'NZD'");
        });
        builder.Property(x => x.RefundNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ApprovedBy).HasMaxLength(200);
        builder.Property(x => x.CompletedBy).HasMaxLength(200);
        builder.Property(x => x.EvidenceReference).HasMaxLength(200);
        builder.Property(x => x.FailureReason).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CompletionKey).HasMaxLength(100);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreditNote>().WithMany().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RefundNumber).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.CompletionKey).IsUnique().HasFilter("\"CompletionKey\" IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.Status });
        builder.HasIndex(x => new { x.Status, x.CompletedAt });
    }
}

public sealed class RefundAllocationConfiguration : IEntityTypeConfiguration<RefundAllocation>
{
    public void Configure(EntityTypeBuilder<RefundAllocation> builder)
    {
        builder.ToTable("refund_allocations", t => t.HasCheckConstraint("ck_refund_allocations_amount", "\"Amount\" > 0"));
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RefundId, x.PaymentId }).IsUnique();
        builder.HasIndex(x => x.PaymentId);
    }
}
