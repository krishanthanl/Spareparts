using System.Text.Json;
using Application.Sales;
using Domain.Sales;

namespace Backend.UnitTests;

public sealed class AfterSaleTests
{
    private static readonly ZoneRule Waikato = new(Guid.NewGuid(), "Waikato", 12.50m, null, 8m, "1-2 days", [(3200, 3299)]);

    [Theory]
    [InlineData("3216", true)]
    [InlineData(" 0600 ", true)]
    [InlineData("321", false)]
    [InlineData("32a6", false)]
    [InlineData("32166", false)]
    [InlineData(null, false)]
    public void Postcodes_are_four_digits(string? value, bool ok) => Assert.Equal(ok, AfterSaleMath.TryParsePostcode(value, out _));

    [Fact]
    public void Quote_uses_zone_charge_free_threshold_and_rural_surcharge()
    {
        Assert.Equal(12.50m, AfterSaleMath.Quote(Waikato, 50m, false, null).Charge);
        Assert.Equal(20.50m, AfterSaleMath.Quote(Waikato, 50m, true, null).Charge);
        var free = AfterSaleMath.Quote(Waikato, 100m, false, 100m);
        Assert.True(free.Free);
        Assert.Equal(0m, free.Charge);
        Assert.Equal(8m, AfterSaleMath.Quote(Waikato, 100m, true, 100m).Charge);
        Assert.Equal(12.50m, AfterSaleMath.Quote(Waikato with { FreeDeliveryThreshold = 500m }, 100m, false, 100m).Charge); // zone threshold wins
    }

    [Fact]
    public void Zones_are_found_by_range_and_overlaps_are_detected()
    {
        Assert.Equal("Waikato", AfterSaleMath.FindZone([Waikato], 3299)?.Name);
        Assert.Null(AfterSaleMath.FindZone([Waikato], 3300));
        Assert.Null(AfterSaleMath.FindOverlap([Waikato, Waikato with { Name = "Bay", Postcodes = [(3300, 3399)] }]));
        Assert.Equal("Waikato and Bay", AfterSaleMath.FindOverlap([Waikato, Waikato with { Name = "Bay", Postcodes = [(3299, 3399)] }]));
    }

    [Theory]
    [InlineData(29, 3)]
    [InlineData(10, 3)]
    [InlineData(100, 7)]
    [InlineData(0.05, 3)]
    public void Returning_a_line_in_single_units_credits_exactly_the_line_total(decimal lineTotal, int qty)
    {
        decimal credited = 0;
        for (var returned = 0; returned < qty; returned++)
            credited += AfterSaleMath.ReturnCredit(lineTotal, qty, returned, 1, credited, credited);
        Assert.Equal(lineTotal, credited);
    }

    [Fact]
    public void Return_credit_never_exceeds_what_is_left_after_price_adjustments()
    {
        // $30 line, $25 already credited as a price adjustment: returning everything credits only the last $5.
        Assert.Equal(5m, AfterSaleMath.ReturnCredit(30m, 3, 0, 3, 0, 25m));
        Assert.Equal(0m, AfterSaleMath.ReturnCredit(30m, 3, 0, 3, 0, 30m));
    }

    [Fact]
    public void Refundable_is_money_held_beyond_what_is_owed_minus_pending_refunds()
    {
        Assert.Equal(20m, AfterSaleMath.Refundable(50m, 70m, 0, false));
        Assert.Equal(5m, AfterSaleMath.Refundable(50m, 70m, 15m, false));
        Assert.Equal(0m, AfterSaleMath.Refundable(50m, 30m, 0, false));
        Assert.Equal(30m, AfterSaleMath.Refundable(50m, 30m, 0, true)); // cancelling: everything paid goes back
    }

    [Fact]
    public void Refunds_are_allocated_newest_receipt_first_within_what_each_has_left()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var split = AfterSaleMath.AllocateRefund(25m, [(b, 10m), (a, 30m)]);
        Assert.Equal([(b, 10m), (a, 15m)], split);
        Assert.Throws<InvalidOperationException>(() => AfterSaleMath.AllocateRefund(50m, [(a, 30m)]));
    }

    [Fact]
    public void Balance_accounts_for_credits_and_refunds()
    {
        var o = new SalesOrder { Status = SalesOrderStatus.Confirmed, Total = 100m, CreditTotal = 30m, AmountPaid = 100m, RefundedTotal = 20m };
        Assert.Equal(70m, o.NetTotal);
        Assert.Equal(80m, o.NetPaid);
        Assert.Equal(-10m, o.Balance); // shop still owes $10
        Assert.Equal(PaymentStatus.Overpaid, AfterSaleMath.PaymentStatusFor(o));
    }

    [Fact]
    public void Inputs_reject_injected_gst_and_totals()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreditNoteInput>("""{"reason":"x","lines":[],"deliveryAmount":0,"idempotencyKey":"k","gstAmount":1}""",
            JsonSerializerOptions.Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RefundInput>("""{"amount":1,"method":1,"reason":"x","forCancellation":false,"idempotencyKey":"k","taxTreatment":2}""",
            JsonSerializerOptions.Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesReturnInput>("""{"reason":"x","lines":[],"idempotencyKey":"k","creditTotal":5}""",
            JsonSerializerOptions.Web));
    }

    [Fact]
    public void Validators_require_reasons_evidence_and_valid_postcodes()
    {
        Assert.False(new CompleteRefundInputValidator().Validate(new CompleteRefundInput(PaymentMethod.Cash, " ", "k")).IsValid);
        Assert.False(new SalesReturnInputValidator().Validate(new SalesReturnInput("", [], "k")).IsValid);
        Assert.False(new CreditNoteInputValidator().Validate(new CreditNoteInput("x", [], 0, "k")).IsValid);
        Assert.False(new DeliveryZoneInputValidator().Validate(new DeliveryZoneInput("Z", 1, null, 0, null, true, [new PostcodeRangeInput("3300", "3200")])).IsValid);
        Assert.True(new DeliveryZoneInputValidator().Validate(new DeliveryZoneInput("Z", 1, null, 0, null, true, [new PostcodeRangeInput("3200", "3300")])).IsValid);
        Assert.False(new RefundInputValidator().Validate(new RefundInput(1.005m, PaymentMethod.Cash, "x", null, false, "k")).IsValid);
    }

    [Fact]
    public void Legacy_guard_restores_the_phase_4a_function()
    {
        Assert.DoesNotContain("CreditTotal", Infrastructure.Persistence.AfterSaleSql.Down.Split("DROP TRIGGER")[0]);
        Assert.DoesNotContain("Refund the payments", Infrastructure.Persistence.AfterSaleSql.Down);
        Assert.Contains("Refund the payments", Infrastructure.Persistence.AfterSaleSql.Up);
    }
}
