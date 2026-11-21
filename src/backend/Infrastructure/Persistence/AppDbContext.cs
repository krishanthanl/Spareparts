using System.Linq.Expressions;
using Domain.Catalog;
using Domain.Common;
using Domain.Identity;
using Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<ItemFitment> ItemFitments => Set<ItemFitment>();
    public DbSet<VehicleMake> VehicleMakes => Set<VehicleMake>();
    public DbSet<VehicleModel> VehicleModels => Set<VehicleModel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<PermissionSeed> PermissionSeeds => Set<PermissionSeed>();
    public DbSet<Grn> Grns => Set<Grn>();
    public DbSet<GrnLine> GrnLines => Set<GrnLine>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeLine> StocktakeLines => Set<StocktakeLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ItemPriceHistory> ItemPriceHistory => Set<ItemPriceHistory>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();
    public DbSet<Domain.Sales.Customer> Customers => Set<Domain.Sales.Customer>();
    public DbSet<Domain.Sales.SalesOrder> SalesOrders => Set<Domain.Sales.SalesOrder>();
    public DbSet<Domain.Sales.SalesOrderLine> SalesOrderLines => Set<Domain.Sales.SalesOrderLine>();
    public DbSet<Domain.Sales.StockIssue> StockIssues => Set<Domain.Sales.StockIssue>();
    public DbSet<Domain.Sales.StockIssueLine> StockIssueLines => Set<Domain.Sales.StockIssueLine>();
    public DbSet<Domain.Sales.Payment> Payments => Set<Domain.Sales.Payment>();
    public DbSet<Domain.Sales.DeliverySettings> DeliverySettings => Set<Domain.Sales.DeliverySettings>();
    public DbSet<Domain.Sales.DeliveryZone> DeliveryZones => Set<Domain.Sales.DeliveryZone>();
    public DbSet<Domain.Sales.DeliveryZonePostcode> DeliveryZonePostcodes => Set<Domain.Sales.DeliveryZonePostcode>();
    public DbSet<Domain.Sales.Shipment> Shipments => Set<Domain.Sales.Shipment>();
    public DbSet<Domain.Sales.ShipmentLine> ShipmentLines => Set<Domain.Sales.ShipmentLine>();
    public DbSet<Domain.Sales.SalesReturn> SalesReturns => Set<Domain.Sales.SalesReturn>();
    public DbSet<Domain.Sales.SalesReturnLine> SalesReturnLines => Set<Domain.Sales.SalesReturnLine>();
    public DbSet<Domain.Sales.CreditNote> CreditNotes => Set<Domain.Sales.CreditNote>();
    public DbSet<Domain.Sales.CreditNoteLine> CreditNoteLines => Set<Domain.Sales.CreditNoteLine>();
    public DbSet<Domain.Sales.Refund> Refunds => Set<Domain.Sales.Refund>();
    public DbSet<Domain.Sales.RefundAllocation> RefundAllocations => Set<Domain.Sales.RefundAllocation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AuditableEntity).IsAssignableFrom(type.ClrType))
            {
                modelBuilder.Entity(type.ClrType).Property(nameof(AuditableEntity.CreatedBy)).HasMaxLength(200).IsRequired();
                modelBuilder.Entity(type.ClrType).Property(nameof(AuditableEntity.UpdatedBy)).HasMaxLength(200).IsRequired();
            }
            if (type.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(type.ClrType))
            {
                var parameter = Expression.Parameter(type.ClrType, "entity");
                var filter = Expression.Lambda(Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))), parameter);
                modelBuilder.Entity(type.ClrType).HasQueryFilter(filter);
                modelBuilder.Entity(type.ClrType).Property(nameof(ISoftDeletable.DeletedBy)).HasMaxLength(200);
            }
        }
    }
}
