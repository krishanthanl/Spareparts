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
