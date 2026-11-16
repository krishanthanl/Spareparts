using Application.Catalog;
using Application.Common;
using Infrastructure.Catalog;
using Infrastructure.Imaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

/// <summary>
/// Applies the real migrations to a brand-new throwaway database, then exercises catalogue rules
/// that only PostgreSQL can enforce (partial unique indexes, check constraints, filters).
/// </summary>
public sealed class CatalogPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string adminConnection = "";
    private string connection = "";
    private string database = "";
    private string mediaRoot = "";

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS") != "1" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION"))) return;
        adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync();
            adminConnection = container.GetConnectionString();
        }
        database = "catalog_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Pooling = false }.ConnectionString;
        mediaRoot = Path.Combine(Path.GetTempPath(), database);
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (database.Length > 0)
        {
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        if (Directory.Exists(mediaRoot)) Directory.Delete(mediaRoot, recursive: true);
        if (container is not null) await container.DisposeAsync();
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
        .AddInterceptors(new AuditSaveChangesInterceptor(new IntegrationActor(), TimeProvider.System)).Options);

    private (CategoryService Categories, ItemService Items, VehicleService Vehicles, PublicCatalogService Public, AppDbContext Db) Services()
    {
        var db = NewDb();
        var storage = new LocalDiskFileStorage(new StorageSettings { LocalRoot = mediaRoot });
        return (new CategoryService(db, storage, new SkiaImageProcessor()), new ItemService(db, storage, new SkiaImageProcessor()),
            new VehicleService(db), new PublicCatalogService(db, storage), db);
    }

    private static ItemInput Item(Guid category, string sku = "tp-001", params FitmentInput[] fitments) =>
        new(sku, "Headlight", null, category, "Depo", null, null, true, fitments, null);

    [PostgresFact]
    public async Task Sku_can_be_reused_after_delete_but_restore_then_conflicts()
    {
        var s = Services();
        var category = await s.Categories.CreateAsync(new CategoryInput("Headlights", null, 0, true, null), default);
        var first = await s.Items.CreateAsync(Item(category.Id), default);
        Assert.Equal("TP-001", first.Sku);
        await Assert.ThrowsAsync<ConflictException>(() => s.Items.CreateAsync(Item(category.Id, " TP-001 "), default));
        await s.Items.DeleteAsync(first.Id, default);
        var reused = await s.Items.CreateAsync(Item(category.Id), default);
        await Assert.ThrowsAsync<ConflictException>(() => s.Items.RestoreAsync(first.Id, default));
        await s.Items.DeleteAsync(reused.Id, default);
        Assert.False((await s.Items.RestoreAsync(first.Id, default)).IsDeleted);
    }

    [PostgresFact]
    public async Task Soft_deleted_records_are_hidden_unless_asked_for()
    {
        var s = Services();
        var category = await s.Categories.CreateAsync(new CategoryInput("Brakes", null, 0, true, null), default);
        var item = await s.Items.CreateAsync(Item(category.Id), default);
        await s.Items.DeleteAsync(item.Id, default);
        Assert.Equal(0, (await s.Items.ListAsync(new PageRequest(), new ItemFilter(), default)).TotalCount);
        var all = await s.Items.ListAsync(new PageRequest(IncludeDeleted: true), new ItemFilter(), default);
        Assert.True(Assert.Single(all.Items).IsDeleted);
        Assert.Equal(0, (await s.Public.ItemsAsync(null, null, 1, 24, default)).TotalCount);
    }

    [PostgresFact]
    public async Task Category_with_items_cannot_be_deleted_and_names_are_unique_ignoring_case()
    {
        var s = Services();
        var category = await s.Categories.CreateAsync(new CategoryInput("Tail Lights", null, 0, true, null), default);
        await s.Items.CreateAsync(Item(category.Id), default);
        await Assert.ThrowsAsync<ConflictException>(() => s.Categories.DeleteAsync(category.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => s.Categories.CreateAsync(new CategoryInput("tail lights", null, 0, true, null), default));
    }

    [PostgresFact]
    public async Task Fitment_filters_respect_inclusive_and_open_year_boundaries()
    {
        var s = Services();
        var category = await s.Categories.CreateAsync(new CategoryInput("Headlights", null, 0, true, null), default);
        var toyota = await s.Vehicles.CreateMakeAsync(new NameInput("Toyota"), default);
        var prius = await s.Vehicles.CreateModelAsync(toyota.Id, new NameInput("Prius"), default);
        var aqua = await s.Vehicles.CreateModelAsync(toyota.Id, new NameInput("Aqua"), default);
        await s.Items.CreateAsync(Item(category.Id, "A", new FitmentInput(prius.Id, 2016, 2022)), default);
        await s.Items.CreateAsync(Item(category.Id, "B", new FitmentInput(prius.Id, 2020, null)), default);
        await s.Items.CreateAsync(Item(category.Id, "C", new FitmentInput(aqua.Id, null, 2015)), default);
        async Task<string[]> Skus(ItemFilter filter) =>
            (await s.Items.ListAsync(new PageRequest(Sort: "sku"), filter, default)).Items.Select(x => x.Sku).ToArray();
        Assert.Equal(["A"], await Skus(new ItemFilter(ModelId: prius.Id, Year: 2016)));
        Assert.Equal(["A", "B"], await Skus(new ItemFilter(ModelId: prius.Id, Year: 2022)));
        Assert.Equal(["B"], await Skus(new ItemFilter(ModelId: prius.Id, Year: 2023)));
        Assert.Equal(["C"], await Skus(new ItemFilter(MakeId: toyota.Id, Year: 2010)));
        await Assert.ThrowsAsync<ConflictException>(() => s.Vehicles.CreateMakeAsync(new NameInput("TOYOTA"), default));
    }

    [PostgresFact]
    public async Task Database_constraints_back_up_validation()
    {
        await using var db = NewDb();
        var category = new Domain.Catalog.Category { Name = "X", Slug = "x" };
        db.Add(category);
        await db.SaveChangesAsync();
        db.Add(new Domain.Catalog.Item { Sku = "lower-case", Name = "n", CategoryId = category.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Public_catalogue_hides_inactive_categories()
    {
        var s = Services();
        var shown = await s.Categories.CreateAsync(new CategoryInput("Headlights", null, 0, true, null), default);
        var hidden = await s.Categories.CreateAsync(new CategoryInput("Showcase", null, 1, false, null), default);
        await s.Items.CreateAsync(Item(shown.Id, "A"), default);
        await s.Items.CreateAsync(Item(hidden.Id, "B"), default);
        Assert.Equal(["Headlights"], (await s.Public.CategoriesAsync(default)).Select(x => x.Name));
        Assert.Equal(["A"], (await s.Public.ItemsAsync(null, null, 1, 24, default)).Items.Select(x => x.Sku));
    }
}
