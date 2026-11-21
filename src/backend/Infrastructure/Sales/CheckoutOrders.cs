using System.Security.Cryptography;
using System.Text.Json;
using Application.Checkout;
using Application.Common;
using Application.Sales;
using Domain.Sales;
using Infrastructure.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Sales;

public sealed class CheckoutOrders(AppDbContext db, SalesOrderService sales, CheckoutQuoteService quotes, TimeProvider clock) : ICheckoutOrders
{
    private static string KeyHash(Guid key) => Convert.ToHexString(SHA256.HashData(key.ToByteArray()));

    public async Task<CheckoutOrderReceipt> PlaceAsync(CheckoutOrderRequest request, CancellationToken ct)
    {
        var hash = KeyHash(request.RequestKey);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hash}, 6))", ct);
        var existing = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(x => x.CheckoutKeyHash == hash, ct);
        if (existing is not null)
        {
            if (existing.CheckoutRequestHash != fingerprint)
                throw new ConflictException("This checkout key was already used for a different order.");
            return Receipt(existing);
        }

        var ledger = new StockLedger(db);
        await ledger.LockAsync(request.Quote.Lines.Select(x => x.ItemId), ct);
        var quote = await quotes.QuoteAsync(request.Quote, ct);
        if (quote.Total <= 0)
            throw new ConflictException("Contact the shop to arrange this order.");
        if (quote.DeliveryCharge != request.AcceptedDeliveryCharge || !quote.Lines.Select(x => x.UnitPrice).SequenceEqual(request.AcceptedUnitPrices))
            throw new ConflictException("Prices or delivery charges changed. Get a new estimate before placing the order.");

        var isDelivery = request.Quote.Fulfillment == CheckoutFulfillment.Delivery;
        var order = new SalesOrder
        {
            CheckoutKeyHash = hash,
            CheckoutRequestHash = fingerprint,
            CheckoutHoldUntil = clock.GetUtcNow().AddHours(48),
        };
        db.SalesOrders.Add(order);
        await sales.ApplyAsync(order, new OrderInput(SalesSource.Online, null, request.Name, request.Phone, request.Email, null,
            request.BillingAddress, PaymentTerms.Immediate, isDelivery ? DeliveryMethod.Delivery : DeliveryMethod.Pickup,
            request.DeliveryAddress, quote.DeliveryCharge, 0, null, null,
            quote.Lines.Select(x => new OrderLineInput(x.ItemId, x.Quantity)).ToList(),
            DeliveryPostcode: isDelivery ? request.Quote.Postcode : null, DeliveryIsRural: isDelivery && request.Quote.IsRural),
            canDiscount: false, ct);
        if (!order.Lines.OrderBy(x => x.LineNo).Select(x => x.UnitPrice).SequenceEqual(request.AcceptedUnitPrices))
            throw new ConflictException("Prices changed. Get a new estimate before placing the order.");

        await StockLedger.CommitAsync(db, transaction, ct);
        return Receipt(order);
    }

    public async Task<CheckoutOrderReceipt> TrackAsync(Guid id, Guid requestKey, CancellationToken ct)
    {
        var hash = KeyHash(requestKey);
        var cutoff = clock.GetUtcNow().AddDays(-30);
        var order = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CheckoutKeyHash == hash
            && x.CheckoutHoldUntil > cutoff, ct) ?? throw new NotFoundException("Order not found.");
        var shipments = await db.Shipments.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.CreatedAt)
            .Select(x => new CheckoutShipment(x.Status, x.Carrier, x.TrackingReference)).ToListAsync(ct);
        return Receipt(order) with { Shipments = shipments };
    }

    public async Task ExpireAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE sales_orders SET "Status" = {(int)SalesOrderStatus.Cancelled}, "CancelReason" = 'Bank transfer reservation expired',
                "CancelledBy" = 'system', "UpdatedBy" = 'system', "UpdatedAt" = {now}
            WHERE "Status" = {(int)SalesOrderStatus.Draft} AND "CheckoutHoldUntil" <= {now}
            """, ct);
    }

    private static CheckoutOrderReceipt Receipt(SalesOrder order) => new(order.Id, order.Status, order.PaymentStatus,
        order.FulfillmentStatus, order.Total, order.Currency, order.CheckoutHoldUntil!.Value, order.InvoiceNumber);
}
