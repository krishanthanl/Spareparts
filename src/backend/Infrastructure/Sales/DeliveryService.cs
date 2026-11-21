using Application.Common;
using Application.Sales;
using Domain.Sales;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Sales;

/// <summary>
/// Delivery settings, zones and quotes (ADR 0006, D1-D4). Quotes are calculated here and snapshotted on the
/// order; editing a zone never changes an order that was already priced.
/// </summary>
public sealed class DeliveryService(AppDbContext db) : IDeliveryService
{
    public async Task<DeliverySettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await db.DeliverySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        return s is null
            ? new DeliverySettingsDto(true, true, null, false, TaxTreatment.NotRegistered, 0, null)
            : new DeliverySettingsDto(s.DeliveryEnabled, s.PickupEnabled, s.FreeDeliveryThreshold, false, TaxTreatment.NotRegistered, s.Version, s.UpdatedAt);
    }

    internal async Task<DeliverySettings> CurrentSettingsAsync(CancellationToken ct) =>
        await db.DeliverySettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? new DeliverySettings();

    public async Task<DeliverySettingsDto> UpdateSettingsAsync(DeliverySettingsInput input, CancellationToken ct)
    {
        var s = await db.DeliverySettings.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new DeliverySettings();
            db.DeliverySettings.Add(s);
        }
        else
        {
            ExpectVersion(db, s, input.Version);
        }
        s.DeliveryEnabled = input.DeliveryEnabled;
        s.PickupEnabled = input.PickupEnabled;
        s.FreeDeliveryThreshold = input.FreeDeliveryThreshold;
        await SaveAsync(db, "The delivery settings were changed by someone else.", ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryZoneDto>> ListZonesAsync(CancellationToken ct)
    {
        var zones = await db.DeliveryZones.AsNoTracking().Include(x => x.Postcodes).OrderBy(x => x.Name).ToListAsync(ct);
        return zones.Select(Dto).ToList();
    }

    public async Task<DeliveryZoneDto> CreateZoneAsync(DeliveryZoneInput input, CancellationToken ct)
    {
        var zone = new DeliveryZone();
        db.DeliveryZones.Add(zone);
        await ApplyAsync(zone, input, ct);
        await SaveAsync(db, "A delivery zone with that name already exists.", ct);
        return await ZoneAsync(zone.Id, ct);
    }

    public async Task<DeliveryZoneDto> UpdateZoneAsync(Guid id, DeliveryZoneInput input, CancellationToken ct)
    {
        var zone = await db.DeliveryZones.Include(x => x.Postcodes).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Delivery zone not found.");
        ExpectVersion(db, zone, input.Version);
        foreach (var p in zone.Postcodes.ToList()) { db.DeliveryZonePostcodes.Remove(p); zone.Postcodes.Remove(p); }
        await ApplyAsync(zone, input, ct);
        db.Entry(zone).State = EntityState.Modified;
        await SaveAsync(db, "A delivery zone with that name already exists.", ct);
        return await ZoneAsync(id, ct);
    }

    public async Task<DeliveryQuoteDto> QuoteAsync(DeliveryQuoteInput input, CancellationToken ct)
    {
        if (!AfterSaleMath.TryParsePostcode(input.Postcode, out var postcode)) throw Invalid("Postcode", "Use a 4-digit NZ postcode.");
        var settings = await CurrentSettingsAsync(ct);
        if (!settings.DeliveryEnabled) return new DeliveryQuoteDto(false, null, null, false, 0, null, "Delivery is switched off.");
        var zone = AfterSaleMath.FindZone(await ActiveRulesAsync(null, ct), postcode);
        if (zone is null) return new DeliveryQuoteDto(false, null, null, false, 0, null, "We don't deliver to that postcode.");
        var q = AfterSaleMath.Quote(zone, input.GoodsValue, input.IsRural, settings.FreeDeliveryThreshold);
        return new DeliveryQuoteDto(true, q.ZoneName, q.Charge, q.Free, q.RuralSurcharge, q.ServicePromise, null);
    }

    internal Task<bool> HasActiveZonesAsync(CancellationToken ct) => db.DeliveryZones.AnyAsync(x => x.IsActive, ct);

    private async Task<List<ZoneRule>> ActiveRulesAsync(Guid? excluding, CancellationToken ct)
    {
        var zones = await db.DeliveryZones.AsNoTracking().Include(x => x.Postcodes).Where(x => x.IsActive && x.Id != excluding).OrderBy(x => x.Name).ToListAsync(ct);
        return zones.Select(z => new ZoneRule(z.Id, z.Name, z.Charge, z.FreeDeliveryThreshold, z.RuralSurcharge, z.ServicePromise,
            z.Postcodes.Select(p => (p.From, p.To)).ToList())).ToList();
    }

    private async Task ApplyAsync(DeliveryZone zone, DeliveryZoneInput input, CancellationToken ct)
    {
        zone.Name = input.Name.Trim();
        zone.Charge = input.Charge;
        zone.FreeDeliveryThreshold = input.FreeDeliveryThreshold;
        zone.RuralSurcharge = input.RuralSurcharge;
        zone.ServicePromise = string.IsNullOrWhiteSpace(input.ServicePromise) ? null : input.ServicePromise.Trim();
        zone.IsActive = input.IsActive;
        var ranges = input.Postcodes.Select(p =>
        {
            AfterSaleMath.TryParsePostcode(p.From, out var from);
            var to = AfterSaleMath.TryParsePostcode(p.To, out var t) ? t : from;
            return (From: from, To: to);
        }).ToList();
        foreach (var (from, to) in ranges)
        {
            var row = new DeliveryZonePostcode { ZoneId = zone.Id, From = from, To = to };
            zone.Postcodes.Add(row);
            db.DeliveryZonePostcodes.Add(row);
        }
        if (!zone.IsActive) return;
        // A postcode must belong to one zone at most, or the quote would depend on which zone is read first.
        var rules = await ActiveRulesAsync(zone.Id, ct);
        rules.Add(new ZoneRule(zone.Id, zone.Name, 0, null, 0, null, ranges));
        if (AfterSaleMath.FindOverlap(rules) is { } overlap) throw Invalid("Postcodes", $"Postcode ranges overlap ({overlap}). Each postcode can be in one active zone only.");
    }

    private async Task<DeliveryZoneDto> ZoneAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return Dto(await db.DeliveryZones.AsNoTracking().Include(x => x.Postcodes).SingleAsync(x => x.Id == id, ct));
    }

    private static DeliveryZoneDto Dto(DeliveryZone z) => new(z.Id, z.Name, z.Charge, z.FreeDeliveryThreshold, z.RuralSurcharge, z.ServicePromise, z.IsActive,
        z.Postcodes.OrderBy(p => p.From).Select(p => new PostcodeRangeDto(p.From.ToString("D4"), p.To.ToString("D4"))).ToList(), z.Version, z.UpdatedAt);
}
