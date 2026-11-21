using Application.Common;
using Application.Inventory;
using Domain.Catalog;
using Domain.Inventory;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

/// <summary>
/// Stock ledger guarantees on a real, throwaway PostgreSQL database with the real migrations
/// (immutability, server time, no negative stock under concurrency, gapless numbers).
/// </summary>
public sealed class InventoryPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string adminConnection = "";
    private string connection = "";
    private string database = "";
    private ServiceProvider? provider;
    private Guid categoryId;

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
        database = "inventory_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ShopTime>();
        services.AddScoped<ICurrentActor, Actor>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddCatalogInfrastructure(new StorageSettings { LocalRoot = Path.Combine(Path.GetTempPath(), database) });
        provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var category = new Category { Name = "Filters", Slug = "filters" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        categoryId = category.Id;
    }

    public async Task DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (database.Length > 0)
        {
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        if (container is not null) await container.DisposeAsync();
    }

    private async Task<T> Run<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Guid> ItemAsync(string sku, int? reorderLevel = null) => Run(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var item = new Item { Sku = sku, Name = "Part " + sku, CategoryId = categoryId, ReorderLevel = reorderLevel };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    });

    private Task<GrnDto> ReceiveAsync(Guid item, decimal qty, decimal cost, decimal? price = 20m, bool post = true) => Run(async sp =>
    {
        var grns = sp.GetRequiredService<IGrnService>();
        var grn = await grns.CreateAsync(new GrnInput("Repco", "INV-1", null, null, [new GrnLineInput(item, qty, cost, price)], null), default);
        return post ? await grns.PostAsync(grn.Id, default) : grn;
    });

    private Task<StockDocumentDto> AdjustAsync(Guid item, decimal qty) => Run(async sp =>
    {
        var docs = sp.GetRequiredService<IStockDocumentService>();
        var d = await docs.CreateAsync(StockDocumentKind.Adjustment, new StockDocumentInput(AdjustmentReason.Damage, null, [new StockDocumentLineInput(item, qty, null, null)], null), default);
        return await docs.PostAsync(StockDocumentKind.Adjustment, d.Id, default);
    });

    private Task<decimal> OnHandAsync(Guid item, DateTimeOffset? asOf = null) => Run(async sp =>
    {
        var report = await sp.GetRequiredService<IStockQueryService>().StockAsync(new StockRequest(PageSize: 200, AsOf: asOf), default);
        return report.Items.Items.Single(r => r.ItemId == item).OnHand;
    });

    [PostgresFact]
    public async Task Stock_at_an_instant_sums_only_earlier_movements_and_is_zero_before_the_first()
    {
        var item = await ItemAsync("TP-100");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var first = await ReceiveAsync(item, 10, 5m);
        await Task.Delay(50);
        var between = DateTimeOffset.UtcNow;
        await Task.Delay(50);
        await AdjustAsync(item, -3);
        Assert.Equal(0, await OnHandAsync(item, before));
        Assert.Equal(10, await OnHandAsync(item, between));
        Assert.Equal(7, await OnHandAsync(item));
        Assert.True(first.PostedAt > before);
    }

    [PostgresFact]
    public async Task Ledger_rows_cannot_be_changed_and_client_times_are_ignored()
    {
        var item = await ItemAsync("TP-101");
        await ReceiveAsync(item, 2, 5m);
        await Run(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var update = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("UPDATE stock_movements SET \"QtyChange\" = 99"));
            Assert.Contains("cannot be changed", update!.Message);
            var delete = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("DELETE FROM item_price_history"));
            Assert.Contains("cannot be changed", delete!.Message);
            // A backdated insert is re-stamped with the database's current time.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO stock_movements ("Id","ItemId","MovementType","QtyChange","OccurredAt","UnitCost","AverageCostAfter","RefType","RefId","PostingKey","CreatedAt","CreatedBy","UpdatedAt","UpdatedBy")
                VALUES (gen_random_uuid(), {0}, 4, 1, '2020-01-01', 5, 5, 'Test', gen_random_uuid(), 'test-backdate', now(), 't', now(), 't')
                """, item);
            var stamped = await db.StockMovements.Where(m => m.PostingKey == "test-backdate").Select(m => m.OccurredAt).SingleAsync();
            Assert.True(stamped > DateTimeOffset.UtcNow.AddMinutes(-5));
            return 0;
        });
    }

    [PostgresFact]
    public async Task Database_rejects_negative_stock_even_without_the_application()
    {
        var item = await ItemAsync("TP-102");
        await ReceiveAsync(item, 1, 5m);
        await Run(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var error = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("""
                INSERT INTO stock_movements ("Id","ItemId","MovementType","QtyChange","UnitCost","AverageCostAfter","RefType","RefId","PostingKey","CreatedAt","CreatedBy","UpdatedAt","UpdatedBy")
                VALUES (gen_random_uuid(), {0}, 4, -5, 5, 5, 'Test', gen_random_uuid(), 'test-negative', now(), 't', now(), 't')
                """, item));
            Assert.Contains("negative", error!.Message);
            return 0;
        });
    }

    [PostgresFact]
    public async Task A_failing_line_rolls_back_the_whole_posting()
    {
        var priced = await ItemAsync("TP-103");
        var unpriced = await ItemAsync("TP-104");
        var grn = await Run(sp => sp.GetRequiredService<IGrnService>().CreateAsync(new GrnInput("Repco", null, null, null,
            [new GrnLineInput(priced, 5, 5m, 20m), new GrnLineInput(unpriced, 5, 5m, null)], null), default));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => Run(sp => sp.GetRequiredService<IGrnService>().PostAsync(grn.Id, default)));
        Assert.Equal(0, await OnHandAsync(priced));
        var after = await Run(sp => sp.GetRequiredService<IGrnService>().GetAsync(grn.Id, default));
        Assert.Equal(DocumentStatus.Draft, after.Status);
        Assert.Null(after.GrnNumber);
    }

    [PostgresFact]
    public async Task Posting_the_same_GRN_twice_at_once_posts_it_once()
    {
        var item = await ItemAsync("TP-105");
        var grn = await ReceiveAsync(item, 4, 5m, post: false);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Run(sp => sp.GetRequiredService<IGrnService>().PostAsync(grn.Id, default)))));
        Assert.Single(results.Select(r => r.GrnNumber).Distinct());
        Assert.Equal(4, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Concurrent_deductions_never_take_stock_below_zero()
    {
        var item = await ItemAsync("TP-106");
        await ReceiveAsync(item, 5, 5m);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AdjustAsync(item, -2); return true; }
            catch (ConflictException) { return false; }
        })));
        Assert.Equal(2, attempts.Count(x => x));
        Assert.Equal(1, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Reversal_is_rejected_once_the_stock_has_been_used()
    {
        var item = await ItemAsync("TP-107");
        var grn = await ReceiveAsync(item, 3, 5m);
        await AdjustAsync(item, -2);
        await Assert.ThrowsAsync<ConflictException>(() => Run(sp => sp.GetRequiredService<IGrnService>().ReverseAsync(grn.Id, new ReverseInput("wrong supplier"), default)));
        Assert.Equal(1, await OnHandAsync(item));

        var other = await ItemAsync("TP-108");
        var ok = await ReceiveAsync(other, 3, 5m);
        var reversed = await Run(sp => sp.GetRequiredService<IGrnService>().ReverseAsync(ok.Id, new ReverseInput("duplicate"), default));
        Assert.Equal(DocumentStatus.Reversed, reversed.Status);
        Assert.NotNull(reversed.ReversedAt);
        Assert.Equal(0, await OnHandAsync(other));
    }

    [PostgresFact]
    public async Task Weighted_average_and_price_history_follow_receipts()
    {
        var item = await ItemAsync("TP-109");
        await ReceiveAsync(item, 10, 4m, 20m);
        await ReceiveAsync(item, 10, 6m, 25m);
        await ReceiveAsync(item, 1.5m, 6m, null); // fractional, keeps the price
        var card = await Run(sp => sp.GetRequiredService<IStockQueryService>().StockCardAsync(item, null, null, default));
        Assert.Equal(21.5m, card.OnHand);
        // (10*4 + 10*6) / 20 = 5.00, then (20*5.00 + 1.5*6) / 21.5 = 5.0698 -> 5.07 (rounded to cents, D3)
        Assert.Equal(5.07m, card.AverageCost);
        Assert.Equal(25m, card.SellingPrice);
        var history = await Run(sp => sp.GetRequiredService<IPriceService>().HistoryAsync(item, default));
        Assert.Equal([25m, 20m], history.Select(h => h.SellingPrice));
    }

    [PostgresFact]
    public async Task Receipt_keeps_the_price_in_effect_when_posted_after_later_price_changes()
    {
        var item = await ItemAsync("TP-SNAPSHOT");
        await ReceiveAsync(item, 2, 5m, 20m);
        var receipt = await ReceiveAsync(item, 3, 6m, null);
        Assert.Equal(20m, Assert.Single(receipt.Lines).SellingPrice);

        await Run(sp => sp.GetRequiredService<IPriceService>().ChangeAsync(item, new PriceChangeInput(30m, "New retail price"), default));
        var saved = await Run(sp => sp.GetRequiredService<IGrnService>().GetAsync(receipt.Id, default));
        Assert.Equal(20m, Assert.Single(saved.Lines).SellingPrice);
        Assert.Equal(30m, Assert.Single(saved.Lines).CurrentPrice);
        Assert.Equal(5m, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Posted_receipt_lines_cannot_be_moved_to_a_draft()
    {
        var item = await ItemAsync("TP-LINE-LOCK");
        var posted = await ReceiveAsync(item, 2, 5m);
        var draft = await ReceiveAsync(item, 1, 6m, post: false);
        await Run(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var error = await Record.ExceptionAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE grn_lines SET \"GrnId\" = {draft.Id}, \"LineNo\" = 2 WHERE \"Id\" = {posted.Lines[0].Id}"));
            Assert.NotNull(error);
            Assert.Contains("cannot change", error.Message);
            return 0;
        });
    }

    [PostgresFact]
    public async Task Stock_card_does_not_silently_drop_movements_after_two_thousand()
    {
        var item = await ItemAsync("TP-LONG-CARD");
        await Run(async sp =>
        {
            await sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stock_movements ("Id","ItemId","MovementType","QtyChange","UnitCost","AverageCostAfter","RefType","RefId","PostingKey","CreatedAt","CreatedBy","UpdatedAt","UpdatedBy")
                SELECT gen_random_uuid(), {item}, 4, 1, 5, 5, 'Test', gen_random_uuid(), 'long-card:' || n, now(), 't', now(), 't'
                FROM generate_series(1, 2001) n
                """);
            return 0;
        });
        var card = await Run(sp => sp.GetRequiredService<IStockQueryService>().StockCardAsync(item, null, null, default));
        Assert.Equal(2001, card.Movements.Count);
        Assert.Equal(card.OnHand, card.Movements[^1].Balance);
    }

    [PostgresFact]
    public async Task Opening_stock_is_allowed_once_per_item()
    {
        var item = await ItemAsync("TP-110");
        var docs = () => provider!.CreateAsyncScope().ServiceProvider.GetRequiredService<IStockDocumentService>();
        StockDocumentInput Input() => new(AdjustmentReason.Opening, null, [new StockDocumentLineInput(item, 3, 4m, 9.5m)], null);
        var first = await docs().CreateAsync(StockDocumentKind.Opening, Input(), default);
        await docs().PostAsync(StockDocumentKind.Opening, first.Id, default);
        var second = await docs().CreateAsync(StockDocumentKind.Opening, Input(), default);
        await Assert.ThrowsAsync<ConflictException>(() => docs().PostAsync(StockDocumentKind.Opening, second.Id, default));
        Assert.Equal(3, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Stocktake_posts_only_the_differences()
    {
        var a = await ItemAsync("TP-111");
        var b = await ItemAsync("TP-112");
        await ReceiveAsync(a, 5, 5m);
        await ReceiveAsync(b, 2, 5m);
        var take = await Run(sp => sp.GetRequiredService<IStocktakeService>().CreateAsync(new StocktakeInput(null, [new(a, 4), new(b, 2)], null), default));
        var posted = await Run(sp => sp.GetRequiredService<IStocktakeService>().PostAsync(take.Id, default));
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.NotNull(posted.AdjustmentId);
        Assert.Equal(4, await OnHandAsync(a));
        Assert.Equal(2, await OnHandAsync(b));
        var adjustment = await Run(sp => sp.GetRequiredService<IStockDocumentService>().GetAsync(StockDocumentKind.Adjustment, posted.AdjustmentId!.Value, default));
        Assert.Equal(AdjustmentReason.Count, adjustment.Reason);
        Assert.Equal(-1, Assert.Single(adjustment.Lines).QtyChange);
    }

    [PostgresFact]
    public async Task Document_numbers_have_no_gaps_under_concurrency_and_posted_lines_are_locked()
    {
        var item = await ItemAsync("TP-113");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ReceiveAsync(item, 1, 5m))));
        var numbers = await Run(sp => sp.GetRequiredService<AppDbContext>().Grns.Where(g => g.GrnNumber != null).Select(g => g.GrnNumber!).ToListAsync());
        var values = numbers.Select(n => int.Parse(n[4..])).Order().ToList();
        Assert.Equal(Enumerable.Range(values[0], values.Count), values);
        await Run(async sp =>
        {
            var error = await Record.ExceptionAsync(() => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE grn_lines SET \"Quantity\" = 50"));
            Assert.Contains("cannot change", error!.Message);
            return 0;
        });
    }

    [PostgresFact]
    public async Task Low_stock_includes_items_at_the_reorder_level()
    {
        var at = await ItemAsync("TP-114", reorderLevel: 2);
        var above = await ItemAsync("TP-115", reorderLevel: 2);
        var none = await ItemAsync("TP-116");
        await ReceiveAsync(at, 2, 5m);
        await ReceiveAsync(above, 3, 5m);
        var low = await Run(sp => sp.GetRequiredService<IStockQueryService>().LowStockAsync(default));
        Assert.Contains(low, r => r.ItemId == at);
        Assert.DoesNotContain(low, r => r.ItemId == above || r.ItemId == none);
    }

    private sealed class Actor : ICurrentActor { public string ActorId => "integration"; }
}
