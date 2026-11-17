using System.Net;
using System.Net.Http.Json;
using Application.Common;
using Application.Inventory;
using Domain.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using P = Application.Identity.PermissionKeys;

namespace Backend.UnitTests;

public sealed class InventoryValidationTests
{
    private static GrnInput Grn(decimal qty = 1, decimal cost = 10m, decimal? price = 20m, string supplier = "Repco") =>
        new(supplier, null, null, null, [new GrnLineInput(Guid.NewGuid(), qty, cost, price)], null);

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1.5, true)]
    [InlineData(0.255, true)]
    [InlineData(0.2555, false)]
    public void Grn_quantity_is_positive_with_at_most_three_decimals(decimal qty, bool valid) =>
        Assert.Equal(valid, new GrnInputValidator().Validate(Grn(qty)).IsValid);

    [Theory]
    [InlineData(10.005, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(12.34, true)]
    public void Unit_cost_is_dollars_and_cents(decimal cost, bool valid) =>
        Assert.Equal(valid, new GrnInputValidator().Validate(Grn(cost: cost)).IsValid);

    [Fact]
    public void Selling_price_must_be_positive_when_given()
    {
        Assert.False(new GrnInputValidator().Validate(Grn(price: 0)).IsValid);
        Assert.True(new GrnInputValidator().Validate(Grn(price: null)).IsValid);
        Assert.False(new GrnInputValidator().Validate(Grn(supplier: "")).IsValid);
    }

    [Fact]
    public void Supplier_date_cannot_be_in_the_future()
    {
        var input = Grn() with { SupplierDocumentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
        Assert.False(new GrnInputValidator().Validate(input).IsValid);
    }

    [Fact]
    public void Adjustment_rules_depend_on_kind()
    {
        var item = Guid.NewGuid();
        var opening = new StockDocumentInput(AdjustmentReason.Opening, null, [new StockDocumentLineInput(item, -1, null, null)], null);
        Assert.Contains(StockDocumentRules.Check(StockDocumentKind.Opening, opening), p => p.Message.Contains("more than zero"));
        Assert.Contains(StockDocumentRules.Check(StockDocumentKind.Opening, opening), p => p.Message.Contains("cost"));
        var adjustment = new StockDocumentInput(AdjustmentReason.Count, null, [new StockDocumentLineInput(item, 2, null, 9m)], null);
        Assert.Contains(StockDocumentRules.Check(StockDocumentKind.Adjustment, adjustment), p => p.Property == "Reason");
        Assert.Contains(StockDocumentRules.Check(StockDocumentKind.Adjustment, adjustment), p => p.Message.Contains("Change price"));
        var ok = new StockDocumentInput(AdjustmentReason.Damage, null, [new StockDocumentLineInput(item, -2, null, null)], null);
        Assert.Empty(StockDocumentRules.Check(StockDocumentKind.Adjustment, ok));
    }

    [Fact]
    public void Adjustment_items_must_be_distinct()
    {
        var item = Guid.NewGuid();
        var input = new StockDocumentInput(AdjustmentReason.Damage, null, [new(item, -1, null, null), new(item, -1, null, null)], null);
        Assert.False(new StockDocumentInputValidator().Validate(input).IsValid);
    }

    [Fact]
    public void Stock_request_takes_date_or_instant_not_both_and_whitelists_sort()
    {
        Assert.False(new StockRequestValidator().Validate(new StockRequest(Date: new DateOnly(2026, 3, 31), AsOf: DateTimeOffset.UtcNow)).IsValid);
        Assert.False(new StockRequestValidator().Validate(new StockRequest(Sort: "CreatedBy")).IsValid);
        Assert.True(new StockRequestValidator().Validate(new StockRequest(Sort: "-onHand")).IsValid);
    }

    [Fact]
    public void Price_change_needs_a_reason_and_cents()
    {
        Assert.False(new PriceChangeInputValidator().Validate(new PriceChangeInput(10m, "")).IsValid);
        Assert.False(new PriceChangeInputValidator().Validate(new PriceChangeInput(10.001m, "supplier rise")).IsValid);
        Assert.True(new PriceChangeInputValidator().Validate(new PriceChangeInput(10.5m, "supplier rise")).IsValid);
    }
}

public sealed class StockDateTests
{
    [Theory]
    // NZ daylight time ends 5 April 2026 and starts 27 September 2026.
    [InlineData(2026, 3, 31, "2026-03-31T11:00:00Z")] // NZDT (+13)
    [InlineData(2026, 4, 5, "2026-04-05T12:00:00Z")]  // changeover day ends in NZST (+12)
    [InlineData(2026, 7, 1, "2026-07-01T12:00:00Z")]  // NZST (+12)
    [InlineData(2026, 9, 27, "2026-09-27T11:00:00Z")] // changeover day ends in NZDT (+13)
    public void A_date_means_the_end_of_that_day_in_New_Zealand(int y, int m, int d, string expectedUtc)
    {
        var (_, end) = new ShopTime().GetUtcDayRange(new DateOnly(y, m, d));
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), end);
    }
}

