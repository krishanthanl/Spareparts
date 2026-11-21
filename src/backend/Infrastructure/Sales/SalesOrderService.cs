using Application.Catalog;
using Application.Common;
using Application.Sales;
using Domain.Inventory;
using Domain.Sales;
using Infrastructure.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Sales;

/// <summary>
/// Orders from draft to invoice, stock issue and payment (ADR 0005). Every step that touches stock or money
/// locks the order row first, then the item rows in id order (the Phase 3 protocol), so steps on the
/// same order or the same item run one after another, also across API instances.
/// </summary>
public sealed class SalesOrderService(AppDbContext db, ICurrentActor actor, ShopTime shopTime, DeliveryService delivery) : ISalesOrderService
{
    private const string Changed = "This order was changed by someone else. Reload it and try again.";

    // ---------------- Reads ----------------

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListRequest request, CancellationToken ct)
    {
        var query = db.SalesOrders.AsNoTracking();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.PaymentStatus is { } payment) query = query.Where(x => x.PaymentStatus == payment && x.Status != SalesOrderStatus.Draft);
        if (request.FulfillmentStatus is { } fulfillment) query = query.Where(x => x.FulfillmentStatus == fulfillment && x.Status != SalesOrderStatus.Draft);
        if (request.Source is { } source) query = query.Where(x => x.Source == source);
        if (request.From is { } from)
        {
            var start = shopTime.GetUtcDayRange(from).Start;
            query = query.Where(x => (x.ConfirmedAt ?? x.CreatedAt) >= start);
        }
        if (request.To is { } to)
        {
            var end = shopTime.GetUtcDayRange(to).EndExclusive;
            query = query.Where(x => (x.ConfirmedAt ?? x.CreatedAt) < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => (x.InvoiceNumber != null && EF.Functions.ILike(x.InvoiceNumber, pattern, "\\"))
                || (x.CustomerName != null && EF.Functions.ILike(x.CustomerName, pattern, "\\"))
                || (x.CustomerPhone != null && EF.Functions.ILike(x.CustomerPhone, pattern, "\\"))
                || (x.CustomerCompany != null && EF.Functions.ILike(x.CustomerCompany, pattern, "\\")));
        }
        query = (request.Sort switch
        {
            "updatedAt" => query.OrderBy(x => x.UpdatedAt),
            "confirmedAt" => query.OrderBy(x => x.ConfirmedAt),
            "-confirmedAt" => query.OrderByDescending(x => x.ConfirmedAt),
            "number" => query.OrderBy(x => x.InvoiceNumber),
            "-number" => query.OrderByDescending(x => x.InvoiceNumber),
            "total" => query.OrderBy(x => x.Total),
            "-total" => query.OrderByDescending(x => x.Total),
            _ => query.OrderByDescending(x => x.UpdatedAt),
        }).ThenBy(x => x.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new OrderSummaryDto(x.Id, x.InvoiceNumber, x.Source, x.Status, x.PaymentStatus, x.FulfillmentStatus, x.CustomerName, x.CustomerPhone,
                x.Total, x.AmountPaid - x.RefundedTotal,
                x.Status == SalesOrderStatus.Cancelled ? x.RefundedTotal - x.AmountPaid : x.Total - x.CreditTotal - (x.AmountPaid - x.RefundedTotal),
                x.ConfirmedAt, x.UpdatedAt))
            .ToListAsync(ct);
        return new(rows, request.Page, request.PageSize, total);
    }

    public Task<OrderDto> GetAsync(Guid id, CancellationToken ct) => DtoAsync(id, ct);

    public async Task<StockIssueDto> GetIssueAsync(Guid id, CancellationToken ct)
    {
        var issue = await db.StockIssues.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Stock issue not found.");
        var skus = await SkusAsync(issue.Lines.Select(l => l.ItemId), ct);
        var names = await ActorNamesAsync([issue.CreatedBy], ct);
        return IssueDto(issue, skus, names);
    }

    public async Task<IReadOnlyList<SaleItemDto>> ItemLookupAsync(string? q, CancellationToken ct)
    {
        var items = db.Items.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = LikePattern(q);
            items = items.Where(x => EF.Functions.ILike(x.Sku, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var found = await items.OrderBy(x => x.Sku).Take(20).Select(x => new { x.Id, x.Sku, x.Name }).ToListAsync(ct);
        var ids = found.Select(x => x.Id).ToList();
        var stock = await StockAsync(ids, ct);
        var prices = await StockLedger.CurrentPrices(db, ids).ToDictionaryAsync(x => x.ItemId, x => x.SellingPrice, ct);
        return found.Select(x =>
        {
            var s = stock.GetValueOrDefault(x.Id);
            return new SaleItemDto(x.Id, x.Sku, x.Name, prices.TryGetValue(x.Id, out var p) ? p : null, s.OnHand, s.Reserved, s.OnHand - s.Reserved);
        }).ToList();
    }

    public async Task<OrderPreviewDto> PreviewAsync(OrderInput input, CancellationToken ct)
    {
        var built = await BuildLinesAsync(input, canDiscount: true, ct);
        var stock = await StockAsync(input.Lines.Select(l => l.ItemId).ToList(), ct);
        var lines = built.Lines.Select(l => new PricedLineDto(l.ItemId, l.Sku, l.Description, l.Quantity, l.ListPrice, l.UnitPrice, l.LineDiscount,
            l.OrderDiscountShare, l.LineTotal, stock.GetValueOrDefault(l.ItemId) is var s ? s.OnHand - s.Reserved : 0)).ToList();
        DeliveryQuoteDto? quote = null;
        if (input.DeliveryMethod == DeliveryMethod.Delivery && !string.IsNullOrWhiteSpace(input.DeliveryPostcode))
            quote = await delivery.QuoteAsync(new DeliveryQuoteInput(input.DeliveryPostcode, input.DeliveryIsRural, built.Priced.Total - built.Priced.DeliveryCharge), ct);
        return new OrderPreviewDto(lines, built.Priced.Subtotal, built.Priced.LineDiscountTotal, built.Priced.OrderDiscount, built.Priced.DeliveryCharge,
            built.Priced.Total, built.NeedsDiscount, quote);
    }

    // ---------------- Drafts ----------------

    public async Task<OrderDto> CreateAsync(OrderInput input, bool canDiscount, CancellationToken ct)
    {
        var order = new SalesOrder();
        db.SalesOrders.Add(order);
        await ApplyAsync(order, input, canDiscount, ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(order.Id, ct);
    }

    public async Task<OrderDto> UpdateAsync(Guid id, OrderInput input, bool canDiscount, CancellationToken ct)
    {
        var order = await db.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (order.Status != SalesOrderStatus.Draft) throw new ConflictException("Only drafts can be edited. A confirmed invoice is fixed.");
        ExpectVersion(db, order, input.Version);
        foreach (var line in order.Lines.ToList()) { db.SalesOrderLines.Remove(line); order.Lines.Remove(line); }
        await ApplyAsync(order, input, canDiscount, ct);
        db.Entry(order).State = EntityState.Modified;
        await SaveAsync(db, Changed, ct);
        return await DtoAsync(order.Id, ct);
    }

    public async Task<OrderDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await db.SalesOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        // A copy starts at today's prices with no discounts: discounting again is a fresh, permissioned decision.
        var input = new OrderInput(source.Source == SalesSource.Online ? SalesSource.Admin : source.Source, source.CustomerId, source.CustomerName,
            source.CustomerPhone, source.CustomerEmail, source.CustomerCompany, source.BillingAddress, source.PaymentTerms, source.DeliveryMethod,
            source.DeliveryAddress, source.DeliveryCharge, 0, null, source.Notes,
            source.Lines.OrderBy(l => l.LineNo).Select(l => new OrderLineInput(l.ItemId, l.Quantity)).ToList(), null, source.DeliveryPostcode,
            source.DeliveryIsRural, source.DeliveryChargeReason);
        return await CreateAsync(input, canDiscount: false, ct);
    }

    // ---------------- Confirm / cancel ----------------

    public async Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(id, ct);
        if (order.Status is SalesOrderStatus.Confirmed or SalesOrderStatus.Completed) return await DtoAsync(id, ct); // already done
        await ConfirmCoreAsync(order, ct);
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    public async Task<OrderDto> CancelAsync(Guid id, CancelOrderInput input, bool canCancelConfirmed, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(id, ct);
        if (order.Status == SalesOrderStatus.Cancelled) return await DtoAsync(id, ct);
        if (order.Status != SalesOrderStatus.Draft)
        {
            if (!canCancelConfirmed) throw new ForbiddenException("Only staff with the sales.cancel permission can cancel a confirmed invoice.");
            if (order.Status == SalesOrderStatus.Completed || await db.StockIssues.AnyAsync(x => x.OrderId == id, ct))
                throw new ConflictException("Goods have been issued on this invoice, so it cannot be cancelled. Use a return instead.");
            if (order.NetPaid != 0)
                throw new ConflictException("This invoice has payments. Void or refund them before cancelling.");
        }
        order.Status = SalesOrderStatus.Cancelled;
        order.CancelledBy = actor.ActorId;
        order.CancelReason = input.Reason.Trim();
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    // ---------------- Stock issue ----------------

    public async Task<OrderDto> IssueAsync(Guid id, IssueInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(id, ct);
        if (input.IdempotencyKey is { Length: > 0 } key && await db.StockIssues.AnyAsync(x => x.IdempotencyKey == key, ct))
        {
            if (!await db.StockIssues.AnyAsync(x => x.IdempotencyKey == key && x.OrderId == id, ct))
                throw new ConflictException("That idempotency key was already used for another order.");
            return await DtoAsync(id, ct);
        }
        await IssueCoreAsync(order, input, ct);
        await StockLedger.CommitAsync(db, transaction, ct);
        return await DtoAsync(id, ct);
    }

    // ---------------- Payments ----------------

    public async Task<OrderDto> RecordPaymentAsync(Guid id, PaymentInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(id, ct);
        if (await ReplayedPaymentAsync(input.IdempotencyKey, id, ct)) return await DtoAsync(id, ct);
        await PayCoreAsync(order, input.Method, input.Amount, input.Reference, input.IdempotencyKey, ct);
        await CommitPaymentAsync(transaction, ct);
        return await DtoAsync(id, ct);
    }

    public async Task<OrderDto> VoidPaymentAsync(Guid paymentId, VoidPaymentInput input, CancellationToken ct)
    {
        var original = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw new NotFoundException("Payment not found.");
        if (original.VoidsPaymentId is not null) throw new ConflictException("This entry is already a void.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(original.OrderId, ct);
        if (await ReplayedPaymentAsync(input.IdempotencyKey, order.Id, ct)) return await DtoAsync(order.Id, ct);
        if (await db.Payments.AnyAsync(x => x.VoidsPaymentId == paymentId, ct)) throw new ConflictException("This payment was already voided.");
        if (await db.RefundAllocations.AnyAsync(a => a.PaymentId == paymentId
                && db.Refunds.Any(r => r.Id == a.RefundId && r.Status != RefundStatus.Failed && r.Status != RefundStatus.Cancelled), ct))
            throw new ConflictException("A refund was made from this payment, so it cannot be voided.");
        if (order.Status == SalesOrderStatus.Cancelled) throw new ConflictException("This order is cancelled.");
        db.Payments.Add(new Payment
        {
            ReceiptNumber = await DocumentNumbers.NextAsync(db, "VD", actor.ActorId, ct), OrderId = order.Id, Method = original.Method,
            Amount = -original.Amount, Reference = original.Reference, IdempotencyKey = input.IdempotencyKey, VoidsPaymentId = original.Id,
            VoidReason = input.Reason.Trim(),
        });
        order.AmountPaid -= original.Amount;
        await RefreshStatusAsync(order, ct);
        await CommitPaymentAsync(transaction, ct);
        return await DtoAsync(order.Id, ct);
    }

    // ---------------- Quick sale ----------------

    public async Task<OrderDto> QuickSaleAsync(QuickSaleInput input, bool canDiscount, CancellationToken ct)
    {
        var replay = await db.Payments.AsNoTracking().Where(x => x.IdempotencyKey == input.IdempotencyKey).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(ct);
        if (replay is { } existing) return await DtoAsync(existing, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = new SalesOrder();
        db.SalesOrders.Add(order);
        await ApplyAsync(order, input.Order, canDiscount, ct);
        await db.SaveChangesAsync(ct);
        order = await LockOrderAsync(order.Id, ct);
        await ConfirmCoreAsync(order, ct);
        await db.SaveChangesAsync(ct);
        await IssueCoreAsync(order, new IssueInput(null, null), ct);
        // Save the issue so the payment's status refresh sees the issued quantities.
        await db.SaveChangesAsync(ct);
        var amount = input.Amount ?? order.Total;
        if (amount > 0) await PayCoreAsync(order, input.Method, amount, input.Reference, input.IdempotencyKey, ct);
        await CommitPaymentAsync(transaction, ct);
        return await DtoAsync(order.Id, ct);
    }

    // ---------------- Core steps (caller owns the transaction) ----------------

    private async Task ConfirmCoreAsync(SalesOrder order, CancellationToken ct)
    {
        if (order.Status != SalesOrderStatus.Draft) throw new ConflictException("Only drafts can be confirmed.");
        if (order.Lines.Count == 0) throw Invalid("Lines", "Add at least one item before confirming.");
        var ledger = new StockLedger(db);
        await ledger.LockAsync(order.Lines.Select(x => x.ItemId), ct);
        foreach (var line in order.Lines.OrderBy(x => x.LineNo))
        {
            var p = ledger[line.ItemId];
            if (p.Available < line.Quantity)
                throw new ConflictException($"Only {Math.Max(p.Available, 0):0.###} of {p.Sku} available ({p.OnHand:0.###} on hand, {p.Reserved:0.###} reserved).");
            p.Reserved += line.Quantity;
        }
        order.InvoiceNumber = await DocumentNumbers.NextAsync(db, "INV", actor.ActorId, ct);
        order.Status = SalesOrderStatus.Confirmed;
        order.ConfirmedBy = actor.ActorId;
        order.PaymentStatus = AfterSaleMath.PaymentStatusFor(order);
        order.FulfillmentStatus = FulfillmentStatus.NotIssued;
    }

    internal async Task<StockIssue> IssueCoreAsync(SalesOrder order, IssueInput input, CancellationToken ct, Guid? shipmentId = null)
    {
        if (order.Status == SalesOrderStatus.Draft) throw new ConflictException("Confirm the order before issuing stock.");
        if (order.Status == SalesOrderStatus.Cancelled) throw new ConflictException("This order is cancelled.");
        var issued = await IssuedAsync(order.Id, ct);
        var lines = order.Lines.ToDictionary(x => x.Id);
        var requested = input.Lines is { Count: > 0 }
            ? input.Lines.Select(r => (Line: lines.GetValueOrDefault(r.OrderLineId) ?? throw Invalid("Lines", "A line does not belong to this order."), r.Quantity)).ToList()
            : order.Lines.OrderBy(l => l.LineNo).Select(l => (Line: l, Quantity: l.Quantity - issued.GetValueOrDefault(l.Id))).Where(x => x.Quantity > 0).ToList();
        if (requested.Count == 0) throw new ConflictException("Everything on this order has already been issued.");
        foreach (var (line, qty) in requested)
        {
            var remaining = line.Quantity - issued.GetValueOrDefault(line.Id);
            if (qty > remaining) throw new ConflictException($"{line.Sku}: only {remaining:0.###} left to issue.");
        }

        var ledger = new StockLedger(db);
        await ledger.LockAsync(requested.Select(x => x.Line.ItemId), ct);
        var issue = new StockIssue
        {
            IssueNumber = await DocumentNumbers.NextAsync(db, "ISS", actor.ActorId, ct), OrderId = order.Id,
            IdempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey, ShipmentId = shipmentId,
        };
        db.StockIssues.Add(issue);
        foreach (var (line, qty) in requested.OrderBy(x => x.Line.LineNo))
        {
            var row = new StockIssueLine { IssueId = issue.Id, OrderLineId = line.Id, ItemId = line.ItemId, Quantity = qty };
            var movement = ledger.Remove(line.ItemId, MovementType.Issue, qty, new MovementRef("StockIssue", issue.Id, row.Id, $"issue-line:{row.Id}"));
            row.MovementId = movement.Id;
            issue.Lines.Add(row);
            db.StockIssueLines.Add(row);
            issued[line.Id] = issued.GetValueOrDefault(line.Id) + qty;
        }
        ledger.EnsureNotNegative();
        order.FulfillmentStatus = SalesMath.FulfillmentStatusFor(order.Lines.Sum(l => l.Quantity), issued.Values.Sum());
        order.Status = SalesMath.StatusFor(order.Status, order.PaymentStatus, order.FulfillmentStatus);
        return issue;
    }

    private async Task PayCoreAsync(SalesOrder order, PaymentMethod method, decimal amount, string? reference, string key, CancellationToken ct)
    {
        if (order.Status == SalesOrderStatus.Draft) throw new ConflictException("Confirm the order before taking payment.");
        if (order.Status == SalesOrderStatus.Cancelled) throw new ConflictException("This order is cancelled.");
        db.Payments.Add(new Payment
        {
            ReceiptNumber = await DocumentNumbers.NextAsync(db, "RCT", actor.ActorId, ct), OrderId = order.Id, Method = method, Amount = amount,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), IdempotencyKey = key,
        });
        order.AmountPaid += amount;
        await RefreshStatusAsync(order, ct);
    }

    internal async Task RefreshStatusAsync(SalesOrder order, CancellationToken ct)
    {
        order.PaymentStatus = AfterSaleMath.PaymentStatusFor(order);
        var issued = (await IssuedAsync(order.Id, ct)).Values.Sum();
        order.FulfillmentStatus = SalesMath.FulfillmentStatusFor(order.Lines.Sum(l => l.Quantity), issued);
        order.Status = SalesMath.StatusFor(order.Status, order.PaymentStatus, order.FulfillmentStatus);
    }

    /// <summary>True when this key already recorded a payment for this order (a retry). A key reused for another order is an error.</summary>
    private async Task<bool> ReplayedPaymentAsync(string key, Guid orderId, CancellationToken ct)
    {
        var existing = await db.Payments.AsNoTracking().Where(x => x.IdempotencyKey == key).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(ct);
        if (existing is null) return false;
        if (existing != orderId) throw new ConflictException("That idempotency key was already used for another order.");
        return true;
    }

    private async Task CommitPaymentAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await StockLedger.CommitAsync(db, transaction, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Changed);
        }
    }

    // ---------------- Building drafts ----------------

    private sealed record BuiltLine(Guid ItemId, string Sku, string Description, decimal Quantity, decimal ListPrice, decimal UnitPrice,
        decimal LineDiscount, decimal OrderDiscountShare, decimal LineTotal, string? DiscountReason);
    private sealed record Built(IReadOnlyList<BuiltLine> Lines, PricedOrder Priced, bool NeedsDiscount);

    private async Task<Built> BuildLinesAsync(OrderInput input, bool canDiscount, CancellationToken ct)
    {
        var ids = input.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => new { x.Sku, x.Name, x.IsActive }, ct);
        var prices = await StockLedger.CurrentPrices(db, ids).ToDictionaryAsync(x => x.ItemId, x => x.SellingPrice, ct);
        var lines = new List<(OrderLineInput Input, string Sku, string Name, decimal ListPrice, decimal UnitPrice)>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var l = input.Lines[i];
            if (!items.TryGetValue(l.ItemId, out var item)) throw Invalid($"Lines[{i}].ItemId", "This item does not exist.");
            if (!item.IsActive) throw Invalid($"Lines[{i}].ItemId", $"{item.Sku} is inactive and cannot be sold.");
            if (!prices.TryGetValue(l.ItemId, out var list)) throw Invalid($"Lines[{i}].ItemId", $"{item.Sku} has no selling price yet.");
            lines.Add((l, item.Sku, item.Name, list, l.UnitPrice ?? list));
        }

        var needsDiscount = input.OrderDiscount > 0 || lines.Any(l => l.UnitPrice < l.ListPrice || (l.Input.DiscountAmount ?? 0) > 0 || (l.Input.DiscountPercent ?? 0) > 0);
        if (needsDiscount && !canDiscount)
            throw new ForbiddenException("Discounts and prices below the selling price need the sales.discount permission.");
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var discounted = l.UnitPrice < l.ListPrice || (l.Input.DiscountAmount ?? 0) > 0 || (l.Input.DiscountPercent ?? 0) > 0;
            if (discounted && string.IsNullOrWhiteSpace(l.Input.DiscountReason) && string.IsNullOrWhiteSpace(input.DiscountReason))
                throw Invalid($"Lines[{i}].DiscountReason", $"Give a reason for the lower price or discount on {l.Sku}.");
        }

        PricedOrder priced;
        try
        {
            priced = SalesMath.Price(lines.Select(l => new PricedLineInput(l.Input.Quantity, l.UnitPrice, l.Input.DiscountAmount, l.Input.DiscountPercent)).ToList(),
                input.OrderDiscount, input.DeliveryCharge);
        }
        catch (ArgumentException ex)
        {
            throw Invalid("Lines", ex.Message);
        }
        var built = lines.Select((l, i) => new BuiltLine(l.Input.ItemId, l.Sku, l.Name, l.Input.Quantity, l.ListPrice, l.UnitPrice, priced.Lines[i].LineDiscount,
            priced.Lines[i].OrderDiscountShare, priced.Lines[i].LineTotal, Trim(l.Input.DiscountReason))).ToList();
        return new Built(built, priced, needsDiscount);
    }

    private async Task ApplyAsync(SalesOrder order, OrderInput input, bool canDiscount, CancellationToken ct)
    {
        var built = await BuildLinesAsync(input, canDiscount, ct);
        Customer? customer = null;
        if (input.CustomerId is { } customerId)
            customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, ct) ?? throw Invalid("CustomerId", "Customer not found.");
        // Snapshot: what the invoice shows never changes when the customer record is edited later.
        order.Source = input.Source;
        order.CustomerId = customer?.Id;
        order.CustomerName = Trim(input.CustomerName) ?? customer?.Name;
        order.CustomerPhone = Trim(input.CustomerPhone) ?? customer?.Phone;
        order.CustomerEmail = Trim(input.CustomerEmail) ?? customer?.Email;
        order.CustomerCompany = Trim(input.CustomerCompany) ?? customer?.Company;
        order.BillingAddress = Trim(input.BillingAddress) ?? customer?.BillingAddress;
        order.PaymentTerms = input.PaymentTerms;
        order.DeliveryMethod = input.DeliveryMethod;
        order.DeliveryAddress = input.DeliveryMethod == DeliveryMethod.Delivery ? Trim(input.DeliveryAddress) ?? customer?.DeliveryAddress : null;
        await ApplyDeliveryAsync(order, input, built.Priced, canDiscount, ct);
        order.DeliveryCharge = built.Priced.DeliveryCharge;
        order.Subtotal = built.Priced.Subtotal;
        order.LineDiscountTotal = built.Priced.LineDiscountTotal;
        order.OrderDiscount = built.Priced.OrderDiscount;
        order.DiscountReason = Trim(input.DiscountReason);
        order.Total = built.Priced.Total;
        order.TaxTreatment = TaxTreatment.NotRegistered;
        order.GstAmount = 0;
        order.Currency = ShopTime.Currency;
        order.Notes = Trim(input.Notes);
        var lineNo = 1;
        foreach (var l in built.Lines)
        {
            var row = new SalesOrderLine
            {
                OrderId = order.Id, LineNo = lineNo++, ItemId = l.ItemId, Sku = l.Sku, Description = l.Description, Quantity = l.Quantity,
                ListPrice = l.ListPrice, UnitPrice = l.UnitPrice, LineDiscount = l.LineDiscount, OrderDiscountShare = l.OrderDiscountShare,
                LineTotal = l.LineTotal, DiscountReason = l.DiscountReason,
            };
            order.Lines.Add(row);
            db.SalesOrderLines.Add(row); // ids are set on construction, so add explicitly (ADR 0002)
        }
    }

    /// <summary>
    /// Checks the delivery charge against the zone quote and snapshots what it was based on (ADR 0006, D4).
    /// A charge other than the quote needs a reason; charging less than the quote, or delivering outside every
    /// zone, also needs sales.discount (Admin). With no zones set up yet, the typed charge is used as in 4a.
    /// </summary>
    private async Task ApplyDeliveryAsync(SalesOrder order, OrderInput input, PricedOrder priced, bool canDiscount, CancellationToken ct)
    {
        order.DeliveryPostcode = null;
        order.DeliveryIsRural = false;
        order.DeliveryZoneName = null;
        order.DeliveryPromise = null;
        order.DeliveryChargeReason = null;
        var settings = await delivery.CurrentSettingsAsync(ct);
        if (input.DeliveryMethod == DeliveryMethod.Pickup)
        {
            if (!settings.PickupEnabled && input.Source != SalesSource.Counter) throw Invalid("DeliveryMethod", "Pickup is switched off in the delivery settings.");
            return;
        }
        if (!settings.DeliveryEnabled) throw Invalid("DeliveryMethod", "Delivery is switched off in the delivery settings.");
        var reason = Trim(input.DeliveryChargeReason);
        order.DeliveryIsRural = input.DeliveryIsRural;
        if (string.IsNullOrWhiteSpace(input.DeliveryPostcode))
        {
            if (await delivery.HasActiveZonesAsync(ct)) throw Invalid("DeliveryPostcode", "Enter the delivery postcode so the charge can be quoted.");
            order.DeliveryChargeReason = reason;
            return;
        }
        order.DeliveryPostcode = input.DeliveryPostcode.Trim();
        var quote = await delivery.QuoteAsync(new DeliveryQuoteInput(order.DeliveryPostcode, input.DeliveryIsRural, priced.Total - priced.DeliveryCharge), ct);
        if (!quote.Delivers)
        {
            if (!canDiscount) throw new ForbiddenException("We don't deliver to that postcode. Only staff with the sales.discount permission can override it.");
            if (reason is null) throw Invalid("DeliveryChargeReason", "Give a reason for delivering outside the delivery zones.");
            order.DeliveryChargeReason = reason;
            return;
        }
        order.DeliveryZoneName = quote.ZoneName;
        order.DeliveryPromise = quote.ServicePromise;
        if (priced.DeliveryCharge != quote.Charge)
        {
            if (reason is null) throw Invalid("DeliveryChargeReason", $"The quoted delivery charge is {quote.Charge:0.00}. Give a reason for charging something else.");
            if (priced.DeliveryCharge < quote.Charge && !canDiscount)
                throw new ForbiddenException("Charging less than the delivery quote needs the sales.discount permission.");
            order.DeliveryChargeReason = reason;
        }
    }

    // ---------------- Helpers ----------------

    internal async Task<SalesOrder> LockOrderAsync(Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM sales_orders WHERE \"Id\" = {id} FOR UPDATE", ct);
        var tracked = db.ChangeTracker.Entries<SalesOrder>().SingleOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(ct);
        return await db.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
    }

    private async Task<Dictionary<Guid, decimal>> IssuedAsync(Guid orderId, CancellationToken ct) =>
        await db.StockIssueLines.Where(l => db.StockIssues.Any(i => i.Id == l.IssueId && i.OrderId == orderId))
            .GroupBy(l => l.OrderLineId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    private readonly record struct Stock(decimal OnHand, decimal Reserved);

    private async Task<Dictionary<Guid, Stock>> StockAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var onHand = await db.StockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.QtyChange) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var reserved = await StockLedger.Reservations(db, ids).ToDictionaryAsync(x => x.ItemId, x => x.Quantity, ct);
        return ids.Distinct().ToDictionary(id => id, id => new Stock(onHand.GetValueOrDefault(id), reserved.GetValueOrDefault(id)));
    }

    private async Task<Dictionary<Guid, string>> SkusAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => list.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Sku, ct);
    }

    private async Task<Dictionary<string, string>> ActorNamesAsync(IEnumerable<string> actors, CancellationToken ct)
    {
        var ids = actors.Select(a => Guid.TryParse(a, out var g) ? g : (Guid?)null).Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id.ToString(), u => u.DisplayName, ct);
        return actors.Distinct().ToDictionary(a => a, a => users.GetValueOrDefault(a) ?? a);
    }

    private static StockIssueDto IssueDto(StockIssue issue, Dictionary<Guid, string> skus, Dictionary<string, string> names) =>
        new(issue.Id, issue.IssueNumber, issue.OrderId, issue.PostedAt, names.GetValueOrDefault(issue.CreatedBy) ?? issue.CreatedBy,
            issue.Lines.Select(l => new StockIssueLineDto(l.OrderLineId, l.ItemId, skus.GetValueOrDefault(l.ItemId) ?? "", l.Quantity)).ToList());

    private async Task<OrderDto> DtoAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var o = await db.SalesOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var issues = await db.StockIssues.AsNoTracking().Include(x => x.Lines).Where(x => x.OrderId == id).OrderBy(x => x.PostedAt).ThenBy(x => x.IssueNumber).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.ReceivedAt).ThenBy(x => x.ReceiptNumber).ToListAsync(ct);
        var issued = issues.SelectMany(i => i.Lines).GroupBy(l => l.OrderLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var live = o.Status is SalesOrderStatus.Draft or SalesOrderStatus.Confirmed;
        var stock = live ? await StockAsync(o.Lines.Select(l => l.ItemId).ToList(), ct) : [];
        var skus = await SkusAsync(issues.SelectMany(i => i.Lines).Select(l => l.ItemId), ct);
        var names = await ActorNamesAsync(issues.Select(i => i.CreatedBy).Concat(payments.Select(p => p.CreatedBy)).Append(o.ConfirmedBy ?? ""), ct);
        var voided = payments.Where(p => p.VoidsPaymentId is not null).Select(p => p.VoidsPaymentId!.Value).ToHashSet();
        var lines = o.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var done = issued.GetValueOrDefault(l.Id);
            decimal? available = stock.TryGetValue(l.ItemId, out var s) ? s.OnHand - s.Reserved : null;
            return new OrderLineDto(l.Id, l.LineNo, l.ItemId, l.Sku, l.Description, l.Quantity, l.ListPrice, l.UnitPrice, l.LineDiscount, l.OrderDiscountShare,
                l.LineTotal, l.DiscountReason, done, o.Status == SalesOrderStatus.Cancelled ? 0 : l.Quantity - done, available);
        }).ToList();
        return new OrderDto(o.Id, o.InvoiceNumber, o.Source, o.Status, o.PaymentStatus, o.FulfillmentStatus, o.PaymentTerms, o.CustomerId, o.CustomerName,
            o.CustomerPhone, o.CustomerEmail, o.CustomerCompany, o.BillingAddress, o.DeliveryMethod, o.DeliveryAddress, o.DeliveryCharge, o.Subtotal,
            o.LineDiscountTotal, o.OrderDiscount, o.DiscountReason, o.Total, o.AmountPaid, o.Balance,
            o.TaxTreatment, o.GstAmount, o.Currency, o.Notes, o.ConfirmedAt, o.ConfirmedBy is null ? null : names.GetValueOrDefault(o.ConfirmedBy),
            o.CancelledAt, o.CancelReason, lines, issues.Select(i => IssueDto(i, skus, names)).ToList(),
            payments.Select(p => new PaymentDto(p.Id, p.ReceiptNumber, p.OrderId, p.Method, p.Amount, p.Reference, p.ReceivedAt,
                names.GetValueOrDefault(p.CreatedBy) ?? p.CreatedBy, p.VoidsPaymentId, p.VoidReason, voided.Contains(p.Id))).ToList(),
            o.Version, o.CreatedAt, o.UpdatedAt, o.CreditTotal, o.RefundedTotal, o.DeliveryPostcode, o.DeliveryIsRural, o.DeliveryZoneName,
            o.DeliveryPromise, o.DeliveryChargeReason);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static NotFoundException NotFound() => new("Order not found.");
}
