Phase 6: Checkout & payments (public site).
- Customer account (register/login) and guest checkout (propose, then implement the decision).
- Checkout steps: Contact details -> Delivery address -> Invoicing address
  (with "same as delivery") -> Payment method -> Review -> Place order.
- Payment options:
  a) Online card payment through a payment gateway behind an `IPaymentGateway`
     interface (evaluate Stripe or another NZ-supported gateway for NZD settlement). Use hosted checkout; never
     handle raw card data. Verify webhook signatures, make webhooks idempotent.
  b) Pay later (Bank transfer or Pay on delivery): order is created as Unpaid/PayAfterDelivery.
- Stock reservation at order placement with expiry for unpaid online attempts.
- Paid online -> automatically generate the invoice (PDF + email). Pay-later orders
  appear in the admin Orders list so the admin can generate/confirm the invoice.
- Order confirmation page, order history, order status tracking.
- Email notifications via `IEmailSender` (SMTP/SendGrid, configurable).
Tests: webhook idempotency, reservation expiry, order -> invoice flow.

## Approval decisions and schema
Confirm account/guest behavior, customer/staff isolation, delivery zones/fees, reservation duration and refund approval rules. NZD/Pacific-Auckland and delivery/tax/refund functionality are required.
Verify New Zealand merchant eligibility, NZD settlement, refund support, fees and sandbox behavior using official gateway documentation. Stripe is a candidate; provider selection remains subject to approval.
Reuse Phase 4 orders/invoices/payments/reservations. Add auditable payment attempts, unique processed-event records, ownership/guest tracking and durable notification jobs as needed.

## Reliable payment workflow
- Server reloads eligibility, prices, delivery/tax and stock; ignores client totals. Create order/reservation atomically with request idempotency.
- Persist payment attempts before hosted checkout; do not hold database transactions across gateway calls. Reconcile interrupted attempts.
- Verify signatures over provider-required raw payloads and validate merchant, amount, currency and order reference. Browser redirects are not payment proof.
- Handle duplicate/out-of-order webhooks transactionally; create at most one payment and approved invoice.
- Expire reservations through a persistent worker. Define late success after expiry: safely re-reserve or route to refund/manual resolution; never oversell.
- Bank/delivery payments remain unpaid until verified. Durable retryable email/PDF jobs prevent notification errors from losing payment acknowledgment.
- Enforce ownership on every order/download. Guest links use expiring unguessable tokens; sensitive pages avoid shared caches.

## API and acceptance
Plan customer auth/account, /api/checkout, gateway initiation/webhook, order history/detail and secure guest tracking.
Test forged/replayed/out-of-order events, amount mismatch, duplicate clicks, stock races, expiry/late payment, gateway interruption, ownership isolation and notification retries.
Use sandbox payments; live charges and production credentials need separate authorization.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Required delivery and tax checkout
- Reuse Phase 4 delivery settings and confirmed NotRegistered tax treatment. Validate NZ addresses/postcodes, service-area eligibility and rural surcharges as configured; pickup requires no shipping address if enabled.
- Server quote includes item subtotal, discounts, delivery and final NZD total, with stored sales GST amount zero and NotRegistered treatment; no customer GST charge or GST-inclusive label. Revalidate immediately before order placement; snapshot the accepted breakdown.
- Gateway amount/currency must match the stored final NZD total using integer minor units. Do not infer currency from a browser locale.
- Show shipment/tracking/delivery status in customer order detail. Carrier API automation is optional and requires approval; manual tracking entry is sufficient initially.

## Required online refunds
- Extend IPaymentGateway with idempotent partial/full refund initiation, status lookup and verified refund webhooks; reuse Phase 4 records/approvals.
- Issue refunds against the original captured payment and reconcile provider IDs/statuses. A requested or pending refund is not displayed as completed.
- Never hold a DB transaction during gateway calls; persist intent then retry/reconcile timeouts safely. Duplicate events cannot duplicate credits or refunds.
- Late successful payment without fulfillable stock triggers the approved reconciliation/refund workflow and a customer notification.
- Add authenticated customer refund/return request submission and staff review; secure guest requests through expiring tokens and verification. No automatic approval based only on a request.
- Customer confirmation/history and emails show delivery fees, non-GST treatment, credits and completed/pending refunds separately.
- Tests include rural/unsupported addresses, stale delivery quotes, exact NZD cents and zero GST on goods/delivery, duplicate/partial/failed refunds, provider timeouts, ownership checks and no restock from financial refunds.


## Review 2026-11-17: gaps found after Phases 0-2

### Customer accounts must stay separate from staff
- Reuse the Phase 1 Identity setup (`UserStore`, password hashing, lockout) with `UserType.Customer`, but give customers their own JWT audience (e.g. `spareparts-shop`), their own refresh cookie name and path, and never any staff permission. Staff policies must reject customer tokens, and customer endpoints must reject staff tokens. Test both directions.
- Self-registration, email verification and **forgot password** need email tokens. Phase 1 did not add Identity token providers or ASP.NET Data Protection: add them here and **persist the Data Protection keys** (database or Blob Storage) so tokens survive restarts and work across instances.
- Link a customer login to the Phase 4 `Customer` record. A guest checkout creates or matches a customer record by email without creating a login.

### Missing items
- **Bot and abuse protection** on registration, login, guest order tracking and checkout: rate limits as in Phase 1, and a privacy-friendly CAPTCHA only if abuse appears (needs approval).
- **NZ Privacy Act 2020.** A privacy notice (owner-approved content), a way for customers to see and correct their details, a retention period for guest order data, and no card data stored, ever (hosted checkout only).
- **Consumer law wording.** Terms, returns and refund pages must not remove Consumer Guarantees Act or Fair Trading Act rights; the content comes from the owner, not invented.
- **Email deliverability.** Set up SPF, DKIM and DMARC for the sending domain and a reply-to address the shop reads; check that order emails do not land in spam.
- **Pay-later order confirmations** must say clearly that the order is not paid yet, give the bank details and the reference to quote, and say when unpaid orders are cancelled.