public sealed class InventoryModelTests
{
    [Fact]
    public void Quantities_have_three_decimals_and_money_two()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=test").Options);
        string Type<T>(string property) => db.Model.FindEntityType(typeof(T))!.FindProperty(property)!.GetColumnType();
        Assert.Equal("numeric(18,3)", Type<StockMovement>(nameof(StockMovement.QtyChange)));
        Assert.Equal("numeric(18,3)", Type<GrnLine>(nameof(GrnLine.Quantity)));
        Assert.Equal("numeric(18,3)", Type<StocktakeLine>(nameof(StocktakeLine.CountedQty)));
        Assert.Equal("numeric(18,2)", Type<StockMovement>(nameof(StockMovement.AverageCostAfter)));
        Assert.Equal("numeric(18,2)", Type<GrnLine>(nameof(GrnLine.UnitCost)));
        Assert.Equal("numeric(18,2)", Type<ItemPriceHistory>(nameof(ItemPriceHistory.SellingPrice)));
    }

    [Fact]
    public void Migration_installs_the_stock_guarantees()
    {
        foreach (var trigger in new[] { "trg_stock_movements_stamp", "trg_stock_movements_immutable", "trg_stock_movements_not_negative",
            "trg_item_price_history_immutable", "trg_grn_lines_draft", "trg_grns_stamp" })
            Assert.Contains(trigger, InventorySql.Up);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", InventorySql.Up);
    }
}

public sealed class InventoryApiTests
{
    [Theory]
    [InlineData("GET", "/api/grns", "inventory.adjust")]
    [InlineData("GET", "/api/stock", "inventory.grn")]
    [InlineData("GET", "/api/stock-adjustments", "inventory.grn")]
    [InlineData("GET", "/api/stocktakes", "inventory.reports")]
    [InlineData("POST", "/api/grns/6c8f8f0e-4a3b-4c55-9d0f-0e1a2b3c4d5e/reverse", "inventory.grn")]
    [InlineData("POST", "/api/items/6c8f8f0e-4a3b-4c55-9d0f-0e1a2b3c4d5e/price", "inventory.reports")]
    public async Task Each_inventory_area_needs_its_own_permission(string method, string url, string held)
    {
        await using var factory = new AuthFactory();
        var response = await factory.ClientWith(factory.TokenFor(held)).SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { reason = "x", sellingPrice = 1 }),
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StoreKeeper_defaults_cannot_change_prices_or_reverse()
    {
        Assert.DoesNotContain(P.InventoryPrice, Infrastructure.Identity.IdentitySeeder.DefaultPermissions("StoreKeeper"));
        Assert.DoesNotContain(P.InventoryReverse, Infrastructure.Identity.IdentitySeeder.DefaultPermissions("StoreKeeper"));
        Assert.Contains(P.InventoryReverse, Infrastructure.Identity.IdentitySeeder.DefaultPermissions("Admin"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Invalid_grn_is_rejected_before_the_database()
    {
        await using var factory = new AuthFactory();
        var response = await factory.ClientWith(factory.TokenFor(P.InventoryGrn)).PostAsJsonAsync("/api/grns", new
        {
            supplierName = "", lines = new[] { new { itemId = Guid.Empty, quantity = 0, unitCost = -1 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "SupplierName", "Lines[0].Quantity", "Lines[0].UnitCost" }) Assert.Contains(field, text);
    }

    [Fact]
    public async Task Enums_are_sent_as_names_and_accepted_as_names()
    {
        await using var factory = new AuthFactory();
        var response = await factory.ClientWith(factory.TokenFor(P.InventoryAdjust)).PostAsJsonAsync("/api/stock-adjustments", new
        {
            reason = "Opening", lines = new[] { new { itemId = Guid.NewGuid(), qtyChange = 1 } },
        });
        // Parsed fine (no 400 for the enum itself); the rule then rejects Opening as an adjustment reason.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("opening stock or a stocktake", await response.Content.ReadAsStringAsync());
    }
}
