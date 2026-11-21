using Domain.Sales;

namespace Application.Sales;

public sealed record PricedLineInput(decimal Quantity, decimal UnitPrice, decimal? DiscountAmount, decimal? DiscountPercent);
public sealed record PricedLine(decimal Gross, decimal LineDiscount, decimal OrderDiscountShare, decimal LineTotal);
public sealed record PricedOrder(IReadOnlyList<PricedLine> Lines, decimal Subtotal, decimal LineDiscountTotal, decimal OrderDiscount,
    decimal DeliveryCharge, decimal Total);

/// <summary>
/// Server-side money rules (ADR 0005, D9). No GST is ever added or extracted: the shop is not registered.
/// All rounding is to cents, half away from zero. Order discounts are split across lines by
/// largest remainder so the parts always add up to the exact discount.
/// </summary>
public static class SalesMath
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static PricedOrder Price(IReadOnlyList<PricedLineInput> lines, decimal orderDiscount, decimal deliveryCharge)
    {
        var gross = lines.Select(l => Money(l.Quantity * l.UnitPrice)).ToArray();
        var lineDiscounts = lines.Select((l, i) => l.DiscountPercent is { } pct ? Money(gross[i] * pct / 100m) : Money(l.DiscountAmount ?? 0)).ToArray();
        for (var i = 0; i < lines.Count; i++)
            if (lineDiscounts[i] > gross[i]) throw new ArgumentException($"Line {i + 1}: the discount is more than the line value.");
        var net = gross.Select((g, i) => g - lineDiscounts[i]).ToArray();
        var netTotal = net.Sum();
        orderDiscount = Money(orderDiscount);
        if (orderDiscount > netTotal) throw new ArgumentException("The order discount is more than the order value.");
        var shares = Allocate(orderDiscount, net);
        var priced = lines.Select((_, i) => new PricedLine(gross[i], lineDiscounts[i], shares[i], net[i] - shares[i])).ToList();
        var subtotal = gross.Sum();
        var lineDiscountTotal = lineDiscounts.Sum();
        deliveryCharge = Money(deliveryCharge);
        return new PricedOrder(priced, subtotal, lineDiscountTotal, orderDiscount, deliveryCharge, subtotal - lineDiscountTotal - orderDiscount + deliveryCharge);
    }

    /// <summary>Splits <paramref name="amount"/> in proportion to <paramref name="weights"/>, in whole cents, summing exactly.</summary>
    public static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var total = weights.Sum();
        if (amount == 0 || total <= 0) return result;
        var cents = (long)(amount * 100);
        var exact = weights.Select(w => cents * w / total).ToArray();
        var floor = exact.Select(e => (long)decimal.Floor(e)).ToArray();
        var left = cents - floor.Sum();
        // Largest fractional remainder first; ties go to the earlier line so results are repeatable.
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => exact[i] - floor[i]).ThenBy(i => i).Take((int)left))
            floor[i]++;
        for (var i = 0; i < result.Length; i++) result[i] = floor[i] / 100m;
        return result;
    }

    public static PaymentStatus PaymentStatusFor(decimal total, decimal paid) =>
        paid > total ? PaymentStatus.Overpaid
        : paid == total ? PaymentStatus.Paid
        : paid > 0 ? PaymentStatus.PartiallyPaid
        : PaymentStatus.Unpaid;

    public static FulfillmentStatus FulfillmentStatusFor(decimal ordered, decimal issued) =>
        issued <= 0 ? FulfillmentStatus.NotIssued : issued >= ordered ? FulfillmentStatus.Issued : FulfillmentStatus.PartiallyIssued;

    /// <summary>Confirmed orders become Completed when fully paid and fully issued, and go back if that stops being true.</summary>
    public static SalesOrderStatus StatusFor(SalesOrderStatus current, PaymentStatus payment, FulfillmentStatus fulfillment)
    {
        if (current is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Completed)) return current;
        var done = payment is PaymentStatus.Paid or PaymentStatus.Overpaid && fulfillment == FulfillmentStatus.Issued;
        return done ? SalesOrderStatus.Completed : SalesOrderStatus.Confirmed;
    }
}
