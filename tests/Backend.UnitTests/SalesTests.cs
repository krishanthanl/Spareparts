using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using Application.Sales;
using Domain.Sales;

namespace Backend.UnitTests;

public sealed class SalesMathTests
{
    [Fact]
    public void Line_totals_round_half_away_from_zero_and_add_up()
    {
        var priced = SalesMath.Price([new(3, 3.335m, null, null), new(1, 10m, 1.005m, null)], 0, 0);
        Assert.Equal(10.01m, priced.Lines[0].Gross); // 10.005 rounds up
        Assert.Equal(1.01m, priced.Lines[1].LineDiscount);
        Assert.Equal(priced.Lines.Sum(l => l.LineTotal), priced.Total);
    }

    [Fact]
    public void Percent_discount_is_turned_into_cents()
    {
        var priced = SalesMath.Price([new(1, 19.99m, null, 15m)], 0, 0);
        Assert.Equal(3.00m, priced.Lines[0].LineDiscount); // 2.9985
        Assert.Equal(16.99m, priced.Total);
    }

    [Fact]
    public void Order_discount_is_split_by_largest_remainder_and_is_exact()
    {
        var priced = SalesMath.Price([new(1, 10m, null, null), new(1, 10m, null, null), new(1, 10m, null, null)], 10m, 0);
        Assert.Equal([3.34m, 3.33m, 3.33m], priced.Lines.Select(l => l.OrderDiscountShare));
        Assert.Equal(10m, priced.Lines.Sum(l => l.OrderDiscountShare));
        Assert.Equal(20m, priced.Total);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(7.77)]
    [InlineData(99.99)]
    public void Allocation_always_sums_to_the_amount(decimal amount)
    {
        decimal[] weights = [1.01m, 33.33m, 0.07m, 12m, 5.55m];
        var shares = SalesMath.Allocate(amount, weights);
        Assert.Equal(amount, shares.Sum());
        Assert.All(shares, s => Assert.True(s >= 0));
    }

    [Fact]
    public void Delivery_is_added_and_no_gst_is_ever_added()
    {
        var priced = SalesMath.Price([new(2, 25m, null, null)], 0, 12.50m);
        Assert.Equal(62.50m, priced.Total);
    }

    [Fact]
    public void Discounts_cannot_exceed_the_value()
    {
        Assert.Throws<ArgumentException>(() => SalesMath.Price([new(1, 5m, 6m, null)], 0, 0));
        Assert.Throws<ArgumentException>(() => SalesMath.Price([new(1, 5m, null, null)], 5.01m, 0));
    }

    [Theory]
    [InlineData(100, 0, PaymentStatus.Unpaid)]
    [InlineData(100, 40, PaymentStatus.PartiallyPaid)]
    [InlineData(100, 100, PaymentStatus.Paid)]
    [InlineData(100, 120, PaymentStatus.Overpaid)]
    [InlineData(0, 0, PaymentStatus.Paid)]
    public void Payment_status_follows_the_amount_paid(decimal total, decimal paid, PaymentStatus expected) =>
        Assert.Equal(expected, SalesMath.PaymentStatusFor(total, paid));

    [Theory]
    [InlineData(SalesOrderStatus.Confirmed, PaymentStatus.Paid, FulfillmentStatus.Issued, SalesOrderStatus.Completed)]
    [InlineData(SalesOrderStatus.Confirmed, PaymentStatus.Overpaid, FulfillmentStatus.Issued, SalesOrderStatus.Completed)]
    [InlineData(SalesOrderStatus.Confirmed, PaymentStatus.PartiallyPaid, FulfillmentStatus.Issued, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Confirmed, PaymentStatus.Paid, FulfillmentStatus.PartiallyIssued, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Completed, PaymentStatus.PartiallyPaid, FulfillmentStatus.Issued, SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.Draft, PaymentStatus.Paid, FulfillmentStatus.Issued, SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Cancelled, PaymentStatus.Paid, FulfillmentStatus.Issued, SalesOrderStatus.Cancelled)]
    public void Order_status_is_derived_from_payment_and_fulfilment(SalesOrderStatus current, PaymentStatus payment, FulfillmentStatus fulfillment, SalesOrderStatus expected) =>
        Assert.Equal(expected, SalesMath.StatusFor(current, payment, fulfillment));
}

public sealed class SalesValidationTests
{
    private static OrderInput Order(DeliveryMethod delivery = DeliveryMethod.Pickup, decimal charge = 0, decimal orderDiscount = 0, string? reason = null,
        params OrderLineInput[] lines) =>
        new(SalesSource.Telephone, null, "Sam", null, null, null, null, PaymentTerms.Immediate, delivery, delivery == DeliveryMethod.Delivery ? "1 Main St" : null,
            charge, orderDiscount, reason, null, lines.Length == 0 ? [new OrderLineInput(Guid.NewGuid(), 1)] : lines);

