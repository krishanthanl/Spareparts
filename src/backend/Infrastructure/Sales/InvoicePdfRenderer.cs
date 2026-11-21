using System.Globalization;
using Application.Common;
using Application.Sales;
using Domain.Sales;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Sales;

/// <summary>
/// Prints a confirmed invoice from its stored snapshot (never from today's prices or customer record).
/// The shop is not GST-registered, so this is an "Invoice", never a "Tax invoice" (D11).
/// </summary>
public sealed class InvoicePdfRenderer(AppDbContext db, ShopTime shopTime, ShopSettings shop) : IInvoicePdfService
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private const double Left = 40, Right = SimplePdf.PageWidth - 40, Bottom = SimplePdf.PageHeight - 70;
    // Column right edges (description is left-aligned and fills the space before Qty).
    private const double ColSku = Left + 4, ColDesc = 130, ColQty = 360, ColPrice = 430, ColDiscount = 490, ColAmount = Right - 4;

    public async Task<(string FileName, byte[] Content)> RenderAsync(Guid orderId, CancellationToken ct)
    {
        var o = await db.SalesOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new NotFoundException("Order not found.");
        if (o.InvoiceNumber is null) throw new ConflictException("Confirm the order first: drafts have no invoice.");
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.ReceivedAt).ToListAsync(ct);
        var pdf = new SimplePdf();
        var y = Header(pdf, o, first: true);

        y = Addresses(pdf, o, y);
        y = TableHeader(pdf, y);
        foreach (var line in o.Lines.OrderBy(l => l.LineNo))
        {
            var desc = SimplePdf.Wrap(line.Description, ColQty - 50 - ColDesc, 9);
            var height = 4 + 12 * desc.Count + (line.DiscountReason is null ? 0 : 11);
            if (y + height > Bottom)
            {
                pdf.NewPage();
                y = TableHeader(pdf, Header(pdf, o, first: false));
            }
            pdf.Text(ColSku, y + 10, SimplePdf.Fit(line.Sku, ColDesc - ColSku - 6, 9), 9);
            for (var i = 0; i < desc.Count; i++) pdf.Text(ColDesc, y + 10 + 12 * i, desc[i], 9);
            if (line.DiscountReason is { } reason) pdf.Text(ColDesc, y + 10 + 12 * desc.Count, SimplePdf.Fit("Discount: " + reason, ColQty - 50 - ColDesc, 8), 8, gray: 0.4);
            pdf.TextRight(ColQty, y + 10, Qty(line.Quantity), 9);
            pdf.TextRight(ColPrice, y + 10, Money(line.UnitPrice), 9);
            var discount = line.LineDiscount + line.OrderDiscountShare;
            pdf.TextRight(ColDiscount, y + 10, discount == 0 ? "" : "-" + Money(discount), 9);
            pdf.TextRight(ColAmount, y + 10, Money(line.LineTotal), 9);
            y += height + 4;
            pdf.Line(Left, y, Right, y, 0.3, 0.85);
        }

        if (y + 160 > Bottom) { pdf.NewPage(); y = Header(pdf, o, first: false); }
        y += 16;
        void Total(string label, string value, bool bold = false)
        {
            pdf.TextRight(ColDiscount, y, label, bold ? 11 : 10, bold);
            pdf.TextRight(ColAmount, y, value, bold ? 11 : 10, bold);
            y += bold ? 18 : 15;
        }
        Total("Subtotal", Money(o.Subtotal));
        if (o.LineDiscountTotal + o.OrderDiscount > 0) Total("Discounts", "-" + Money(o.LineDiscountTotal + o.OrderDiscount));
        if (o.DeliveryMethod == DeliveryMethod.Delivery) Total("Delivery", Money(o.DeliveryCharge));
        pdf.Line(ColPrice - 60, y - 10, Right, y - 10, 0.6, 0.3);
        y += 4;
        Total("Total NZD", Money(o.Total), bold: true);
        Total("Paid", Money(o.AmountPaid));
        Total(o.AmountPaid > o.Total ? "Credit owing to customer" : "Balance due", Money(Math.Abs(o.Total - o.AmountPaid)), bold: true);

        var received = payments.Where(p => p.VoidsPaymentId is null && !payments.Any(v => v.VoidsPaymentId == p.Id)).ToList();
        if (received.Count > 0)
        {
            y += 6;
            pdf.Text(Left, y, "Payments received", 9, bold: true);
            y += 13;
            foreach (var p in received)
            {
                if (y > Bottom) { pdf.NewPage(); y = Header(pdf, o, first: false); }
                pdf.Text(Left, y, $"{Date(p.ReceivedAt)}  {p.ReceiptNumber}  {MethodName(p.Method)}{(p.Reference is null ? "" : "  " + p.Reference)}", 9);
                pdf.TextRight(ColAmount, y, Money(p.Amount), 9);
                y += 12;
            }
        }

        // Footer on every page.
        var pageCount = pdf.PageCount;
        Footer(pdf, o, pageCount);
        return ($"{o.InvoiceNumber}.pdf", pdf.Build());
    }

    private double Header(SimplePdf pdf, SalesOrder o, bool first)
    {
        double y = 50;
        pdf.Text(Left, y, shop.TradingName, first ? 18 : 12, bold: true);
        pdf.TextRight(Right, y, "INVOICE", first ? 20 : 12, bold: true);
        if (!first)
        {
            pdf.TextRight(Right, y + 14, o.InvoiceNumber + " (continued)", 9);
            return y + 34;
        }
        y += 18;
        var details = new[] { shop.Address, shop.Phone, shop.Email }.Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => s!.Split('\n')).ToList();
        var infoY = y;
        foreach (var d in details) { pdf.Text(Left, y, d.Trim(), 9, gray: 0.25); y += 12; }
        pdf.TextRight(Right, infoY, "Invoice no. " + o.InvoiceNumber, 10, bold: true);
        pdf.TextRight(Right, infoY + 14, "Date " + Date(o.ConfirmedAt ?? o.CreatedAt), 9);
        pdf.TextRight(Right, infoY + 27, o.PaymentTerms == PaymentTerms.OnDelivery ? "Payment on delivery" : "Payment due on receipt", 9);
        if (o.Status == SalesOrderStatus.Cancelled) pdf.TextRight(Right, infoY + 42, "CANCELLED", 12, bold: true);
        y = Math.Max(y, infoY + 50);
        pdf.Text(Left, y, "Not GST registered - no GST charged", 9, bold: true);
        return y + 20;
    }

    private static double Addresses(SimplePdf pdf, SalesOrder o, double y)
    {
        var billTo = new[] { o.CustomerName ?? "Cash sale", o.CustomerCompany, o.BillingAddress, o.CustomerPhone, o.CustomerEmail }
            .Where(s => !string.IsNullOrWhiteSpace(s)).SelectMany(s => SimplePdf.Wrap(s!, 230, 9)).ToList();
        var deliverTo = o.DeliveryMethod == DeliveryMethod.Delivery
            ? SimplePdf.Wrap(o.DeliveryAddress ?? "", 230, 9)
            : (IReadOnlyList<string>)["Customer pickup"];
        pdf.Text(Left, y, "Bill to", 9, bold: true, gray: 0.35);
        pdf.Text(310, y, o.DeliveryMethod == DeliveryMethod.Delivery ? "Deliver to" : "Delivery", 9, bold: true, gray: 0.35);
        for (var i = 0; i < billTo.Count; i++) pdf.Text(Left, y + 13 + 12 * i, billTo[i], 9);
        for (var i = 0; i < deliverTo.Count; i++) pdf.Text(310, y + 13 + 12 * i, deliverTo[i], 9);
        return y + 13 + 12 * Math.Max(billTo.Count, deliverTo.Count) + 14;
    }

    private static double TableHeader(SimplePdf pdf, double y)
    {
        pdf.FillRect(Left, y, Right - Left, 18);
        pdf.Text(ColSku, y + 12, "Part no.", 9, bold: true);
        pdf.Text(ColDesc, y + 12, "Description", 9, bold: true);
        pdf.TextRight(ColQty, y + 12, "Qty", 9, bold: true);
        pdf.TextRight(ColPrice, y + 12, "Unit price", 9, bold: true);
        pdf.TextRight(ColDiscount, y + 12, "Discount", 9, bold: true);
        pdf.TextRight(ColAmount, y + 12, "Amount", 9, bold: true);
        return y + 20;
    }

    private void Footer(SimplePdf pdf, SalesOrder o, int pageCount)
    {
        // Payment terms go once, at the foot of the last page (continuation pages carry the invoice number in their header).
        var y = SimplePdf.PageHeight - 52;
        pdf.Line(Left, y - 10, Right, y - 10, 0.4, 0.7);
        var terms = shop.PaymentTerms;
        if (!string.IsNullOrWhiteSpace(shop.BankAccount)) terms += "  Bank transfer: " + shop.BankAccount + ", reference " + o.InvoiceNumber + ".";
        foreach (var (line, i) in SimplePdf.Wrap(terms, Right - Left, 8).Take(2).Select((l, i) => (l, i))) pdf.Text(Left, y + 10 * i, line, 8, gray: 0.3);
        if (pageCount > 1) pdf.TextRight(Right, y + 20, $"{pageCount} pages", 8);
    }

    private string Date(DateTimeOffset instant) => shopTime.ToLocal(instant).ToString("d MMM yyyy", Nz);
    private static string Money(decimal value) => "$" + value.ToString("#,##0.00", CultureInfo.InvariantCulture);
    private static string Qty(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Card => "EFTPOS/Card",
        PaymentMethod.BankTransfer => "Bank transfer",
        _ => "Other",
    };
}
