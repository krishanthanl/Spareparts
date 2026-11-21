using Application.Common;
using Application.Checkout;
using Application.Inventory;
using Application.Sales;
using Domain.Catalog;
using Domain.Inventory;
using Domain.Sales;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Sales;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

/// <summary>
/// Phase 4a sales guarantees on a real, throwaway PostgreSQL database: reservations under concurrency,
/// issue exactly once, idempotent payments, frozen invoices and gapless numbers (ADR 0005).
/// </summary>
public sealed class SalesPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string adminConnection = "";
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
        database = "sales_" + Guid.NewGuid().ToString("N");
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
        services.AddScoped<ICurrentActor, Actor>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddCatalogInfrastructure(new StorageSettings { LocalRoot = Path.Combine(Path.GetTempPath(), database) });
        services.AddScoped<CheckoutQuoteService>();
        services.AddScoped<CheckoutOrders>();
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

    /// <summary>An item with stock received at $10 cost and a $25 selling price.</summary>
    private Task<Guid> StockedItemAsync(string sku, decimal qty, decimal price = 25m) => Run(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var item = new Item { Sku = sku, Name = "Brake pad " + sku, CategoryId = categoryId };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        if (qty > 0)
        {
            var grns = sp.GetRequiredService<IGrnService>();
            var grn = await grns.CreateAsync(new GrnInput("Repco", null, null, null, [new GrnLineInput(item.Id, qty, 10m, price)], null), default);
            await grns.PostAsync(grn.Id, default);
        }
        return item.Id;
    });

    private static OrderInput Input(params OrderLineInput[] lines) =>
        new(SalesSource.Telephone, null, "Sam Driver", "021 555 000", null, null, "1 Queen St, Auckland", PaymentTerms.Immediate,
            DeliveryMethod.Pickup, null, 0, 0, null, null, lines);

    private Task<OrderDto> ConfirmedAsync(params OrderLineInput[] lines) => Orders(async s =>
    {
        var draft = await s.CreateAsync(Input(lines), canDiscount: false, default);
        return await s.ConfirmAsync(draft.Id, default);
    });

    private Task<decimal> OnHandAsync(Guid item) => Run(async sp =>
        await sp.GetRequiredService<AppDbContext>().StockMovements.Where(m => m.ItemId == item).SumAsync(m => m.QtyChange));

    private Task<StockRow> StockRowAsync(Guid item) => Run(async sp =>
        (await sp.GetRequiredService<IStockQueryService>().StockAsync(new StockRequest(PageSize: 100), default)).Items.Items.Single(r => r.ItemId == item));

    [PostgresFact]
    public async Task Storefront_draft_holds_stock_and_verified_transfer_posts_one_invoice_and_payment()
    {
        var item = await StockedItemAsync("WEB-100", 1);
        var request = new CheckoutOrderRequest(new CheckoutQuoteRequest([new(item, 1)], CheckoutFulfillment.Pickup, null, false),
            Guid.NewGuid(), "Sam Driver", "sam@example.test", null, "1 Queen St, Auckland", null, [25m], 0m);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Run(sp => sp.GetRequiredService<CheckoutOrders>().PlaceAsync(request, default))));
        var order = results[0];
        Assert.Equal(order.OrderId, results[1].OrderId);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.Equal(0, (await StockRowAsync(item)).Available);
        Assert.Equal(1, await OnHandAsync(item));
        await Assert.ThrowsAsync<ConflictException>(() => Run(sp => sp.GetRequiredService<CheckoutOrders>().PlaceAsync(request with { RequestKey = Guid.NewGuid() }, default)));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => sp.GetRequiredService<CheckoutOrders>().TrackAsync(order.OrderId, Guid.NewGuid(), default)));

        var transfer = new PaymentInput(PaymentMethod.BankTransfer, 25m, "Verified statement reference", Guid.NewGuid().ToString());
        var paid = await Orders(s => s.RecordPaymentAsync(order.OrderId, transfer, default));
        var replay = await Orders(s => s.RecordPaymentAsync(order.OrderId, transfer, default));
        Assert.NotNull(paid.InvoiceNumber);
        Assert.Equal(paid.InvoiceNumber, replay.InvoiceNumber);
        Assert.Single(replay.Payments);
        Assert.Equal(PaymentStatus.Paid, replay.PaymentStatus);
        Assert.Equal(0, (await StockRowAsync(item)).Available);
        Assert.Equal(1, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Expired_storefront_draft_releases_stock_and_cannot_accept_payment()
    {
        var item = await StockedItemAsync("WEB-101", 1);
        var request = new CheckoutOrderRequest(new CheckoutQuoteRequest([new(item, 1)], CheckoutFulfillment.Pickup, null, false),
            Guid.NewGuid(), "Sam Driver", "sam@example.test", null, "1 Queen St, Auckland", null, [25m], 0m);
        var order = await Run(sp => sp.GetRequiredService<CheckoutOrders>().PlaceAsync(request, default));
        await Run(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var draft = await db.SalesOrders.SingleAsync(x => x.Id == order.OrderId);
            draft.CheckoutHoldUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        Assert.Equal(1, (await StockRowAsync(item)).Available);
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.RecordPaymentAsync(order.OrderId,
            new PaymentInput(PaymentMethod.BankTransfer, 25m, "Late transfer", Guid.NewGuid().ToString()), default)));
        var tracked = await Run(async sp =>
        {
            var checkout = sp.GetRequiredService<CheckoutOrders>();
            await checkout.ExpireAsync(default);
            return await checkout.TrackAsync(order.OrderId, request.RequestKey, default);
        });
        Assert.Equal(SalesOrderStatus.Cancelled, tracked.Status);
    }

    [PostgresFact]
    public async Task Confirm_reserves_stock_and_only_one_of_two_racing_orders_gets_the_last_unit()
    {
        var item = await StockedItemAsync("SP-100", 3);
        await ConfirmedAsync(new OrderLineInput(item, 2));
        var row = await StockRowAsync(item);
        Assert.Equal(3, row.OnHand);
        Assert.Equal(2, row.Reserved);
        Assert.Equal(1, row.Available);

        var drafts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Orders(s => s.CreateAsync(Input(new OrderLineInput(item, 1)), false, default))));
        var results = await Task.WhenAll(drafts.Select(d => Task.Run(async () =>
        {
            try { await Orders(s => s.ConfirmAsync(d.Id, default)); return true; }
            catch (ConflictException) { return false; }
        })));
        Assert.Single(results, r => r);
        Assert.Equal(0, (await StockRowAsync(item)).Available);
        Assert.Equal(3, await OnHandAsync(item)); // reservations never move stock
    }

    [PostgresFact]
    public async Task Adjustments_and_grn_reversals_cannot_take_reserved_stock()
    {
        var item = await StockedItemAsync("SP-101", 4);
        await ConfirmedAsync(new OrderLineInput(item, 3));
        var error = await Record.ExceptionAsync(() => Run(async sp =>
        {
            var docs = sp.GetRequiredService<IStockDocumentService>();
            var d = await docs.CreateAsync(StockDocumentKind.Adjustment, new StockDocumentInput(AdjustmentReason.Damage, null, [new StockDocumentLineInput(item, -2, null, null)], null), default);
            return await docs.PostAsync(StockDocumentKind.Adjustment, d.Id, default);
        }));
        Assert.IsType<ConflictException>(error);
        Assert.Contains("reserved", error.Message);
        Assert.Equal(4, await OnHandAsync(item));
    }

    [PostgresFact]
    public async Task Partial_issue_then_the_rest_decreases_stock_exactly_once_and_over_issue_is_rejected()
    {
        var item = await StockedItemAsync("SP-102", 10);
        var order = await ConfirmedAsync(new OrderLineInput(item, 4));
        var line = order.Lines.Single();
        order = await Orders(s => s.IssueAsync(order.Id, new IssueInput([new IssueLineInput(line.Id, 1)], "issue-a"), default));
        Assert.Equal(FulfillmentStatus.PartiallyIssued, order.FulfillmentStatus);
        Assert.Equal(9, await OnHandAsync(item));
        Assert.Equal(3, (await StockRowAsync(item)).Reserved);

        // The same key again is a retry, not a second issue.
        await Orders(s => s.IssueAsync(order.Id, new IssueInput([new IssueLineInput(line.Id, 1)], "issue-a"), default));
        Assert.Equal(9, await OnHandAsync(item));

        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.IssueAsync(order.Id, new IssueInput([new IssueLineInput(line.Id, 4)], null), default)));
        order = await Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default));
        Assert.Equal(FulfillmentStatus.Issued, order.FulfillmentStatus);
        Assert.Equal(6, await OnHandAsync(item));
        Assert.Equal(0, (await StockRowAsync(item)).Reserved);
        Assert.Equal(2, order.Issues.Count);
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default)));
    }

    [PostgresFact]
    public async Task Racing_issue_requests_post_the_goods_once()
    {
        var item = await StockedItemAsync("SP-103", 5);
        var order = await ConfirmedAsync(new OrderLineInput(item, 5));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try { await Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default)); return true; }
            catch (ConflictException) { return false; }
        })));
        Assert.Single(results, r => r);
        Assert.Equal(0, await OnHandAsync(item));
        var issueRows = await Run(sp => sp.GetRequiredService<AppDbContext>().StockIssueLines.CountAsync());
        Assert.Equal(1, issueRows);
    }

    [PostgresFact]
    public async Task Cancel_releases_the_reservation_and_is_blocked_after_an_issue_or_payment()
    {
        var item = await StockedItemAsync("SP-104", 2);
        var order = await ConfirmedAsync(new OrderLineInput(item, 2));
        await Assert.ThrowsAsync<ForbiddenException>(() => Orders(s => s.CancelAsync(order.Id, new CancelOrderInput("Changed mind"), false, default)));
        var cancelled = await Orders(s => s.CancelAsync(order.Id, new CancelOrderInput("Changed mind"), true, default));
        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(order.InvoiceNumber, cancelled.InvoiceNumber); // numbers are never reused
        Assert.Equal(2, (await StockRowAsync(item)).Available);

        var paid = await ConfirmedAsync(new OrderLineInput(item, 1));
        await Orders(s => s.RecordPaymentAsync(paid.Id, new PaymentInput(PaymentMethod.Cash, 5m, null, "dep-1"), default));
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.CancelAsync(paid.Id, new CancelOrderInput("x"), true, default)));

        var issued = await ConfirmedAsync(new OrderLineInput(item, 1));
        await Orders(s => s.IssueAsync(issued.Id, new IssueInput(null, null), default));
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.CancelAsync(issued.Id, new CancelOrderInput("x"), true, default)));
        // Even a direct database update cannot cancel an order whose goods have left.
        var error = await Record.ExceptionAsync(() => Run(sp => sp.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlAsync($"UPDATE sales_orders SET \"Status\" = 4 WHERE \"Id\" = {issued.Id}")));
        Assert.Contains("cannot be cancelled", error!.Message);
    }

    [PostgresFact]
    public async Task Payments_are_partial_idempotent_exact_under_concurrency_and_voids_restore_the_balance()
    {
        var item = await StockedItemAsync("SP-105", 5, price: 30m);
        var order = await ConfirmedAsync(new OrderLineInput(item, 2)); // $60
        Assert.Equal(60m, order.Total);

        order = await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Card, 20m, "EFTPOS 1", "pay-1"), default));
        order = await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Card, 20m, "EFTPOS 1", "pay-1"), default)); // retry
        Assert.Equal(20m, order.AmountPaid);
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
            Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Cash, 10m, null, $"pay-c{i}"), default)))));
        order = await Orders(s => s.GetAsync(order.Id, default));
        Assert.Equal(60m, order.AmountPaid);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(5, order.Payments.Count);

        order = await Orders(s => s.IssueAsync(order.Id, new IssueInput(null, null), default));
        Assert.Equal(SalesOrderStatus.Completed, order.Status);

        var first = order.Payments.First(p => p.ReceiptNumber.StartsWith("RCT"));
        order = await Orders(s => s.VoidPaymentAsync(first.Id, new VoidPaymentInput("Wrong card", "void-1"), default));
        Assert.Equal(40m, order.AmountPaid);
        Assert.Equal(SalesOrderStatus.Confirmed, order.Status); // no longer fully paid
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.VoidPaymentAsync(first.Id, new VoidPaymentInput("again", "void-2"), default)));

        order = await Orders(s => s.RecordPaymentAsync(order.Id, new PaymentInput(PaymentMethod.Cash, 25m, null, "pay-over"), default));
        Assert.Equal(PaymentStatus.Overpaid, order.PaymentStatus);
        Assert.Equal(-5m, order.BalanceDue);

        var error = await Record.ExceptionAsync(() => Run(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE payments SET \"Amount\" = 1")));
        Assert.Contains("cannot be changed", error!.Message);
    }

    [PostgresFact]
    public async Task Confirmed_invoices_keep_their_prices_and_customer_snapshot()
    {
        var item = await StockedItemAsync("SP-106", 5, price: 40m);
        var customer = await Run(sp => sp.GetRequiredService<ICustomerService>().CreateAsync(new CustomerInput("Kiri Mechanics", "09 111 222", null, null, "5 Trade Rd", null, null), default));
        var draft = await Orders(s => s.CreateAsync(Input(new OrderLineInput(item, 1)) with { CustomerId = customer.Id, CustomerName = null, CustomerPhone = null, BillingAddress = null }, false, default));
        Assert.Equal("Kiri Mechanics", draft.CustomerName);
        var order = await Orders(s => s.ConfirmAsync(draft.Id, default));

        await Run(sp => sp.GetRequiredService<IPriceService>().ChangeAsync(item, new PriceChangeInput(55m, "Supplier increase"), default));
        await Run(sp => sp.GetRequiredService<ICustomerService>().UpdateAsync(customer.Id, new CustomerInput("Kiri Motors", "09 999", null, null, "New Rd", null, null, true, customer.Version), default));
        var reread = await Orders(s => s.GetAsync(order.Id, default));
        Assert.Equal(40m, reread.Lines.Single().UnitPrice);
        Assert.Equal(40m, reread.Total);
        Assert.Equal("Kiri Mechanics", reread.CustomerName);
        Assert.Equal("5 Trade Rd", reread.BillingAddress);

        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.UpdateAsync(order.Id, Input(new OrderLineInput(item, 3)) with { Version = reread.Version }, false, default)));
        var error = await Record.ExceptionAsync(() => Run(sp => sp.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlAsync($"UPDATE sales_orders SET \"Total\" = 1, \"Subtotal\" = 1 WHERE \"Id\" = {order.Id}")));
        Assert.Contains("cannot be edited", error!.Message);
        error = await Record.ExceptionAsync(() => Run(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE sales_order_lines SET \"UnitPrice\" = 1")));
        Assert.Contains("cannot change", error!.Message);
    }

    [PostgresFact]
    public async Task Discounts_need_permission_and_a_reason_and_the_totals_are_server_calculated()
    {
        var item = await StockedItemAsync("SP-107", 5, price: 10m);
        var discounted = Input(new OrderLineInput(item, 3, 9m, null, null, "Trade price"));
        await Assert.ThrowsAsync<ForbiddenException>(() => Orders(s => s.CreateAsync(discounted, false, default)));
        var noReason = Input(new OrderLineInput(item, 3, 9m));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => Orders(s => s.CreateAsync(noReason, true, default)));
        var order = await Orders(s => s.CreateAsync(discounted with { OrderDiscount = 1m, DiscountReason = "Loyalty" }, true, default));
        Assert.Equal(10m, order.Lines.Single().ListPrice);
        Assert.Equal(9m, order.Lines.Single().UnitPrice);
        Assert.Equal(26m, order.Total);
        Assert.Equal(0m, order.GstAmount);
        Assert.Equal(TaxTreatment.NotRegistered, order.TaxTreatment);
    }

    [PostgresFact]
    public async Task Invoice_numbers_are_gapless_under_concurrency_and_drafts_have_none()
    {
        var item = await StockedItemAsync("SP-108", 50);
        var draft = await Orders(s => s.CreateAsync(Input(new OrderLineInput(item, 1)), false, default));
        Assert.Null(draft.InvoiceNumber);
        var orders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ConfirmedAsync(new OrderLineInput(item, 1)))));
        var values = orders.Select(o => int.Parse(o.InvoiceNumber![4..])).Order().ToList();
        Assert.Equal(Enumerable.Range(values[0], values.Count), values);
    }

    [PostgresFact]
    public async Task Quick_sale_creates_confirms_issues_and_pays_in_one_step_and_prints()
    {
        var item = await StockedItemAsync("SP-109", 3, price: 12.50m);
        var input = new QuickSaleInput(Input(new OrderLineInput(item, 2)) with { Source = SalesSource.Counter, CustomerName = null }, PaymentMethod.Cash, null, null, "qs-1");
        var sale = await Orders(s => s.QuickSaleAsync(input, false, default));
        Assert.Equal(SalesOrderStatus.Completed, sale.Status);
        Assert.Equal(25m, sale.AmountPaid);
        Assert.Equal(1, await OnHandAsync(item));
        var again = await Orders(s => s.QuickSaleAsync(input, false, default));
        Assert.Equal(sale.Id, again.Id);
        Assert.Equal(1, await OnHandAsync(item));

        var (name, pdf) = await Run(sp => sp.GetRequiredService<IInvoicePdfService>().RenderAsync(sale.Id, default));
        Assert.Equal(sale.InvoiceNumber + ".pdf", name);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        // A sale that cannot be filled leaves nothing behind.
        var tooMany = input with { Order = input.Order with { Lines = [new OrderLineInput(item, 5)] }, IdempotencyKey = "qs-2" };
        await Assert.ThrowsAsync<ConflictException>(() => Orders(s => s.QuickSaleAsync(tooMany, false, default)));
        Assert.Equal(1, await Run(sp => sp.GetRequiredService<AppDbContext>().SalesOrders.CountAsync(o => o.Source == SalesSource.Counter)));
    }

    [PostgresFact]
    public async Task Daily_takings_group_payments_by_method_in_shop_time()
    {
        var item = await StockedItemAsync("SP-110", 10, price: 20m);
        var a = await ConfirmedAsync(new OrderLineInput(item, 1));
        var b = await ConfirmedAsync(new OrderLineInput(item, 2));
        await Orders(s => s.RecordPaymentAsync(a.Id, new PaymentInput(PaymentMethod.Cash, 20m, null, "t-1"), default));
        var paid = await Orders(s => s.RecordPaymentAsync(b.Id, new PaymentInput(PaymentMethod.Card, 40m, null, "t-2"), default));
        await Orders(s => s.VoidPaymentAsync(paid.Payments.Single().Id, new VoidPaymentInput("Duplicate swipe", "t-3"), default));

        var today = await Run(sp => Task.FromResult(DateOnly.FromDateTime(sp.GetRequiredService<ShopTime>().ToLocal(DateTimeOffset.UtcNow).DateTime)));
        var report = await Run(sp => sp.GetRequiredService<ITakingsReportService>().DailyAsync(new TakingsRequest(today.AddDays(-1), today), default));
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(60m, report.Received);
        Assert.Equal(40m, report.Voided);
        Assert.Equal(20m, report.Net);
        Assert.Equal(20m, report.Methods.Single(m => m.Method == PaymentMethod.Cash).Net);
        Assert.Equal(0m, report.Methods.Single(m => m.Method == PaymentMethod.Card).Net);
        Assert.Equal(3, report.Days.Single(d => d.Date == today).Payments.Count);
    }

    private sealed class Actor : ICurrentActor { public string ActorId => "integration"; }
}
