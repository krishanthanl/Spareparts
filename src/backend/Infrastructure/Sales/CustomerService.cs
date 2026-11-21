using Application.Catalog;
using Application.Common;
using Application.Sales;
using Domain.Sales;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Sales;

public sealed class CustomerService(AppDbContext db) : ICustomerService
{
    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerListRequest request, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (!request.IncludeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern, "\\"))
                || (x.Email != null && EF.Functions.ILike(x.Email, pattern, "\\")) || (x.Company != null && EF.Functions.ILike(x.Company, pattern, "\\")));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(rows.Select(Dto).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) =>
        Dto(await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer not found."));

    public async Task<CustomerDto> CreateAsync(CustomerInput input, CancellationToken ct)
    {
        var customer = new Customer();
        Apply(customer, input);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerInput input, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer not found.");
        ExpectVersion(db, customer, input.Version);
        Apply(customer, input);
        await SaveAsync(db, "This customer was changed by someone else.", ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Customer c, CustomerInput input)
    {
        c.Name = input.Name.Trim();
        c.Phone = Trim(input.Phone);
        c.Email = Trim(input.Email);
        c.Company = Trim(input.Company);
        c.BillingAddress = Trim(input.BillingAddress);
        c.DeliveryAddress = Trim(input.DeliveryAddress);
        c.Notes = Trim(input.Notes);
        c.IsActive = input.IsActive;
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static CustomerDto Dto(Customer c) => new(c.Id, c.Name, c.Phone, c.Email, c.Company, c.BillingAddress, c.DeliveryAddress, c.Notes, c.IsActive,
        c.Version, c.UpdatedAt);
}

/// <summary>Daily takings by Pacific/Auckland shop day (ADR 0005, D17). Refunds join in Phase 4b.</summary>
public sealed class TakingsReportService(AppDbContext db, ShopTime shopTime) : ITakingsReportService
{
    public async Task<TakingsReport> DailyAsync(TakingsRequest request, CancellationToken ct)
    {
        var start = shopTime.GetUtcDayRange(request.From).Start;
        var end = shopTime.GetUtcDayRange(request.To).EndExclusive;
        var rows = await (from p in db.Payments.AsNoTracking()
                          join o in db.SalesOrders.AsNoTracking() on p.OrderId equals o.Id
                          where p.ReceivedAt >= start && p.ReceivedAt < end
                          orderby p.ReceivedAt, p.ReceiptNumber
                          select new { p, o.InvoiceNumber, o.CustomerName }).ToListAsync(ct);
        var ids = rows.Select(r => r.p.CreatedBy).Select(a => Guid.TryParse(a, out var g) ? g : Guid.Empty).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id.ToString(), u => u.DisplayName, ct);
        var payments = rows.Select(r => new TakingsPaymentRow(r.p.Id, r.p.ReceiptNumber, r.InvoiceNumber, r.p.OrderId, r.CustomerName, r.p.Method, r.p.Amount,
            r.p.Reference, r.p.ReceivedAt, names.GetValueOrDefault(r.p.CreatedBy) ?? r.p.CreatedBy, r.p.VoidsPaymentId is not null)).ToList();

        var days = new List<TakingsDay>();
        for (var day = request.From; day <= request.To; day = day.AddDays(1))
        {
            var (s, e) = shopTime.GetUtcDayRange(day);
            var list = payments.Where(p => p.ReceivedAt >= s && p.ReceivedAt < e).ToList();
            days.Add(Day(day, list));
        }
        var all = Day(request.From, payments);
        return new TakingsReport(request.From, request.To, days, all.Methods, all.Received, all.Voided, 0, all.Net);
    }

    private static TakingsDay Day(DateOnly date, IReadOnlyList<TakingsPaymentRow> list)
    {
        var methods = Enum.GetValues<Domain.Sales.PaymentMethod>().Select(m =>
        {
            var received = list.Where(p => p.Method == m && !p.IsVoid).Sum(p => p.Amount);
            var voided = -list.Where(p => p.Method == m && p.IsVoid).Sum(p => p.Amount);
            return new TakingsMethodTotal(m, received, voided, received - voided);
        }).ToList();
        var totalReceived = methods.Sum(x => x.Received);
        var totalVoided = methods.Sum(x => x.Voided);
        return new TakingsDay(date, methods, totalReceived, totalVoided, 0, totalReceived - totalVoided, list);
    }
}
