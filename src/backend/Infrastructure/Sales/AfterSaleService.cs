using System.Globalization;
using Application.Catalog;
using Application.Common;
using Application.Sales;
using Domain.Inventory;
using Domain.Sales;
using Infrastructure.Inventory;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Sales;

/// <summary>
/// After the sale (ADR 0006): shipments, returns, credit notes and manual refunds. Every write locks the
/// order row first (then item rows in id order, as in 4a), so steps on one invoice run one after another
/// and the return, credit and refund limits are checked against settled numbers.
/// </summary>
public sealed class AfterSaleService(AppDbContext db, ICurrentActor actor, ShopTime shopTime, ShopSettings shop, SalesOrderService orders) : IAfterSaleService
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private static readonly RefundStatus[] Live = [RefundStatus.Requested, RefundStatus.Approved, RefundStatus.Processing];

    // ======================= Order view and balance =======================

    public async Task<OrderAfterSaleDto> GetForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var balance = await BalanceAsync(orderId, ct);
        var shipments = await ShipmentDtosAsync(db.Shipments.Where(x => x.OrderId == orderId), ct);
        var returns = await ReturnDtosAsync(db.SalesReturns.Where(x => x.OrderId == orderId), ct);
        var credits = await CreditDtosAsync(db.CreditNotes.Where(x => x.OrderId == orderId), ct);
        var refunds = await RefundDtosAsync(db.Refunds.Where(x => x.OrderId == orderId), ct);
        return new OrderAfterSaleDto(balance, shipments, await ReturnableAsync(orderId, ct), returns, credits, refunds);
    }

    public async Task<InvoiceBalanceDto> BalanceAsync(Guid orderId, CancellationToken ct)
    {
        var o = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new NotFoundException("Order not found.");
        var pending = await PendingRefundsAsync(orderId, null, ct);
        var issued = await db.StockIssues.AnyAsync(x => x.OrderId == orderId, ct);
        var cancellable = o.Status == SalesOrderStatus.Confirmed && !issued;
        return new InvoiceBalanceDto(o.Id, o.InvoiceNumber, o.Status, o.Total, o.CreditTotal, o.NetTotal, o.AmountPaid, o.RefundedTotal, o.NetPaid,
            Math.Max(0, o.Balance), Math.Max(0, -o.Balance), pending,
            o.Status == SalesOrderStatus.Draft ? 0 : AfterSaleMath.Refundable(o.NetTotal, o.NetPaid, pending, o.Status == SalesOrderStatus.Cancelled),
            cancellable || o.Status == SalesOrderStatus.Cancelled ? AfterSaleMath.Refundable(o.NetTotal, o.NetPaid, pending, forCancellation: true) : 0,
            cancellable && o.NetPaid == 0);
    }

    // ======================= Shipments (D5) =======================

    public async Task<PagedResult<ShipmentDto>> ListShipmentsAsync(ShipmentListRequest request, CancellationToken ct)
    {
        var query = db.Shipments.AsQueryable();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var p = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.ShipmentNumber, p, "\\") || (x.TrackingReference != null && EF.Functions.ILike(x.TrackingReference, p, "\\"))
                || db.SalesOrders.Any(o => o.Id == x.OrderId && ((o.InvoiceNumber != null && EF.Functions.ILike(o.InvoiceNumber, p, "\\"))
                    || (o.CustomerName != null && EF.Functions.ILike(o.CustomerName, p, "\\")))));
        }
        var total = await query.CountAsync(ct);
        var page = query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        return new(await ShipmentDtosAsync(page, ct), request.Page, request.PageSize, total);
    }

    public async Task<ShipmentDto> GetShipmentAsync(Guid id, CancellationToken ct) =>
        (await ShipmentDtosAsync(db.Shipments.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw new NotFoundException("Shipment not found.");

    public async Task<ShipmentDto> CreateShipmentAsync(Guid orderId, ShipmentInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        RequireLive(order);
        if (order.DeliveryMethod != DeliveryMethod.Delivery) throw new ConflictException("This order is for pickup. Use Issue stock when the customer collects it.");
        var open = await OpenToShipAsync(order, ct);
        var lines = order.Lines.ToDictionary(x => x.Id);
        var requested = input.Lines is { Count: > 0 }
            ? input.Lines.Select(r => (Line: lines.GetValueOrDefault(r.OrderLineId) ?? throw Invalid("Lines", "A line does not belong to this order."), r.Quantity)).ToList()
            : order.Lines.OrderBy(l => l.LineNo).Select(l => (Line: l, Quantity: open.GetValueOrDefault(l.Id))).Where(x => x.Quantity > 0).ToList();
        if (requested.Count == 0) throw new ConflictException("Everything on this order has already been issued or is on a pending shipment.");
        foreach (var (line, qty) in requested)
            if (qty > open.GetValueOrDefault(line.Id))
                throw new ConflictException($"{line.Sku}: only {open.GetValueOrDefault(line.Id):0.###} left to ship.");

        var shipment = new Shipment
        {
            ShipmentNumber = await DocumentNumbers.NextAsync(db, "SHP", actor.ActorId, ct), OrderId = order.Id, Carrier = Trim(input.Carrier),
            TrackingReference = Trim(input.TrackingReference), Notes = Trim(input.Notes),
        };
        db.Shipments.Add(shipment);
        foreach (var (line, qty) in requested.OrderBy(x => x.Line.LineNo))
        {
            var row = new ShipmentLine { ShipmentId = shipment.Id, OrderLineId = line.Id, Quantity = qty };
            shipment.Lines.Add(row);
            db.ShipmentLines.Add(row);
        }
        await StockLedger.CommitAsync(db, transaction, ct);
        return await GetShipmentAsync(shipment.Id, ct);
    }

    /// <summary>Goods leave the shop: posts the stock issue for the shipment's lines, exactly once.</summary>
    public async Task<ShipmentDto> DispatchAsync(Guid id, DispatchInput input, CancellationToken ct)
    {
        var orderId = await db.Shipments.Where(x => x.Id == id).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Shipment not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        var shipment = await db.Shipments.Include(x => x.Lines).SingleAsync(x => x.Id == id, ct);
        if (shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.Delivered) return await GetShipmentAsync(id, ct); // retry or double click
        if (shipment.Status == ShipmentStatus.Cancelled) throw new ConflictException("This shipment was cancelled.");
        RequireLive(order);
        if (await db.StockIssues.AnyAsync(x => x.IdempotencyKey == input.IdempotencyKey, ct))
            throw new ConflictException("That idempotency key was already used.");
        var issue = await orders.IssueCoreAsync(order,
            new IssueInput(shipment.Lines.Select(l => new IssueLineInput(l.OrderLineId, l.Quantity)).ToList(), input.IdempotencyKey), ct, shipment.Id);
        shipment.StockIssueId = issue.Id;
        shipment.Status = ShipmentStatus.Dispatched;
        shipment.DispatchedBy = actor.ActorId;
        if (Trim(input.Carrier) is { } carrier) shipment.Carrier = carrier;
        if (Trim(input.TrackingReference) is { } tracking) shipment.TrackingReference = tracking;
        await StockLedger.CommitAsync(db, transaction, ct);
        return await GetShipmentAsync(id, ct);
    }

    /// <summary>Records arrival only. Stock already left at dispatch, so nothing moves here.</summary>
    public async Task<ShipmentDto> DeliverAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Shipment not found.");
        if (shipment.Status == ShipmentStatus.Delivered) return await GetShipmentAsync(id, ct);
        if (shipment.Status != ShipmentStatus.Dispatched) throw new ConflictException("Dispatch the shipment before marking it delivered.");
        shipment.Status = ShipmentStatus.Delivered;
        shipment.DeliveredBy = actor.ActorId;
        await SaveAsync(db, "This shipment was changed by someone else.", ct);
        return await GetShipmentAsync(id, ct);
    }

    public async Task<ShipmentDto> CancelShipmentAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Shipment not found.");
        if (shipment.Status == ShipmentStatus.Cancelled) return await GetShipmentAsync(id, ct);
        if (shipment.Status != ShipmentStatus.Pending) throw new ConflictException("The goods have left the shop. Use a return if they come back.");
        shipment.Status = ShipmentStatus.Cancelled;
        await SaveAsync(db, "This shipment was changed by someone else.", ct);
        return await GetShipmentAsync(id, ct);
    }

    /// <summary>Per order line: still to issue, minus what pending shipments are already holding.</summary>
    private async Task<Dictionary<Guid, decimal>> OpenToShipAsync(SalesOrder order, CancellationToken ct)
    {
        var issued = await IssuedAsync(order.Id, ct);
        var pending = await db.ShipmentLines.Where(l => db.Shipments.Any(s => s.Id == l.ShipmentId && s.OrderId == order.Id && s.Status == ShipmentStatus.Pending))
            .GroupBy(l => l.OrderLineId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        return order.Lines.ToDictionary(l => l.Id, l => l.Quantity - issued.GetValueOrDefault(l.Id) - pending.GetValueOrDefault(l.Id));
    }

    // ======================= Returns (D6) =======================

    public async Task<PagedResult<SalesReturnDto>> ListReturnsAsync(ReturnListRequest request, CancellationToken ct)
    {
        var query = db.SalesReturns.AsQueryable();
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.From is { } from) { var s = shopTime.GetUtcDayRange(from).Start; query = query.Where(x => x.PostedAt >= s); }
        if (request.To is { } to) { var e = shopTime.GetUtcDayRange(to).EndExclusive; query = query.Where(x => x.PostedAt < e); }
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var p = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.ReturnNumber, p, "\\")
                || db.SalesOrders.Any(o => o.Id == x.OrderId && ((o.InvoiceNumber != null && EF.Functions.ILike(o.InvoiceNumber, p, "\\"))
                    || (o.CustomerName != null && EF.Functions.ILike(o.CustomerName, p, "\\")))));
        }
        var total = await query.CountAsync(ct);
        var page = query.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        return new(await ReturnDtosAsync(page, ct), request.Page, request.PageSize, total);
    }

    public async Task<SalesReturnDto> GetReturnAsync(Guid id, CancellationToken ct) =>
        (await ReturnDtosAsync(db.SalesReturns.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw new NotFoundException("Return not found.");

    /// <summary>
    /// Takes goods back. Restocked lines post a Return movement at the cost they were issued at, so stock value
    /// reverses exactly; written-off lines post nothing. Always issues the matching credit note in the same transaction.
    /// </summary>
    public async Task<SalesReturnDto> CreateReturnAsync(Guid orderId, SalesReturnInput input, CancellationToken ct)
    {
        if (await ReplayAsync(db.SalesReturns, input.IdempotencyKey, orderId, ct) is { } replay) return await GetReturnAsync(replay, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        if (await ReplayAsync(db.SalesReturns, input.IdempotencyKey, orderId, ct) is { } raced) return await GetReturnAsync(raced, ct);
        RequireLive(order);
        var lines = order.Lines.ToDictionary(x => x.Id);
        var issued = await IssuedAsync(orderId, ct);
        var returned = await ReturnedAsync(orderId, ct);
        var requested = input.Lines.Select(r => (Line: lines.GetValueOrDefault(r.OrderLineId) ?? throw Invalid("Lines", "A line does not belong to this order."), r)).ToList();
        foreach (var (line, r) in requested)
        {
            var left = issued.GetValueOrDefault(line.Id) - returned.GetValueOrDefault(line.Id);
            if (r.Quantity > left)
                throw new ConflictException(left <= 0 ? $"{line.Sku}: nothing issued is left to return." : $"{line.Sku}: only {left:0.###} can be returned.");
        }

        var costs = await IssueCostsAsync(orderId, ct);
        var credited = await CreditedAsync(orderId, ct);
        var ledger = new StockLedger(db);
        await ledger.LockAsync(requested.Select(x => x.Line.ItemId), ct);
        var ret = new SalesReturn
        {
            ReturnNumber = await DocumentNumbers.NextAsync(db, "RTN", actor.ActorId, ct), OrderId = orderId, Reason = input.Reason.Trim(),
            IdempotencyKey = input.IdempotencyKey,
        };
        db.SalesReturns.Add(ret);
        var creditLines = new List<CreditNoteLine>();
        foreach (var (line, r) in requested.OrderBy(x => x.Line.LineNo))
        {
            var row = new SalesReturnLine
            {
                ReturnId = ret.Id, OrderLineId = line.Id, ItemId = line.ItemId, Quantity = r.Quantity, Disposition = r.Disposition,
                UnitCost = costs.GetValueOrDefault(line.Id),
            };
            if (r.Disposition == ReturnDisposition.Restock)
                row.MovementId = ledger.Receive(line.ItemId, MovementType.Return, r.Quantity, row.UnitCost,
                    new MovementRef("SalesReturn", ret.Id, row.Id, $"return-line:{row.Id}")).Id;
            ret.Lines.Add(row);
            db.SalesReturnLines.Add(row);

            var c = credited.GetValueOrDefault(line.Id);
            var amount = AfterSaleMath.ReturnCredit(line.LineTotal, line.Quantity, returned.GetValueOrDefault(line.Id), r.Quantity, c.ForReturns, c.All);
            if (amount > 0) creditLines.Add(new CreditNoteLine { OrderLineId = line.Id, Description = $"{line.Sku} {line.Description}", Quantity = r.Quantity, Amount = amount });
        }
        if (creditLines.Count > 0)
            await AddCreditNoteAsync(order, $"Return {ret.ReturnNumber}: {ret.Reason}", creditLines, ret.Id, null, ct);
        await orders.RefreshStatusAsync(order, ct);
        await StockLedger.CommitAsync(db, transaction, ct);
        return await GetReturnAsync(ret.Id, ct);
    }

    private async Task<IReadOnlyList<ReturnableLineDto>> ReturnableAsync(Guid orderId, CancellationToken ct)
    {
        var lines = await db.SalesOrderLines.AsNoTracking().Where(x => x.OrderId == orderId).OrderBy(x => x.LineNo).ToListAsync(ct);
        var issued = await IssuedAsync(orderId, ct);
        var returned = await ReturnedAsync(orderId, ct);
        var credited = await CreditedAsync(orderId, ct);
        return lines.Select(l => new ReturnableLineDto(l.Id, l.LineNo, l.Sku, l.Description, l.Quantity, issued.GetValueOrDefault(l.Id),
            returned.GetValueOrDefault(l.Id), issued.GetValueOrDefault(l.Id) - returned.GetValueOrDefault(l.Id), l.LineTotal, credited.GetValueOrDefault(l.Id).All)).ToList();
    }

    // ======================= Credit notes (D7, D8) =======================

    public async Task<PagedResult<CreditNoteDto>> ListCreditNotesAsync(CreditNoteListRequest request, CancellationToken ct)
    {
        var query = db.CreditNotes.AsQueryable();
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (request.From is { } from) { var s = shopTime.GetUtcDayRange(from).Start; query = query.Where(x => x.IssuedAt >= s); }
        if (request.To is { } to) { var e = shopTime.GetUtcDayRange(to).EndExclusive; query = query.Where(x => x.IssuedAt < e); }
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var p = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.CreditNoteNumber, p, "\\")
                || db.SalesOrders.Any(o => o.Id == x.OrderId && ((o.InvoiceNumber != null && EF.Functions.ILike(o.InvoiceNumber, p, "\\"))
                    || (o.CustomerName != null && EF.Functions.ILike(o.CustomerName, p, "\\")))));
        }
        var total = await query.CountAsync(ct);
        var page = query.OrderByDescending(x => x.IssuedAt).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        return new(await CreditDtosAsync(page, ct), request.Page, request.PageSize, total);
    }

    public async Task<CreditNoteDto> GetCreditNoteAsync(Guid id, CancellationToken ct) =>
        (await CreditDtosAsync(db.CreditNotes.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw new NotFoundException("Credit note not found.");

    /// <summary>A price adjustment or goodwill credit with no goods returned, optionally including delivery.</summary>
    public async Task<CreditNoteDto> CreateCreditNoteAsync(Guid orderId, CreditNoteInput input, CancellationToken ct)
    {
        if (await ReplayAsync(db.CreditNotes, input.IdempotencyKey, orderId, ct) is { } replay) return await GetCreditNoteAsync(replay, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        if (await ReplayAsync(db.CreditNotes, input.IdempotencyKey, orderId, ct) is { } raced) return await GetCreditNoteAsync(raced, ct);
        RequireLive(order);
        var lines = order.Lines.ToDictionary(x => x.Id);
        var credited = await CreditedAsync(orderId, ct);
        var rows = new List<CreditNoteLine>();
        foreach (var r in input.Lines.OrderBy(r => lines.GetValueOrDefault(r.OrderLineId)?.LineNo ?? 0))
        {
            var line = lines.GetValueOrDefault(r.OrderLineId) ?? throw Invalid("Lines", "A line does not belong to this order.");
            var left = line.LineTotal - credited.GetValueOrDefault(line.Id).All;
            if (r.Amount > left) throw new ConflictException($"{line.Sku}: at most {left:0.00} can still be credited.");
            rows.Add(new CreditNoteLine { OrderLineId = line.Id, Description = $"{line.Sku} {line.Description}", Quantity = 0, Amount = r.Amount });
        }
        if (input.DeliveryAmount > 0)
        {
            var deliveryLeft = order.DeliveryCharge - await DeliveryCreditedAsync(orderId, ct);
            if (input.DeliveryAmount > deliveryLeft) throw new ConflictException($"At most {deliveryLeft:0.00} of the delivery charge can still be credited.");
            rows.Add(new CreditNoteLine { OrderLineId = null, Description = "Delivery charge", Quantity = 0, Amount = input.DeliveryAmount });
        }
        var note = await AddCreditNoteAsync(order, input.Reason.Trim(), rows, null, input.IdempotencyKey, ct);
        await orders.RefreshStatusAsync(order, ct);
        await StockLedger.CommitAsync(db, transaction, ct);
        return await GetCreditNoteAsync(note.Id, ct);
    }

    private async Task<CreditNote> AddCreditNoteAsync(SalesOrder order, string reason, List<CreditNoteLine> rows, Guid? returnId, string? key, CancellationToken ct)
    {
        var note = new CreditNote
        {
            CreditNoteNumber = await DocumentNumbers.NextAsync(db, "CN", actor.ActorId, ct), OrderId = order.Id, SalesReturnId = returnId,
            Reason = reason.Length > 500 ? reason[..500] : reason, Total = rows.Sum(r => r.Amount), TaxTreatment = TaxTreatment.NotRegistered, GstAmount = 0,
            Currency = ShopTime.Currency, IdempotencyKey = key,
        };
        db.CreditNotes.Add(note);
        foreach (var row in rows)
        {
            row.CreditNoteId = note.Id;
            note.Lines.Add(row);
            db.CreditNoteLines.Add(row);
        }
        order.CreditTotal += note.Total;
        if (order.CreditTotal > order.Total) throw new ConflictException("Credits cannot exceed the invoice total.");
        return note;
    }

    // ======================= Refunds (D9, D10) =======================

    public async Task<PagedResult<RefundDto>> ListRefundsAsync(RefundListRequest request, CancellationToken ct)
    {
        var query = db.Refunds.AsQueryable();
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.OrderId is { } orderId) query = query.Where(x => x.OrderId == orderId);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var p = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.RefundNumber, p, "\\")
                || db.SalesOrders.Any(o => o.Id == x.OrderId && ((o.InvoiceNumber != null && EF.Functions.ILike(o.InvoiceNumber, p, "\\"))
                    || (o.CustomerName != null && EF.Functions.ILike(o.CustomerName, p, "\\")))));
        }
        var total = await query.CountAsync(ct);
        var page = query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        return new(await RefundDtosAsync(page, ct), request.Page, request.PageSize, total);
    }

    public async Task<RefundDto> GetRefundAsync(Guid id, CancellationToken ct) =>
        (await RefundDtosAsync(db.Refunds.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw new NotFoundException("Refund not found.");

    public async Task<RefundDto> RequestRefundAsync(Guid orderId, RefundInput input, CancellationToken ct)
    {
        if (await ReplayAsync(db.Refunds, input.IdempotencyKey, orderId, ct) is { } replay) return await GetRefundAsync(replay, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        if (await ReplayAsync(db.Refunds, input.IdempotencyKey, orderId, ct) is { } raced) return await GetRefundAsync(raced, ct);
        if (order.Status == SalesOrderStatus.Draft) throw new ConflictException("A draft has no payments to refund.");
        var cancelling = input.ForCancellation || order.Status == SalesOrderStatus.Cancelled;
        if (input.ForCancellation && (order.Status != SalesOrderStatus.Confirmed || await db.StockIssues.AnyAsync(x => x.OrderId == orderId, ct)))
            throw new ConflictException("Only invoices with nothing issued can be refunded for cancellation. Use a return instead.");
        if (input.CreditNoteId is { } cn && !await db.CreditNotes.AnyAsync(x => x.Id == cn && x.OrderId == orderId, ct))
            throw Invalid("CreditNoteId", "That credit note is not on this invoice.");
        var refundable = AfterSaleMath.Refundable(order.NetTotal, order.NetPaid, await PendingRefundsAsync(orderId, null, ct), cancelling);
        if (input.Amount > refundable)
            throw new ConflictException(refundable <= 0 ? "Nothing is owed to the customer on this invoice." : $"At most {refundable:0.00} can be refunded.");

        var refund = new Refund
        {
            RefundNumber = await DocumentNumbers.NextAsync(db, "RFD", actor.ActorId, ct), OrderId = orderId, CreditNoteId = input.CreditNoteId,
            Amount = input.Amount, Method = input.Method, Reason = input.Reason.Trim(), RequestedBy = actor.ActorId, IdempotencyKey = input.IdempotencyKey,
            TaxTreatment = TaxTreatment.NotRegistered, Currency = ShopTime.Currency,
        };
        db.Refunds.Add(refund);
        foreach (var (paymentId, amount) in AfterSaleMath.AllocateRefund(input.Amount, await RefundableReceiptsAsync(orderId, ct)))
        {
            var row = new RefundAllocation { RefundId = refund.Id, PaymentId = paymentId, Amount = amount };
            refund.Allocations.Add(row);
            db.RefundAllocations.Add(row);
        }
        await StockLedger.CommitAsync(db, transaction, ct);
        return await GetRefundAsync(refund.Id, ct);
    }

    public async Task<RefundDto> ApproveRefundAsync(Guid id, CancellationToken ct)
    {
        var (transaction, order, refund) = await LockRefundAsync(id, ct);
        await using var _ = transaction;
        if (refund.Status == RefundStatus.Approved) return await GetRefundAsync(id, ct);
        if (refund.Status != RefundStatus.Requested) throw new ConflictException("Only requested refunds can be approved.");
        if (refund.RequestedBy == actor.ActorId) throw new ForbiddenException("You cannot approve a refund you requested. Ask another admin.");
        // Payments may have been voided since the request: check the limit again, leaving this refund out.
        var cancelling = order.Status == SalesOrderStatus.Cancelled || (order.Status == SalesOrderStatus.Confirmed && !await db.StockIssues.AnyAsync(x => x.OrderId == order.Id, ct));
        var refundable = AfterSaleMath.Refundable(order.NetTotal, order.NetPaid, await PendingRefundsAsync(order.Id, refund.Id, ct), cancelling);
        if (refund.Amount > refundable) throw new ConflictException($"Only {refundable:0.00} can be refunded now. Cancel this request and make a new one.");
        refund.Status = RefundStatus.Approved;
        refund.ApprovedBy = actor.ActorId;
        await CommitAsync(transaction, ct);
        return await GetRefundAsync(id, ct);
    }

    public async Task<RefundDto> CompleteRefundAsync(Guid id, CompleteRefundInput input, CancellationToken ct)
    {
        var (transaction, order, refund) = await LockRefundAsync(id, ct);
        await using var _ = transaction;
        if (refund.Status == RefundStatus.Succeeded)
        {
            if (refund.CompletionKey == input.IdempotencyKey) return await GetRefundAsync(id, ct);
            throw new ConflictException("This refund was already paid out.");
        }
        if (refund.Status is not (RefundStatus.Approved or RefundStatus.Processing)) throw new ConflictException("Only approved refunds can be paid out.");
        if (await db.Refunds.AnyAsync(x => x.CompletionKey == input.IdempotencyKey, ct)) throw new ConflictException("That idempotency key was already used.");
        refund.Status = RefundStatus.Succeeded;
        refund.Method = input.Method;
        refund.EvidenceReference = input.EvidenceReference.Trim();
        refund.CompletionKey = input.IdempotencyKey;
        refund.CompletedBy = actor.ActorId;
        refund.CompletedAt = DateTimeOffset.UtcNow; // replaced by the database clock; set so the evidence check passes
        order.RefundedTotal += refund.Amount;
        await orders.RefreshStatusAsync(order, ct);
        await CommitAsync(transaction, ct);
        return await GetRefundAsync(id, ct);
    }

    public async Task<RefundDto> FailRefundAsync(Guid id, RefundReasonInput input, CancellationToken ct)
    {
        var (transaction, _, refund) = await LockRefundAsync(id, ct);
        await using var t = transaction;
        if (refund.Status == RefundStatus.Failed) return await GetRefundAsync(id, ct);
        if (refund.Status is not (RefundStatus.Approved or RefundStatus.Processing)) throw new ConflictException("Only approved refunds can be marked as failed.");
        refund.Status = RefundStatus.Failed;
        refund.FailureReason = input.Reason.Trim();
        refund.CompletedBy = actor.ActorId;
        await CommitAsync(transaction, ct);
        return await GetRefundAsync(id, ct);
    }

    public async Task<RefundDto> CancelRefundAsync(Guid id, RefundReasonInput input, CancellationToken ct)
    {
        var (transaction, _, refund) = await LockRefundAsync(id, ct);
        await using var t = transaction;
        if (refund.Status == RefundStatus.Cancelled) return await GetRefundAsync(id, ct);
        if (refund.Status is not (RefundStatus.Requested or RefundStatus.Approved)) throw new ConflictException("This refund can no longer be cancelled.");
        refund.Status = RefundStatus.Cancelled;
        refund.FailureReason = input.Reason.Trim();
        refund.CompletedBy = actor.ActorId;
        await CommitAsync(transaction, ct);
        return await GetRefundAsync(id, ct);
    }

    private async Task<(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction, SalesOrder, Refund)> LockRefundAsync(Guid id, CancellationToken ct)
    {
        var orderId = await db.Refunds.Where(x => x.Id == id).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Refund not found.");
        var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.LockOrderAsync(orderId, ct);
        var refund = await db.Refunds.SingleAsync(x => x.Id == id, ct);
        return (transaction, order, refund);
    }

    /// <summary>Refunds that still hold money: requested, approved or in progress. Optionally leaves one out.</summary>
    private async Task<decimal> PendingRefundsAsync(Guid orderId, Guid? excluding, CancellationToken ct) =>
        await db.Refunds.Where(x => x.OrderId == orderId && Live.Contains(x.Status) && x.Id != excluding).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;

    /// <summary>Receipts that were not voided, newest first, with what is left after live and succeeded refunds.</summary>
    private async Task<List<(Guid, decimal)>> RefundableReceiptsAsync(Guid orderId, CancellationToken ct)
    {
        var receipts = await db.Payments.Where(p => p.OrderId == orderId && p.VoidsPaymentId == null && !db.Payments.Any(v => v.VoidsPaymentId == p.Id))
            .OrderByDescending(p => p.ReceivedAt).ThenByDescending(p => p.ReceiptNumber).Select(p => new { p.Id, p.Amount }).ToListAsync(ct);
        var ids = receipts.Select(r => r.Id).ToList();
        var used = await db.RefundAllocations.Where(a => ids.Contains(a.PaymentId)
                && db.Refunds.Any(r => r.Id == a.RefundId && r.Status != RefundStatus.Failed && r.Status != RefundStatus.Cancelled))
            .GroupBy(a => a.PaymentId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        return receipts.Select(r => (r.Id, r.Amount - used.GetValueOrDefault(r.Id))).ToList();
    }

    private async Task CommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await StockLedger.CommitAsync(db, transaction, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This refund was changed by someone else. Reload it and try again.");
        }
    }

    // ======================= Credit note PDF =======================

    public async Task<(string FileName, byte[] Content)> CreditNotePdfAsync(Guid id, CancellationToken ct)
    {
        var note = await db.CreditNotes.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Credit note not found.");
        var o = await db.SalesOrders.AsNoTracking().SingleAsync(x => x.Id == note.OrderId, ct);
        const double left = 40, right = SimplePdf.PageWidth - 40;
        var pdf = new SimplePdf();
        double y = 50;
        pdf.Text(left, y, shop.TradingName, 18, bold: true);
        pdf.TextRight(right, y, "CREDIT NOTE", 20, bold: true);
        y += 18;
        var infoY = y;
        foreach (var d in new[] { shop.Address, shop.Phone, shop.Email }.Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => s!.Split('\n')))
        {
            pdf.Text(left, y, d.Trim(), 9, gray: 0.25);
            y += 12;
        }
        pdf.TextRight(right, infoY, "Credit note no. " + note.CreditNoteNumber, 10, bold: true);
        pdf.TextRight(right, infoY + 14, "Date " + shopTime.ToLocal(note.IssuedAt).ToString("d MMM yyyy", Nz), 9);
        pdf.TextRight(right, infoY + 27, "Against invoice " + o.InvoiceNumber, 9);
        y = Math.Max(y, infoY + 46);
        pdf.Text(left, y, "Not GST registered - no GST charged", 9, bold: true);
        y += 20;
        pdf.Text(left, y, "Customer", 9, bold: true, gray: 0.35);
        y += 13;
        foreach (var s in new[] { o.CustomerName ?? "Cash sale", o.CustomerCompany, o.BillingAddress }.Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => SimplePdf.Wrap(s!, 300, 9)))
        {
            pdf.Text(left, y, s, 9);
            y += 12;
        }
        y += 8;
        foreach (var line in SimplePdf.Wrap("Reason: " + note.Reason, right - left, 9)) { pdf.Text(left, y, line, 9); y += 12; }
        y += 8;
        pdf.FillRect(left, y, right - left, 18);
        pdf.Text(left + 4, y + 12, "Description", 9, bold: true);
        pdf.TextRight(400, y + 12, "Qty returned", 9, bold: true);
        pdf.TextRight(right - 4, y + 12, "Credit", 9, bold: true);
        y += 22;
        foreach (var l in note.Lines)
        {
            if (y > SimplePdf.PageHeight - 120) { pdf.NewPage(); y = 50; }
            pdf.Text(left + 4, y + 10, SimplePdf.Fit(l.Description, 300, 9), 9);
            pdf.TextRight(400, y + 10, l.Quantity == 0 ? "" : l.Quantity.ToString("0.###", CultureInfo.InvariantCulture), 9);
            pdf.TextRight(right - 4, y + 10, Money(l.Amount), 9);
            y += 16;
            pdf.Line(left, y, right, y, 0.3, 0.85);
        }
        y += 18;
        pdf.TextRight(400, y, "Total credit NZD", 11, bold: true);
        pdf.TextRight(right - 4, y, Money(note.Total), 11, bold: true);
        y += 18;
        pdf.Text(left, y, "This credit reduces the amount owing on the invoice. It is not a cash refund.", 8, gray: 0.3);
        return ($"{note.CreditNoteNumber}.pdf", pdf.Build());
    }

    // ======================= Shared queries =======================

    private static void RequireLive(SalesOrder order)
    {
        if (order.Status == SalesOrderStatus.Draft) throw new ConflictException("Confirm the order first.");
        if (order.Status == SalesOrderStatus.Cancelled) throw new ConflictException("This order is cancelled.");
    }

    /// <summary>Existing document id for this idempotency key on this order; a key used on another order is an error.</summary>
    private static async Task<Guid?> ReplayAsync<T>(IQueryable<T> set, string? key, Guid orderId, CancellationToken ct) where T : class
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var hit = await set.AsNoTracking().Where(x => EF.Property<string>(x, "IdempotencyKey") == key)
            .Select(x => new { Id = EF.Property<Guid>(x, "Id"), OrderId = EF.Property<Guid>(x, "OrderId") }).SingleOrDefaultAsync(ct);
        if (hit is null) return null;
        if (hit.OrderId != orderId) throw new ConflictException("That idempotency key was already used for another order.");
        return hit.Id;
    }

    private async Task<Dictionary<Guid, decimal>> IssuedAsync(Guid orderId, CancellationToken ct) =>
        await db.StockIssueLines.Where(l => db.StockIssues.Any(i => i.Id == l.IssueId && i.OrderId == orderId))
            .GroupBy(l => l.OrderLineId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    private async Task<Dictionary<Guid, decimal>> ReturnedAsync(Guid orderId, CancellationToken ct) =>
        await db.SalesReturnLines.Where(l => db.SalesReturns.Any(r => r.Id == l.ReturnId && r.OrderId == orderId))
            .GroupBy(l => l.OrderLineId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    /// <summary>Average unit cost each order line was issued at (quantity-weighted over its Issue movements).</summary>
    private async Task<Dictionary<Guid, decimal>> IssueCostsAsync(Guid orderId, CancellationToken ct)
    {
        var rows = await (from l in db.StockIssueLines
                          join i in db.StockIssues on l.IssueId equals i.Id
                          join m in db.StockMovements on l.MovementId equals m.Id
                          where i.OrderId == orderId
                          select new { l.OrderLineId, l.Quantity, m.UnitCost }).ToListAsync(ct);
        return rows.GroupBy(r => r.OrderLineId).ToDictionary(g => g.Key, g => SalesMath.Money(g.Sum(r => r.Quantity * r.UnitCost) / g.Sum(r => r.Quantity)));
    }

    private readonly record struct Credited(decimal All, decimal ForReturns);

    private async Task<Dictionary<Guid, Credited>> CreditedAsync(Guid orderId, CancellationToken ct)
    {
        var rows = await db.CreditNoteLines.Where(l => l.OrderLineId != null && db.CreditNotes.Any(c => c.Id == l.CreditNoteId && c.OrderId == orderId))
            .Select(l => new { l.OrderLineId, l.Amount, l.Quantity }).ToListAsync(ct);
        return rows.GroupBy(r => r.OrderLineId!.Value).ToDictionary(g => g.Key, g => new Credited(g.Sum(r => r.Amount), g.Where(r => r.Quantity > 0).Sum(r => r.Amount)));
    }

    private async Task<decimal> DeliveryCreditedAsync(Guid orderId, CancellationToken ct) =>
        await db.CreditNoteLines.Where(l => l.OrderLineId == null && db.CreditNotes.Any(c => c.Id == l.CreditNoteId && c.OrderId == orderId))
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0;

    private async Task<Dictionary<Guid, (string? Invoice, string? Customer, string? Address)>> OrderInfoAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.SalesOrders.AsNoTracking().Where(o => list.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => (o.InvoiceNumber, o.CustomerName, o.DeliveryAddress), ct);
    }

    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string?> actors, CancellationToken ct)
    {
        var all = actors.Where(a => a is not null).Select(a => a!).Distinct().ToList();
        var ids = all.Select(a => Guid.TryParse(a, out var g) ? g : (Guid?)null).Where(g => g is not null).Select(g => g!.Value).ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id.ToString(), u => u.DisplayName, ct);
        return all.ToDictionary(a => a, a => users.GetValueOrDefault(a) ?? a);
    }

    private async Task<List<ShipmentDto>> ShipmentDtosAsync(IQueryable<Shipment> query, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var rows = await query.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var info = await OrderInfoAsync(rows.Select(r => r.OrderId), ct);
        var lineIds = rows.SelectMany(r => r.Lines).Select(l => l.OrderLineId).ToList();
        var lines = await db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => (l.Sku, l.Description, l.LineNo), ct);
        var issueIds = rows.Where(r => r.StockIssueId != null).Select(r => r.StockIssueId!.Value).ToList();
        var issues = await db.StockIssues.AsNoTracking().Where(i => issueIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.IssueNumber, ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.DispatchedBy, r.DeliveredBy }), ct);
        return rows.Select(s =>
        {
            var o = info.GetValueOrDefault(s.OrderId);
            return new ShipmentDto(s.Id, s.ShipmentNumber, s.OrderId, o.Invoice, o.Customer, o.Address, s.Status, s.Carrier, s.TrackingReference, s.Notes,
                s.StockIssueId, s.StockIssueId is { } iid ? issues.GetValueOrDefault(iid) : null, s.DispatchedAt, Name(names, s.DispatchedBy), s.DeliveredAt,
                Name(names, s.DeliveredBy), s.CancelledAt,
                s.Lines.OrderBy(l => lines.GetValueOrDefault(l.OrderLineId).LineNo).Select(l => new ShipmentLineDto(l.OrderLineId,
                    lines.GetValueOrDefault(l.OrderLineId).Sku ?? "", lines.GetValueOrDefault(l.OrderLineId).Description ?? "", l.Quantity)).ToList(),
                s.Version, s.CreatedAt);
        }).ToList();
    }

    private async Task<List<SalesReturnDto>> ReturnDtosAsync(IQueryable<SalesReturn> query, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var rows = await query.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.PostedAt).ToListAsync(ct);
        var info = await OrderInfoAsync(rows.Select(r => r.OrderId), ct);
        var lineIds = rows.SelectMany(r => r.Lines).Select(l => l.OrderLineId).ToList();
        var lines = await db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => (l.Sku, l.Description, l.LineNo), ct);
        var returnIds = rows.Select(r => r.Id).ToList();
        var notes = await db.CreditNotes.AsNoTracking().Include(c => c.Lines).Where(c => c.SalesReturnId != null && returnIds.Contains(c.SalesReturnId.Value))
            .ToDictionaryAsync(c => c.SalesReturnId!.Value, ct);
        var names = await NamesAsync(rows.Select(r => r.CreatedBy), ct);
        return rows.Select(r =>
        {
            var o = info.GetValueOrDefault(r.OrderId);
            var note = notes.GetValueOrDefault(r.Id);
            return new SalesReturnDto(r.Id, r.ReturnNumber, r.OrderId, o.Invoice, o.Customer, r.Reason, r.PostedAt, Name(names, r.CreatedBy) ?? "", note?.Id,
                note?.CreditNoteNumber, note?.Total ?? 0,
                r.Lines.OrderBy(l => lines.GetValueOrDefault(l.OrderLineId).LineNo).Select(l => new SalesReturnLineDto(l.OrderLineId, l.ItemId,
                    lines.GetValueOrDefault(l.OrderLineId).Sku ?? "", lines.GetValueOrDefault(l.OrderLineId).Description ?? "", l.Quantity, l.Disposition, l.UnitCost,
                    note?.Lines.Where(c => c.OrderLineId == l.OrderLineId).Sum(c => c.Amount) ?? 0)).ToList());
        }).ToList();
    }

    private async Task<List<CreditNoteDto>> CreditDtosAsync(IQueryable<CreditNote> query, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var rows = await query.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.IssuedAt).ToListAsync(ct);
        var info = await OrderInfoAsync(rows.Select(r => r.OrderId), ct);
        var returnIds = rows.Where(r => r.SalesReturnId != null).Select(r => r.SalesReturnId!.Value).ToList();
        var returns = await db.SalesReturns.AsNoTracking().Where(r => returnIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.ReturnNumber, ct);
        var names = await NamesAsync(rows.Select(r => r.CreatedBy), ct);
        return rows.Select(c =>
        {
            var o = info.GetValueOrDefault(c.OrderId);
            return new CreditNoteDto(c.Id, c.CreditNoteNumber, c.OrderId, o.Invoice, o.Customer, c.SalesReturnId,
                c.SalesReturnId is { } rid ? returns.GetValueOrDefault(rid) : null, c.Reason, c.Total, c.TaxTreatment, c.GstAmount, c.Currency, c.IssuedAt,
                Name(names, c.CreatedBy) ?? "", c.Lines.OrderBy(l => l.OrderLineId is null).Select(l => new CreditNoteLineDto(l.OrderLineId, l.Description, l.Quantity, l.Amount)).ToList());
        }).ToList();
    }

    private async Task<List<RefundDto>> RefundDtosAsync(IQueryable<Refund> query, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var rows = await query.AsNoTracking().Include(x => x.Allocations).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var info = await OrderInfoAsync(rows.Select(r => r.OrderId), ct);
        var paymentIds = rows.SelectMany(r => r.Allocations).Select(a => a.PaymentId).ToList();
        var receipts = await db.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ReceiptNumber, ct);
        var noteIds = rows.Where(r => r.CreditNoteId != null).Select(r => r.CreditNoteId!.Value).ToList();
        var notes = await db.CreditNotes.AsNoTracking().Where(c => noteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CreditNoteNumber, ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.RequestedBy, r.ApprovedBy, r.CompletedBy }), ct);
        return rows.Select(r =>
        {
            var o = info.GetValueOrDefault(r.OrderId);
            return new RefundDto(r.Id, r.RefundNumber, r.OrderId, o.Invoice, o.Customer, r.CreditNoteId, r.CreditNoteId is { } nid ? notes.GetValueOrDefault(nid) : null,
                r.Amount, r.Method, r.Reason, r.Status, Name(names, r.RequestedBy) ?? "", Guid.TryParse(r.RequestedBy, out var g) ? g : Guid.Empty, r.CreatedAt,
                Name(names, r.ApprovedBy), r.ApprovedAt, Name(names, r.CompletedBy), r.CompletedAt, r.EvidenceReference, r.FailureReason,
                r.Allocations.Select(a => new RefundAllocationDto(a.PaymentId, receipts.GetValueOrDefault(a.PaymentId) ?? "", a.Amount)).ToList(), r.Version);
        }).ToList();
    }

    private static string? Name(Dictionary<string, string> names, string? actorId) => actorId is null ? null : names.GetValueOrDefault(actorId) ?? actorId;
    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Money(decimal value) => "$" + value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
