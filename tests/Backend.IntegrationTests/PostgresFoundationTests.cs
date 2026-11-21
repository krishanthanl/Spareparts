using Application.Common;
using Domain.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS") != "1" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")))
            Skip = "Requires RUN_POSTGRES_TESTS=1 and Docker, or TEST_POSTGRES_CONNECTION pointing to an explicitly disposable PostgreSQL database.";
    }
}

public sealed class PostgresFoundationTests
{
    [PostgresFact]
    public async Task Real_PostgreSQL_preserves_audit_money_UTC_and_soft_delete()
    {
        PostgreSqlContainer? container = null;
        var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync();
            connection = container.GetConnectionString();
        }
        var schema = "foundation_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        try
        {
            await admin.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await command.ExecuteNonQueryAsync();
            var options = new DbContextOptionsBuilder<IntegrationContext>().UseNpgsql(connection)
                .AddInterceptors(new AuditSaveChangesInterceptor(new IntegrationActor(), TimeProvider.System)).Options;
            await using var db = new IntegrationContext(options, schema);
            // Create only this test's isolated schema objects, never an application schema.
            var script = db.Database.GenerateCreateScript();
            await db.Database.ExecuteSqlRawAsync(script);
            var item = new TestItem { Amount = 12.34m };
            db.Add(item);
            await db.SaveChangesAsync();
            Assert.Equal("integration", item.CreatedBy);
            Assert.Equal(TimeSpan.Zero, item.CreatedAt.Offset);
            db.ChangeTracker.Clear();
            var read = await db.Probes.SingleAsync();
            Assert.Equal(12.34m, read.Amount);
            db.Remove(read);
            await db.SaveChangesAsync();
            Assert.Empty(await db.Probes.ToListAsync());
            var deleted = await db.Probes.IgnoreQueryFilters().SingleAsync();
            Assert.True(deleted.IsDeleted);
            Assert.Equal("integration", deleted.DeletedBy);
            Assert.NotNull(deleted.DeletedAt);
        }
        finally
        {
            if (admin.State == System.Data.ConnectionState.Open)
            {
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin);
                await command.ExecuteNonQueryAsync();
            }
            if (container is not null) await container.DisposeAsync();
        }
    }
}

public sealed class IntegrationContext(DbContextOptions<IntegrationContext> options, string schema) : AppDbContext(options)
{
    public DbSet<TestItem> Probes => Set<TestItem>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Keep every table, including the application model, inside the throwaway schema.
        builder.HasDefaultSchema(schema);
        builder.Entity<TestItem>().ToTable("foundation_probe", schema).HasKey(x => x.Id);
        base.OnModelCreating(builder);
    }
}
public sealed class TestItem : AuditableEntity, ISoftDeletable
{
    public decimal Amount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
internal sealed class IntegrationActor : ICurrentActor { public string ActorId => "integration"; }
