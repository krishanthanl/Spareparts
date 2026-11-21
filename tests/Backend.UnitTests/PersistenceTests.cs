using Application.Common;
using Domain.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.UnitTests;

public sealed class PersistenceTests
{
    [Fact]
    public void Model_applies_money_UTC_audit_and_soft_delete_conventions()
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(typeof(Probe))!;
        Assert.Equal("numeric(18,2)", entity.FindProperty(nameof(Probe.Amount))!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(Probe.CreatedAt))!.GetColumnType());
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
        Assert.Contains("IsDeleted", db.Probes.ToQueryString());
        Assert.Contains("NOT", db.Probes.ToQueryString());
        var production = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=test").Options).Model.GetEntityTypes().Select(x => x.ClrType.Name).Order().ToArray();
        Assert.Equal(["Category", "CreditNote", "CreditNoteLine", "Customer", "DeliverySettings", "DeliveryZone", "DeliveryZonePostcode", "DocumentCounter", "Grn", "GrnLine", "Item", "ItemFitment", "ItemImage", "ItemPriceHistory", "Menu", "Payment", "PermissionSeed", "RefreshToken", "Refund", "RefundAllocation", "Role", "RolePermission", "SalesOrder", "SalesOrderLine", "SalesReturn", "SalesReturnLine", "SecurityEvent", "Shipment", "ShipmentLine", "StockDocument", "StockDocumentLine", "StockIssue", "StockIssueLine", "StockMovement", "Stocktake", "StocktakeLine", "User", "UserPermissionOverride", "UserRole", "VehicleMake", "VehicleModel"], production);
    }

    [Fact]
    public void Audit_sets_creation_update_and_soft_delete_values_without_a_database()
    {
        using var db = NewContext();
        var item = new Probe();
        db.Add(item);
        var clock = new FrozenClock();
        var interceptor = new AuditSaveChangesInterceptor(new TestActor(), clock);
        interceptor.ApplyAudit(db);
        Assert.Equal("test-user", item.CreatedBy);
        Assert.Equal(clock.GetUtcNow(), item.CreatedAt);
        Assert.Equal(item.CreatedAt, item.UpdatedAt);
        db.ChangeTracker.AcceptAllChanges();
        item.Amount = 7m;
        db.ChangeTracker.DetectChanges();
        interceptor.ApplyAudit(db);
        Assert.Equal("test-user", item.UpdatedBy);
        db.Remove(item);
        interceptor.ApplyAudit(db);
        Assert.Equal(EntityState.Modified, db.Entry(item).State);
        Assert.True(item.IsDeleted);
        Assert.Equal(clock.GetUtcNow(), item.DeletedAt);
        Assert.Equal("test-user", item.DeletedBy);
    }

    private static ProbeContext NewContext() => new(new DbContextOptionsBuilder<ProbeContext>().UseNpgsql("Host=localhost;Database=test").Options);
}

public sealed class Probe : AuditableEntity, ISoftDeletable
{
    public decimal Amount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : AppDbContext(options)
{
    public DbSet<Probe> Probes => Set<Probe>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Probe>().HasKey(x => x.Id);
        base.OnModelCreating(modelBuilder);
    }
}
public sealed class TestActor : ICurrentActor { public string ActorId => "test-user"; }
public sealed class FrozenClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-15T10:00:00Z");
}