    [Fact]
    public void Pickup_has_no_delivery_charge()
    {
        Assert.False(new OrderInputValidator().Validate(Order(charge: 5)).IsValid);
        Assert.True(new OrderInputValidator().Validate(Order(DeliveryMethod.Delivery, 5)).IsValid);
    }

    [Fact]
    public void Order_discount_needs_a_reason()
    {
        Assert.False(new OrderInputValidator().Validate(Order(orderDiscount: 5)).IsValid);
        Assert.True(new OrderInputValidator().Validate(Order(orderDiscount: 5, reason: "Trade customer")).IsValid);
    }

    [Fact]
    public void A_line_discount_is_an_amount_or_a_percent_not_both()
    {
        var id = Guid.NewGuid();
        Assert.False(new OrderInputValidator().Validate(Order(lines: new OrderLineInput(id, 1, null, 1m, 5m))).IsValid);
        Assert.False(new OrderInputValidator().Validate(Order(lines: [new OrderLineInput(id, 1), new OrderLineInput(id, 2)])).IsValid);
    }

    [Fact]
    public void Gst_and_total_fields_in_a_request_are_rejected()
    {
        var json = """{"source":2,"paymentTerms":1,"deliveryMethod":1,"deliveryCharge":0,"orderDiscount":0,"lines":[],"gstAmount":15}""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrderInput>(json, options));
        var total = """{"itemId":"00000000-0000-0000-0000-000000000001","quantity":1,"lineTotal":1}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrderLineInput>(total, options));
    }

    [Fact]
    public void Payments_are_positive_cents_with_an_idempotency_key()
    {
        Assert.True(new PaymentInputValidator().Validate(new PaymentInput(PaymentMethod.Cash, 10.50m, null, "k1")).IsValid);
        Assert.False(new PaymentInputValidator().Validate(new PaymentInput(PaymentMethod.Cash, 0, null, "k1")).IsValid);
        Assert.False(new PaymentInputValidator().Validate(new PaymentInput(PaymentMethod.Cash, 1.001m, null, "k1")).IsValid);
        Assert.False(new PaymentInputValidator().Validate(new PaymentInput(PaymentMethod.Cash, 1m, null, "")).IsValid);
    }

    [Fact]
    public void Takings_range_is_limited()
    {
        var day = new DateOnly(2026, 11, 1);
        Assert.True(new TakingsRequestValidator().Validate(new TakingsRequest(day, day)).IsValid);
        Assert.False(new TakingsRequestValidator().Validate(new TakingsRequest(day, day.AddDays(-1))).IsValid);
        Assert.False(new TakingsRequestValidator().Validate(new TakingsRequest(day, day.AddDays(200))).IsValid);
    }
}

public sealed class SimplePdfTests
{
    [Fact]
    public void Builds_a_valid_looking_pdf_with_escaped_text()
    {
        var type = typeof(Infrastructure.DependencyInjection).Assembly.GetType("Infrastructure.Sales.SimplePdf")!;
        var pdf = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetMethod("Text")!.Invoke(pdf, [40.0, 40.0, "Brake (front) \\ Māori ā – $12.00", 10.0, false, 0.0]);
        type.GetMethod("NewPage")!.Invoke(pdf, null);
        var bytes = (byte[])type.GetMethod("Build")!.Invoke(pdf, null)!;
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 2", text);
        Assert.Contains(@"Brake \(front\) \\ M", text);
        Assert.Contains("startxref", text);
    }
}

public sealed class SalesQueryTranslationTests
{
    /// <summary>The reservation query runs inside every stock posting, so it must translate to SQL (no client evaluation).</summary>
    [Fact]
    public void Reservation_query_translates_to_sql()
    {
        using var db = new Infrastructure.Persistence.AppDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Infrastructure.Persistence.AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test").Options);
        var ledger = typeof(Infrastructure.DependencyInjection).Assembly.GetType("Infrastructure.Inventory.StockLedger")!;
        var method = ledger.GetMethod("Reservations")!;
        var query = (IQueryable)method.Invoke(null, [db, new[] { Guid.NewGuid() }])!;
        var sql = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToQueryString(query);
        Assert.Contains("sales_order_lines", sql);
        Assert.Contains("stock_issue_lines", sql);
        Assert.Contains("GROUP BY", sql);
    }
}
