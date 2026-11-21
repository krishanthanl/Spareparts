# ADR 0005: Sales orders, invoices, stock issue and payments (Phase 4a)

Status: accepted 2026-11-21. `Plan/Phase4a-Design-Proposal.md` approved as written ("Approve, start coding"). The open questions in that proposal had no separate answers, so the proposal's recommendations were taken:
1. One order/invoice document (D1).
2. In-stock only: no backorders (D5).
3. No third-party PDF library (D16, see below).
4. Shop details for invoices come from configuration (`Shop` section) and are blank until filled in.
5. Payment methods: Cash, EFTPOS/Card, Bank transfer, Other.
6. Cancelling confirmed invoices and voiding payments are Admin only (`sales.cancel`, `payments.void`).
7. Numbering starts at `INV-000001`.

Any of these can still be changed. Points 4 and 7 are configuration or data, not code.

## Decisions

- **One document.** `sales_orders` is a quote/order as a draft and becomes the invoice on **Confirm**, which allocates a gapless `INV-` number from `document_counters` inside the transaction. Drafts have no number. Cancelled invoices keep theirs.
- **Three independent states**, all set by the server: `Status` (Draft → Confirmed ↔ Completed, or → Cancelled), `PaymentStatus` (Unpaid/PartiallyPaid/Paid/Overpaid) and `FulfillmentStatus` (NotIssued/PartiallyIssued/Issued). Completed means fully paid and fully issued. A void can move it back to Confirmed. "Pay on delivery" is `PaymentTerms = OnDelivery`, not a payment.
- **Frozen after Confirm.** A trigger (`sales_order_guard`) allows only those status changes. Once an order is confirmed, it rejects any change except to the status, amount-paid and cancellation columns. Lines can change only while the order is a draft (the Phase 3 `inv_draft_lines_only` trigger). Each line keeps `ListPrice` (the price at the time) and `UnitPrice` (the price charged). The order keeps a snapshot of the customer and addresses. Changing an invoice means cancelling it and using *Copy to new draft*.
- **Reservations are derived, not stored.** Reserved = Σ over confirmed or completed orders of (line quantity − issued). It is read in `StockLedger.LockAsync` under the same item-row locks as on-hand, so it cannot drift and needs no cleanup job. This differs from the proposal's `stock_reservations` table, and the behaviour is the same. Available = on-hand − reserved.
  - Confirm locks the order, then the items in id order, and rejects any line where available < quantity. Of two racing orders for the last unit, exactly one confirms.
  - Manual adjustments and GRN reversals cannot take on-hand below reserved. A stocktake can, because it records what is physically there.
  - Staff reservations do not expire. Online orders with expiry come in Phase 6.
- **Issue** (`ISS-`) is partial or full. In one transaction it posts one `Issue` movement per line at the current average cost, and the line's reservation shrinks by the same amount. Protection against issuing too much: an application check, plus a deferred constraint trigger (issued ≤ ordered per line), plus a unique `PostingKey` per movement, plus an optional idempotency key per issue. Issues are append-only. An order with any issue cannot be cancelled, and a trigger enforces this too.
- **Payments** (`RCT-`) are append-only, with the database setting `ReceivedAt`. Every request needs an idempotency key, so a retry returns the same result, and a key reused for a different order is rejected. Payments on the same order are serialised by the order-row lock. A mistake is fixed with a **void** row (`VD-`, the exact negative, linked to the original, reason required, `payments.void`). Overpayment is allowed and shown as owed to the customer (Phase 4b refunds settle it). Cancelling is blocked while the net paid is not zero.
- **Money.** The server prices every draft. Line amount = round(qty × unit price). A discount is an amount or a %, turned into cents and rounded half away from zero. An order discount is split across lines by largest remainder, so the shares add up exactly. Total = Σ lines + delivery. There is **no GST**: `TaxTreatment = NotRegistered`, `GstAmount = 0` (check constraint), currency NZD (check constraint). Inputs reject unknown JSON members, so a client cannot send tax or total fields.
- **Discounts** (any line or order discount, or a unit price below list) need `sales.discount` and a reason. Charging above list needs neither.
- **Quick sale** (`POST /api/orders/quick-sale`): create, confirm, issue everything and take payment in one transaction. The payment idempotency key makes a retry return the same sale. If stock is short, nothing is saved.
- **Invoice PDF** is rendered on demand from the stored snapshot by a small built-in writer (`SimplePdf`) using the standard Helvetica fonts that every PDF reader has. There is no dependency, licence or font file, and it works on the Windows App Service and Linux CI alike. Text is Latin-1: macrons and other characters outside it print as the base letter (ā → a). The heading is "INVOICE" with "Not GST registered - no GST charged", never "Tax invoice". Emailing comes in Phase 6.
- **Daily takings** (`GET /api/reports/daily-takings?from&to[&format=csv]`): payments for each Pacific/Auckland day (daylight saving handled by `ShopTime`), by method, with voids shown separately. The range is at most 92 days. The refunds column is zero until 4b. CSV cells are quoted, and leading `= + - @` are escaped.
- **Customers**: one lightweight `customers` table, which Phase 6 will reuse for online logins. Walk-in sales need no customer ("Cash sale").
- **Permissions** (new keys, granted once to existing system roles through `permission_seeds`):

  | Key | Admin | Cashier | StoreKeeper |
  |---|---|---|---|
  | `sales.discount`, `sales.cancel`, `payments.void` | ✓ | | |
  | `sales.issue` | ✓ | ✓ | ✓ |
  | `payments.record`, `sales.reports`, `customers.manage` | ✓ | ✓ | |

  Seeded menus: Sales → Counter sale, Orders and invoices, Customers, Daily takings. Returns stays off until 4b.
- **Stock report** gains Reserved and Available columns (current stock only; historical stock has no reservations). Stock-card rows from sales show their `ISS-` number.
