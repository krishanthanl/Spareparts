using Application.Common;
using Application.Inventory;
using Application.Sales;
using Domain.Catalog;
using Domain.Sales;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

/// <summary>
/// Phase 4b after-sale guarantees on a real, throwaway PostgreSQL database (ADR 0006): delivery quotes,
/// dispatch exactly once, return limits under concurrency, exact credit rounding and refund limits.
/// </summary>
public sealed class AfterSalePostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string adminConnection = "";
    private string database = "";
    private ServiceProvider? provider;
    private Guid categoryId;
    private readonly SwitchableActor actor = new();

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
        database = "aftersale_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ShopTime>();
        services.AddSingleton<ICurrentActor>(actor);
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddCatalogInfrastructure(new StorageSettings { LocalRoot = Path.Combine(Path.GetTempPath(), database) });
        provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var category = new Category { Name = "Brakes", Slug = "brakes" };
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

    private Task<T> Orders<T>(Func<ISalesOrderService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<ISalesOrderService>()));
    private Task<T> After<T>(Func<IAfterSaleService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IAfterSaleService>()));
    private Task<T> Delivery<T>(Func<IDeliveryService, Task<T>> action) => Run(sp => action(sp.GetRequiredService<IDeliveryService>()));

    private Task<Guid> StockedItemAsync(string sku, decimal qty, decimal price = 25m, decimal cost = 10m) => Run(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var item = new Item { Sku = sku, Name = "Brake pad " + sku, CategoryId = categoryId };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        var grns = sp.GetRequiredService<IGrnService>();
        var grn = await grns.CreateAsync(new GrnInput("Repco", null, null, null, [new GrnLineInput(item.Id, qty, cost, price)], null), default);
        await grns.PostAsync(grn.Id, default);
        return item.Id;
    });

    private static OrderInput Input(params OrderLineInput[] lines) =>
        new(SalesSource.Telephone, null, "Sam Driver", "021 555 000", null, null, "1 Queen St, Auckland", PaymentTerms.Immediate,
            DeliveryMethod.Pickup, null, 0, 0, null, null, lines);

    private static OrderInput Delivered(decimal charge, string postcode, params OrderLineInput[] lines) =>
        Input(lines) with { DeliveryMethod = DeliveryMethod.Delivery, DeliveryAddress = "5 Main Rd, Hamilton", DeliveryCharge = charge, DeliveryPostcode = postcode };

    private Task<OrderDto> ConfirmedAsync(OrderInput input, bool canDiscount = false) => Orders(async s =>
    {
        var draft = await s.CreateAsync(input, canDiscount, default);
        return await s.ConfirmAsync(draft.Id, default);
    });

    /// <summary>A confirmed, issued and fully paid pickup sale.</summary>
    private async Task<OrderDto> SoldAsync(OrderInput input, bool canDiscount = false)
    {
        var order = await ConfirmedAsync(input, canDiscount);
        await Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default));
        return await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Card, order.Total, null, "pay-" + order.Id), default));
    }

    private Task<decimal> OnHandAsync(Guid item) => Run(async sp =>
        await sp.GetRequiredService<AppDbContext>().StockMovements.Where(m => m.ItemId == item).SumAsync(m => m.QtyChange));

    private static string Key() => Guid.NewGuid().ToString("N");

    // ---------------- Delivery ----------------

    [PostgresFact]
    public async Task Delivery_quotes_by_zone_with_free_threshold_rural_surcharge_and_unsupported_postcodes()
    {
        await Delivery(d => d.UpdateSettingsAsync(new DeliverySettingsInput(true, true, 200m), default));
        var zone = await Delivery(d => d.CreateZoneAsync(new DeliveryZoneInput("Waikato", 12.50m, null, 8m, "1-2 working days", true,
            [new PostcodeRangeInput("3200", "3299"), new PostcodeRangeInput("3400")]), default));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => Delivery(d => d.CreateZoneAsync(
            new DeliveryZoneInput("Overlap", 5m, null, 0, null, true, [new PostcodeRangeInput("3250", "3260")]), default)));

        var standard = await Delivery(d => d.QuoteAsync(new DeliveryQuoteInput("3216", false, 50m), default));
        Assert.True(standard.Delivers);
        Assert.Equal(12.50m, standard.Charge);
        Assert.Equal(20.50m, (await Delivery(d => d.QuoteAsync(new DeliveryQuoteInput("3400", true, 50m), default))).Charge);
        var free = await Delivery(d => d.QuoteAsync(new DeliveryQuoteInput("3216", true, 200m), default));
        Assert.True(free.Free);
        Assert.Equal(8m, free.Charge); // free covers the standard run; rural still applies
        Assert.False((await Delivery(d => d.QuoteAsync(new DeliveryQuoteInput("9010", false, 50m), default))).Delivers);

        var item = await StockedItemAsync("DL-1", 5, price: 30m);
        // The quote must be used, or a reason given; delivering outside every zone needs sales.discount.
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => Orders(s => s.CreateAsync(Delivered(5m, "3216", new OrderLineInput(item, 1)), true, default)));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => Orders(s => s.CreateAsync(Delivered(12.50m, null!, new OrderLineInput(item, 1)), true, default)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Orders(s => s.CreateAsync(Delivered(30m, "9010", new OrderLineInput(item, 1)) with { DeliveryChargeReason = "Special run" }, false, default)));
        var order = await ConfirmedAsync(Delivered(12.50m, "3216", new OrderLineInput(item, 1)));
        Assert.Equal("Waikato", order.DeliveryZoneName);
        Assert.Equal(42.50m, order.Total);
        Assert.Equal(0, order.GstAmount);

        // Changing the zone later never touches the confirmed invoice.
        await Delivery(d => d.UpdateZoneAsync(zone.Id, new DeliveryZoneInput("Waikato", 99m, null, 8m, null, true, [new PostcodeRangeInput("3200", "3299")], zone.Version), default));
        var reloaded = await Orders(s => s.GetAsync(order.Id, default));
        Assert.Equal(12.50m, reloaded.DeliveryCharge);
        Assert.Equal("1-2 working days", reloaded.DeliveryPromise);
    }

    // ---------------- Shipments ----------------

    [PostgresFact]
    public async Task Dispatch_issues_stock_exactly_once_and_delivery_does_not_move_stock()
    {
        var item = await StockedItemAsync("SH-1", 5);
        var order = await ConfirmedAsync(Input(new OrderLineInput(item, 3)) with { DeliveryMethod = DeliveryMethod.Delivery, DeliveryAddress = "2 Bridge St", DeliveryCharge = 10m });
        var first = await After(a => a.CreateShipmentAsync(order.Id, new ShipmentInput("NZ Post", null, null, [new IssueLineInput(order.Lines[0].Id, 2)]), default));
        // Pending shipments hold their quantity: only 1 more can be put on another shipment.
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CreateShipmentAsync(order.Id, new ShipmentInput(null, null, null, [new IssueLineInput(order.Lines[0].Id, 2)]), default)));
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.IssueAsync(order.Id, new IssueInput(null, Key()), default)));
        Assert.Equal(5, await OnHandAsync(item));

        var key = Key();
        var dispatched = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => After(a => a.DispatchAsync(first.Id, new DispatchInput(null, "TRK1", key), default)))));
        Assert.All(dispatched, d => Assert.Equal(ShipmentStatus.Dispatched, d.Status));
        Assert.Equal(3, await OnHandAsync(item));
        var delivered = await After(a => a.DeliverAsync(first.Id, default));
        Assert.Equal(ShipmentStatus.Delivered, delivered.Status);
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Equal(3, await OnHandAsync(item));
        Assert.Equal(FulfillmentStatus.PartiallyIssued, (await Orders(s => s.GetAsync(order.Id, default))).FulfillmentStatus);
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CancelShipmentAsync(first.Id, default)));
    }

    // ---------------- Returns and credit notes ----------------

    [PostgresFact]
    public async Task Returns_restock_or_write_off_never_exceed_issued_and_credit_the_exact_line_total()
    {
        var item = await StockedItemAsync("RT-1", 10, price: 10m, cost: 4m);
        // 3 x $10 with a $1 order discount: line total $29.00; three single returns must credit exactly 29.00.
        var sale = await SoldAsync(Input(new OrderLineInput(item, 3)) with { OrderDiscount = 1m, DiscountReason = "Loyal" }, canDiscount: true);
        var line = sale.Lines[0].Id;
        Assert.Equal(7, await OnHandAsync(item));

        var r1 = await After(a => a.CreateReturnAsync(sale.Id, new SalesReturnInput("Wrong part", [new ReturnLineInput(line, 1, ReturnDisposition.Restock)], "r1"), default));
        Assert.Equal(8, await OnHandAsync(item));
        Assert.Equal(9.67m, r1.CreditTotal);
        Assert.Equal(4m, r1.Lines[0].UnitCost);
        var again = await After(a => a.CreateReturnAsync(sale.Id, new SalesReturnInput("Wrong part", [new ReturnLineInput(line, 1, ReturnDisposition.Restock)], "r1"), default));
        Assert.Equal(r1.Id, again.Id); // idempotent retry
        var r2 = await After(a => a.CreateReturnAsync(sale.Id, new SalesReturnInput("Damaged", [new ReturnLineInput(line, 1, ReturnDisposition.WriteOff)], "r2"), default));
        Assert.Equal(8, await OnHandAsync(item)); // write-off does not restock
        Assert.Equal(9.66m, r2.CreditTotal);

        // Two racing returns for the last unit: exactly one wins.
        var results = await Task.WhenAll(new[] { "r3", "r4" }.Select(k => Task.Run(async () =>
        {
            try { await After(a => a.CreateReturnAsync(sale.Id, new SalesReturnInput("Race", [new ReturnLineInput(line, 1, ReturnDisposition.Restock)], k), default)); return true; }
            catch (ConflictException) { return false; }
        })));
        Assert.Single(results, r => r);
        var view = await After(a => a.GetForOrderAsync(sale.Id, default));
        Assert.Equal(29m, view.CreditNotes.Sum(c => c.Total));
        Assert.Equal(0, view.Returnable[0].Returnable);
        Assert.Equal(29m, view.Balance.Credits);
        Assert.Equal(29m, view.Balance.OwedToCustomer);
        Assert.Equal(9, await OnHandAsync(item));
        Assert.All(view.CreditNotes, c => Assert.Equal(0, c.GstAmount));
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CreateReturnAsync(sale.Id, new SalesReturnInput("Too many", [new ReturnLineInput(line, 1, ReturnDisposition.Restock)], "r5"), default)));
        var (_, pdf) = await After(a => a.CreditNotePdfAsync(view.CreditNotes[0].Id, default));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [PostgresFact]
    public async Task Standalone_credits_are_capped_by_line_and_delivery_and_are_append_only()
    {
        var item = await StockedItemAsync("CN-1", 5, price: 20m);
        var order = await ConfirmedAsync(Input(new OrderLineInput(item, 2)) with { DeliveryMethod = DeliveryMethod.Delivery, DeliveryAddress = "9 Hill Rd", DeliveryCharge = 15m });
        var line = order.Lines[0].Id;
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CreateCreditNoteAsync(order.Id, new CreditNoteInput("Too much", [new CreditLineInput(line, 40.01m)], 0, Key()), default)));
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CreateCreditNoteAsync(order.Id, new CreditNoteInput("Late", [], 15.01m, Key()), default)));
        var note = await After(a => a.CreateCreditNoteAsync(order.Id, new CreditNoteInput("Late delivery", [new CreditLineInput(line, 5m)], 15m, Key()), default));
        Assert.Equal(20m, note.Total);
        var reloaded = await Orders(s => s.GetAsync(order.Id, default));
        Assert.Equal(55m, reloaded.Total); // the invoice itself never changes
        Assert.Equal(35m, reloaded.BalanceDue);
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CreateCreditNoteAsync(order.Id, new CreditNoteInput("Again", [], 0.01m, Key()), default)));

        await Assert.ThrowsAnyAsync<PostgresException>(() => Run(async sp =>
            await sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE credit_notes SET \"Total\" = 1")));
    }

    // ---------------- Refunds ----------------

    [PostgresFact]
    public async Task Refunds_need_another_approver_cannot_exceed_what_is_owed_and_complete_once()
    {
        var item = await StockedItemAsync("RF-1", 5, price: 50m);
        var order = await ConfirmedAsync(Input(new OrderLineInput(item, 1)));
        await Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default));
        await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Cash, 30m, null, Key()), default));
        await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Card, 40m, null, Key()), default)); // $20 overpaid

        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.RequestRefundAsync(order.Id, new RefundInput(20.01m, PaymentMethod.Cash, "Overpaid", null, false, Key()), default)));
        // Two racing requests for the whole $20: only one can hold it.
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            try { return await After(a => a.RequestRefundAsync(order.Id, new RefundInput(20m, PaymentMethod.Cash, "Overpaid", null, false, Key()), default)); }
            catch (ConflictException) { return null; }
        })));
        var refund = Assert.Single(results, r => r is not null)!;
        Assert.Equal(20m, refund.Allocations.Sum(a => a.Amount));
        Assert.Equal(RefundStatus.Requested, refund.Status);

        await Assert.ThrowsAsync<ForbiddenException>(() => After(a => a.ApproveRefundAsync(refund.Id, default)));
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CompleteRefundAsync(refund.Id, new CompleteRefundInput(PaymentMethod.Cash, "till", Key()), default)));
        actor.ActorId = "approver";
        await After(a => a.ApproveRefundAsync(refund.Id, default));
        var key = Key();
        var done = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => After(a => a.CompleteRefundAsync(refund.Id, new CompleteRefundInput(PaymentMethod.Cash, "Till slip 44", key), default)))));
        Assert.All(done, d => Assert.Equal(RefundStatus.Succeeded, d.Status));
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.CompleteRefundAsync(refund.Id, new CompleteRefundInput(PaymentMethod.Cash, "x", Key()), default)));

        var reloaded = await Orders(s => s.GetAsync(order.Id, default));
        Assert.Equal(20m, reloaded.RefundedTotal);
        Assert.Equal(0, reloaded.BalanceDue);
        Assert.Equal(PaymentStatus.Paid, reloaded.PaymentStatus);
        // A receipt a refund came from can no longer be voided.
        var source = refund.Allocations[0].PaymentId;
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.VoidPaymentAsync(source, new VoidPaymentInput("Oops", Key()), default)));

        var today = await Run(sp => Task.FromResult(DateOnly.FromDateTime(sp.GetRequiredService<ShopTime>().ToLocal(DateTimeOffset.UtcNow).DateTime)));
        var takings = await Run(sp => sp.GetRequiredService<ITakingsReportService>().DailyAsync(new TakingsRequest(today, today), default));
        Assert.True(takings.RefundsPaid >= 20m);
        Assert.Contains(takings.Days[0].Refunds!, r => r.RefundNumber == refund.RefundNumber);
        actor.ActorId = "integration";
    }

    [PostgresFact]
    public async Task An_invoice_with_payments_can_be_cancelled_once_refunded_in_full()
    {
        var item = await StockedItemAsync("RF-2", 2, price: 15m);
        var order = await ConfirmedAsync(Input(new OrderLineInput(item, 2)));
        await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.BankTransfer, 10m, "deposit", Key()), default));
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.CancelAsync(order.Id, new CancelOrderInput("Changed mind"), true, default)));
        // Not owed while the order stands; owed in full when cancelling before any issue.
        await Assert.ThrowsAsync<ConflictException>(() => After(a => a.RequestRefundAsync(order.Id, new RefundInput(10m, PaymentMethod.BankTransfer, "x", null, false, Key()), default)));
        var refund = await After(a => a.RequestRefundAsync(order.Id, new RefundInput(10m, PaymentMethod.BankTransfer, "Order cancelled", null, true, Key()), default));
        actor.ActorId = "approver";
        await After(a => a.ApproveRefundAsync(refund.Id, default));
        await After(a => a.CompleteRefundAsync(refund.Id, new CompleteRefundInput(PaymentMethod.BankTransfer, "BNZ ref 1234", Key()), default));
        actor.ActorId = "integration";
        var cancelled = await Orders(s => s.CancelAsync(order.Id, new CancelOrderInput("Changed mind"), true, default));
        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, (await Run(sp => sp.GetRequiredService<IStockQueryService>().StockAsync(new StockRequest(PageSize: 100), default)))
            .Items.Items.Single(r => r.ItemId == item).Available); // reservation released
    }

    private sealed class SwitchableActor : ICurrentActor
    {
        public string ActorId { get; set; } = "integration";
    }
}
