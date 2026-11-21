using Domain.Catalog;
using Domain.Inventory;
using Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Company).HasMaxLength(200);
        builder.Property(x => x.BillingAddress).HasMaxLength(500);
        builder.Property(x => x.DeliveryAddress).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.Phone);
    }
}

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_orders", t =>
        {
            t.HasCheckConstraint("ck_sales_orders_number",
                "(\"Status\" <> 1 OR \"InvoiceNumber\" IS NULL) AND (\"Status\" NOT IN (2, 3) OR \"InvoiceNumber\" IS NOT NULL)");
            t.HasCheckConstraint("ck_sales_orders_no_gst", "\"TaxTreatment\" = 1 AND \"GstAmount\" = 0");
            t.HasCheckConstraint("ck_sales_orders_currency", "\"Currency\" = 'NZD'");
            t.HasCheckConstraint("ck_sales_orders_money",
                "\"Subtotal\" >= 0 AND \"LineDiscountTotal\" >= 0 AND \"OrderDiscount\" >= 0 AND \"DeliveryCharge\" >= 0 AND \"Total\" >= 0");
            t.HasCheckConstraint("ck_sales_orders_total",
                "\"Total\" = \"Subtotal\" - \"LineDiscountTotal\" - \"OrderDiscount\" + \"DeliveryCharge\"");
        });
        builder.Property(x => x.InvoiceNumber).HasMaxLength(20);
        builder.Property(x => x.CheckoutKeyHash).HasMaxLength(64);
        builder.Property(x => x.CheckoutRequestHash).HasMaxLength(64);
        builder.HasIndex(x => x.CheckoutKeyHash).IsUnique();
        builder.HasIndex(x => x.CheckoutHoldUntil).HasFilter("\"Status\" = 1 AND \"CheckoutHoldUntil\" IS NOT NULL");
        builder.Property(x => x.CustomerName).HasMaxLength(200);
        builder.Property(x => x.CustomerPhone).HasMaxLength(50);
        builder.Property(x => x.CustomerEmail).HasMaxLength(200);
        builder.Property(x => x.CustomerCompany).HasMaxLength(200);
        builder.Property(x => x.BillingAddress).HasMaxLength(500);
        builder.Property(x => x.DeliveryAddress).HasMaxLength(500);
        builder.Property(x => x.DiscountReason).HasMaxLength(500);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.ConfirmedBy).HasMaxLength(200);
        builder.Property(x => x.CancelledBy).HasMaxLength(200);
        builder.Property(x => x.CancelReason).HasMaxLength(500);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.InvoiceNumber).IsUnique().HasFilter("\"InvoiceNumber\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.ConfirmedAt });
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.UpdatedAt);
    }
}

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("sales_order_lines", t =>
        {
            t.HasCheckConstraint("ck_sales_order_lines_qty", "\"Quantity\" > 0");
            t.HasCheckConstraint("ck_sales_order_lines_money",
                "\"ListPrice\" >= 0 AND \"UnitPrice\" >= 0 AND \"LineDiscount\" >= 0 AND \"OrderDiscountShare\" >= 0 AND \"LineTotal\" >= 0");
        });
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.Property(x => x.DiscountReason).HasMaxLength(500);
        builder.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrderId, x.LineNo }).IsUnique();
        builder.HasIndex(x => x.ItemId);
    }
}

public sealed class StockIssueConfiguration : IEntityTypeConfiguration<StockIssue>
{
    public void Configure(EntityTypeBuilder<StockIssue> builder)
    {
        builder.ToTable("stock_issues");
        builder.Property(x => x.IssueNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PostedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.IssueNumber).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
    }
}

public sealed class StockIssueLineConfiguration : IEntityTypeConfiguration<StockIssueLine>
{
    public void Configure(EntityTypeBuilder<StockIssueLine> builder)
    {
        builder.ToTable("stock_issue_lines", t => t.HasCheckConstraint("ck_stock_issue_lines_qty", "\"Quantity\" > 0"));
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OrderLineId);
        builder.HasIndex(x => x.MovementId).IsUnique();
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            // A receipt is positive; a void is the exact negative of the receipt it cancels.
            t.HasCheckConstraint("ck_payments_amount", "(\"VoidsPaymentId\" IS NULL AND \"Amount\" > 0) OR (\"VoidsPaymentId\" IS NOT NULL AND \"Amount\" < 0)");
        });
        builder.Property(x => x.ReceiptNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.ReceivedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.VoidReason).HasMaxLength(500);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.VoidsPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.VoidsPaymentId).IsUnique().HasFilter("\"VoidsPaymentId\" IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.ReceivedAt });
        builder.HasIndex(x => x.ReceivedAt);
    }
}
