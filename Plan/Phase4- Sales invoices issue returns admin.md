Phase 4: Sales (admin side).
Entities: Customer (optional lightweight), SalesOrder/Invoice (InvoiceNumber, Source
[Admin, Telephone, Online], Status [Draft, Confirmed, PartiallyPaid, Paid, Issued,
Cancelled], PaymentMethod, PaymentStatus [Unpaid, Paid, PayAfterDelivery], delivery
address, invoicing address, contact details), InvoiceLine (ItemId, Qty, UnitPrice,
Discount, Tax), Payment, StockIssue, SalesReturn + SalesReturnLine.
Flows:
- Admin creates an invoice (phone order, or converts an unpaid online order).
- Stock reservation on confirmation (propose: reserved qty vs on-hand qty) and
  stock issue (movement type Issue) when goods are issued/delivered.
- View all orders/invoices (including those from the public site) with filters
  (status, date, payment status, source).
- Record payments (cash, bank transfer, card, pay-after-delivery settlement).
- Returns: select invoice, choose lines and qty (cannot exceed issued qty), reason,
  create a positive Return movement only for restock; non-restock/write-off does not increase saleable stock; issue a credit note as applicable.
- Printable invoice PDF (QuestPDF or similar).
Admin UI: invoice create/edit, invoice list + detail, issue-stock action, record payment, return screen.
Tests: reservation, issue, partial return, over-return prevention, stock after return.

## Lifecycle and schema decisions
- Separate order, payment and fulfillment state: partially paid and partially issued can coexist. Propose transition tables and permitted actors.
- Justify separate SalesOrder/Invoice models versus a combined document. Preserve original prices and contact/address snapshots.
- Use NZD and Pacific/Auckland. GST registration is confirmed absent. Approve rounding, numbering, reservation duration, delivery fees and refund approval rules; delivery, tax and refunds are required scope.
- Add auditable reservations, partial issues, payments, returns and credit notes with original-line references. All tables have audit fields and money uses numeric(18,2).

## Transaction rules
- Available = ledger on-hand minus active unexpired reservations. Reservations never create stock movements.
- Reserve atomically using the inventory concurrency protocol; expiry/cancellation releases reservations.
- Issue consumes reservations and appends negative movements in one transaction. Prevent duplicate/over-issue.
- Payments support partial/multiple receipts and idempotency. PayAfterDelivery is a collection arrangement, not proof of payment.
- Return limits subtract prior accepted returns from issued quantities, including concurrent requests.
- Restock returns increase saleable stock; write-off/non-restock returns do not. Credit notes do not imply cash refunds.
- Server calculates totals; corrections use reversals, not deleted financial history.

## Files, API and acceptance
Plan migrations, handlers, validators, PDF rendering and admin screens. Proposed groups: /api/orders, /api/invoices, /api/stock-issues, /api/payments, /api/sales-returns, credit notes, /api/refunds and delivery configuration and read-only non-GST status.
Verify PDF library licensing. Test races, duplicate actions, exact rounding, partial payment/issue, historical prices, expiry and return/write-off behavior. Gateway integration remains Phase 6.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Required delivery and fulfillment module
- Add audited delivery configuration: enabled service areas/zones, NZ postcode/rural rules, charge and optional free-delivery threshold. Local pickup may be offered after confirmation.
- Order delivery snapshots preserve method, address, quoted charge, tax treatment and service promise. Rate edits never recalculate posted invoices.
- Add shipment/fulfillment records with carrier, tracking reference, dispatch/delivery timestamps and status; all goods leave the single shop.
- Staff can quote delivery, dispatch partial/full orders and record delivery. Stock decreases exactly once at physical issue/dispatch; delivery confirmation does not issue it again.
- Proposed endpoints: /api/delivery-settings, /api/delivery-zones, /api/orders/{id}/delivery-quote and /api/shipments; authorize settings and fulfillment separately.

