using Domain.Sales;

namespace Application.Sales;

public sealed record ZoneRule(Guid ZoneId, string Name, decimal Charge, decimal? FreeDeliveryThreshold, decimal RuralSurcharge, string? ServicePromise,
    IReadOnlyList<(int From, int To)> Postcodes);
public sealed record DeliveryQuote(Guid ZoneId, string ZoneName, decimal Charge, bool Free, decimal RuralSurcharge, string? ServicePromise);

/// <summary>Pure after-sale rules (ADR 0006): delivery quotes, credit amounts and refund limits. No GST anywhere.</summary>
public static class AfterSaleMath
{
    public static bool TryParsePostcode(string? value, out int postcode)
    {
        postcode = 0;
        var v = value?.Trim();
        return v is { Length: 4 } && v.All(char.IsAsciiDigit) && int.TryParse(v, out postcode);
    }

    /// <summary>The first active zone whose ranges cover the postcode, or null when the shop does not deliver there.</summary>
    public static ZoneRule? FindZone(IEnumerable<ZoneRule> zones, int postcode) =>
        zones.FirstOrDefault(z => z.Postcodes.Any(p => postcode >= p.From && postcode <= p.To));

    /// <summary>
    /// Zone charge, waived when the goods value (after discounts) reaches the zone's or the shop's free threshold.
    /// The rural surcharge always applies: free delivery covers the standard run only.
    /// </summary>
    public static DeliveryQuote Quote(ZoneRule zone, decimal goodsValue, bool rural, decimal? shopFreeThreshold)
    {
        var threshold = zone.FreeDeliveryThreshold ?? shopFreeThreshold;
        var free = threshold is { } t && goodsValue >= t;
        var surcharge = rural ? zone.RuralSurcharge : 0;
        return new DeliveryQuote(zone.ZoneId, zone.Name, SalesMath.Money((free ? 0 : zone.Charge) + surcharge), free, surcharge, zone.ServicePromise);
    }

    /// <summary>Returns the name of a zone whose ranges overlap another active zone, or null.</summary>
    public static string? FindOverlap(IReadOnlyList<ZoneRule> zones)
    {
        var ranges = zones.SelectMany(z => z.Postcodes.Select(p => (z.Name, p.From, p.To))).OrderBy(r => r.From).ToList();
        for (var i = 1; i < ranges.Count; i++)
            if (ranges[i].From <= ranges[i - 1].To) return ranges[i].Name == ranges[i - 1].Name ? ranges[i].Name : $"{ranges[i - 1].Name} and {ranges[i].Name}";
        return null;
    }

    /// <summary>
    /// Credit for returning <paramref name="qty"/> more units of a line. Cumulative rounding: the credit for
    /// all units returned so far is round(lineTotal x returned / ordered), so returning the whole line in
    /// any number of steps credits exactly the line total. Capped by what is still uncredited on the line.
    /// </summary>
    public static decimal ReturnCredit(decimal lineTotal, decimal ordered, decimal previouslyReturned, decimal qty, decimal previouslyCreditedForReturns,
        decimal alreadyCreditedOnLine)
    {
        if (ordered <= 0 || qty <= 0) return 0;
        var cumulative = SalesMath.Money(lineTotal * (previouslyReturned + qty) / ordered);
        var credit = Math.Max(0, cumulative - previouslyCreditedForReturns);
        return Math.Min(credit, Math.Max(0, lineTotal - alreadyCreditedOnLine));
    }

    /// <summary>
    /// How much more may be refunded on an invoice: money the shop holds beyond what the customer still owes,
    /// minus refunds already approved or in progress. For a cancellation before issue, the whole net paid.
    /// </summary>
    public static decimal Refundable(decimal netTotal, decimal netPaid, decimal pendingRefunds, bool forCancellation)
    {
        var owed = forCancellation ? netPaid : netPaid - netTotal;
        return Math.Max(0, owed - pendingRefunds);
    }

    /// <summary>Splits a refund over receipts, newest first, never more than each receipt has left.</summary>
    public static IReadOnlyList<(Guid PaymentId, decimal Amount)> AllocateRefund(decimal amount, IEnumerable<(Guid PaymentId, decimal Remaining)> newestFirst)
    {
        var result = new List<(Guid, decimal)>();
        foreach (var (id, remaining) in newestFirst)
        {
            if (amount <= 0) break;
            if (remaining <= 0) continue;
            var take = Math.Min(amount, remaining);
            result.Add((id, take));
            amount -= take;
        }
        if (amount > 0) throw new InvalidOperationException("The refund is more than the receipts on this invoice.");
        return result;
    }

    public static PaymentStatus PaymentStatusFor(SalesOrder order) => SalesMath.PaymentStatusFor(order.NetTotal, order.NetPaid);
}