## Confirmed non-GST sales and document handling
- The shop is not GST-registered. Record IsGstRegistered=false, no GST number, and document tax treatment NotRegistered with sales GST amount zero. This is not a zero-rated or exempt classification.
- Server totals equal item charges minus discounts plus delivery charges; do not add GST or extract a GST component. Reject client requests attempting to charge GST.
- Ordinary invoices, receipts and linked credit notes use NZD and may state 'Not GST registered - no GST charged'. Do not label them 'Tax invoice' or show 'GST included', a GST registration number, or a claimable GST amount.
- Preserve original non-GST treatment on returns, credits and refunds. Allocate discounts/delivery/refunds to cents without exceeding original document amounts.
- Provide sales/payment/credit/refund reconciliation, not GST returns or GST filing. Supplier charges remain purchase costs; do not infer recoverable input GST or extract it from GRN unit costs.
- Future registration is a separate approved change with an effective date and preserved historical snapshots. Do not build rate administration, registered-business tax documents or GST filing now.

## Required manual refunds and credit accounting
- Add Refund/RefundAllocation records linked to original payment(s), invoice/credit note and return where applicable: amount NZD, reason, approver, method, status, idempotency/reference and audit fields.
- Distinguish Requested/Approved/Processing/Succeeded/Failed/Cancelled; approved credit is not automatically refunded cash. Manual cash/bank refunds require evidence/reference and completion confirmation.
- Enforce remaining captured/received payment minus successful and pending committed refunds under concurrency, and cap allocations to the approved credit/eligible cancellation amount. Handle overpayments explicitly.
- Support partial/full refunds and cancellation before stock issue. Financial refunds alone never restock goods.
- Define delivery-charge refund eligibility and admin reason/approval; snapshot refunded delivery and original non-GST treatment.
- Proposed endpoints: /api/refunds, explicit approve/complete/cancel actions, refund detail/list and invoice credit/refund balance. Gateway execution is added in Phase 6.
- Test delivery rates, unsupported zones, dispatch once, no-GST item/delivery totals and rejection of injected tax, partial credit rounding, concurrent over-refund, duplicate refund completion and unchanged historical invoices.


## Review 2026-11-17: gaps found after Phases 0-2

### Split this phase into two deliveries
Phase 4 is the largest phase (orders, reservations, issue, payments, PDF, delivery, returns, credit notes, refunds). Propose, approve and deliver it in two parts so the shop can start selling sooner:
- **4a: Sell and get paid.** Customers (lightweight), orders/invoices, reservations, stock issue, payments, numbering, invoice PDF, order list, daily takings report.
- **4b: After the sale.** Delivery settings and shipments, returns, credit notes, manual refunds.

### Missing items
- **Daily takings / cash-up report.** Payments received per day in Pacific/Auckland time, split by method (cash, card, bank transfer), with invoice references, so the owner can reconcile the till and bank each day. Include refunds paid out that day.
- **Invoice numbering.** Gapless and sequential per document type (e.g. `INV-000123`, `CN-000045`), allocated inside the posting transaction from a counter row locked `FOR UPDATE`. Drafts have no number. Cancelled invoices keep their number and are marked cancelled, never reused.
- **Price override and discounts need permission.** Charging less than the current selling price, or any line or order discount, needs a new key (proposal: `sales.discount`) and a reason; the original price is kept on the line.
- **More permission keys** to add to `PermissionKeys` and the seeded roles: `payments.record`, `sales.cancel`, `sales.discount`, `creditnotes.issue`. Enable the seeded Sales menus.
- **Refund approval (decided in ADR 0003, D15).** Cashier and Admin can create refunds; only Admin approves and executes; nobody approves a refund they requested. Enforced on the server.
- **One customer record, reused in Phase 6.** A single `Customer` table for phone and walk-in customers now; Phase 6 links an optional online login to the same record. Staff stay in `users` (`UserType.Staff`) and are never customers.
- **Walk-in sale.** Allow an invoice with no customer record ("Cash sale") for counter sales; contact details optional.
- **Reservation expiry for staff orders.** State whether staff-created reservations expire (proposal: no expiry, released on cancel) versus online ones (Phase 6, with expiry). A background job is only needed for expiring reservations.
- **PDF library.** QuestPDF is free only under its Community licence revenue limit; confirm the shop qualifies or choose an MIT-licensed alternative before coding.
- **Emailing invoices** needs `IEmailSender`, which arrives in Phase 6. Phase 4 produces the PDF for printing and download only; do not build email twice.
