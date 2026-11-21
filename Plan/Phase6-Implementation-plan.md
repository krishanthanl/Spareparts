# Phase 6 Checkout and Payments Implementation Plan

**Status** Draft for approval. See the [Phase 6 roadmap](Phase6-%20Checkout%20payments.md) and [working rules](Masterprompt.md).

## Handoff — Phase 6 is incomplete

Development is paused on the local `codex/Phase-6` branch. Server quotes, guest bank-transfer checkout, 48-hour stock holds, staff payment verification, guest tracking, shipment updates, and invoice downloads are implemented. See the [checkout setup and workflow](../docs/phase6-checkout.md).

Resume with the following work:

- [ ] Customer accounts, including email verification, password recovery, profiles, and authenticated order history.
- [ ] Order and invoice emails with durable delivery and retries, plus email-based guest access.
- [ ] Hosted card checkout in sandbox, verified payment webhooks, reservation expiry and late-payment reconciliation.
- [ ] Customer return and refund requests, staff review, and gateway refund processing.
- [ ] End-to-end verification for the remaining checkout, payment, email, and refund flows.

**Goal** Let shoppers place NZD orders from the storefront, pay securely, and track delivery, invoices, returns, and refunds.

**Architecture** Extend the .NET 10, EF Core/PostgreSQL, and React apps. Reuse the Phase 5 cart and Phase 4 sales, stock locks, delivery quotes, invoice PDF, shipments, and refund approvals. Add customer ownership, hosted payments, and durable processing around those services.

## Decisions for approval

| Decision | Proposed default |
|---|---|
| Checkout access | Offer guest checkout and optional verified accounts. Grant account access through explicit ownership. Claim guest orders only with an order-specific email proof. |
| Online payment | Stripe-hosted Checkout, card payments in NZD, sandbox first. Confirm the shop's eligibility, NZD payout setup, and applicable fees before enabling live mode. [Stripe NZ pricing](https://stripe.com/nz/pricing) · [Checkout documentation](https://docs.stripe.com/payments/checkout) |
| Pay later | Start with bank transfer. Make payment on delivery configurable once the shop confirms it. Propose 48 hours for bank transfer before cancellation, with staff confirmation against the order reference. |
| Stock hold | Propose a 35-minute card reservation with the hosted session ending first. Reconcile the attempt before expiry and handle a captured payment that arrives after release. |
| Delivery | Reuse configured NZ zones, rural surcharges, free delivery threshold, and pickup. Confirm the enabled methods, rates, and how rural addresses are validated before rollout. |
| Refunds | Reuse separate requester and approver permissions. Route late captures without stock to staff reconciliation and notify the customer. Confirm who handles the queue and whether to automate that specific refund later. |
| Shop content | Confirm sender address, bank instructions, guest data retention, and owner-approved privacy, delivery, returns, and terms copy. |

## Implementation sequence

### 1. Customer identity and ownership

- [ ] Extend `src/backend/Infrastructure/Identity` and add `src/backend/Api/Controllers/CustomerAccountController.cs`. Add `/api/customer/auth/{register,verify-email,login,refresh,logout,forgot-password,reset-password}` and `GET/PATCH /api/customer/profile`.
- [ ] Add an EF migration for a unique customer login link, explicit order ownership, hashed expiring guest grants, and persisted Data Protection keys. Keep customer JWT audience, refresh cookie and policy separate from staff. Rate-limit public account and guest endpoints and use generic recovery responses.
- [ ] Add storefront sign-in, registration, verification, password reset, and profile screens. Verify both directions of staff/customer policy rejection, expired grants, cross-account access, and token validation after restart.

### 2. Server quotes, orders, and reservations

- [ ] Add checkout contracts and FluentValidation under `src/backend/Application/Checkout` and orchestration under `src/backend/Infrastructure/Checkout`. Extend `SalesOrderService.cs` through shared internal operations and use `DeliveryService.cs` for shipping quotes.
- [ ] Add `POST /api/checkout/quote` and `POST /api/checkout/orders` in `CheckoutController.cs`. Quote items, discounts, delivery, and total from current server data. Revalidate the accepted quote at submission and return a refreshed quote on price or delivery changes. Snapshot the accepted amounts and addresses. Persist zero GST and `NotRegistered`.
- [ ] Add a migration for checkout request keys, request fingerprints, and reservation deadlines. Atomically create the order and reserve stock using the existing stock locks. Replay an identical submission and reject reuse of the key with changed input. A worker cancels unpaid expired orders under the same sales locks used for payment and fulfillment. Keep ledger stock unchanged until issue and include unpaid orders in admin Orders.
- [ ] Add `/checkout` in `src/public-web/src/App.tsx` with contact, delivery or pickup, invoice address with same-address option, payment selection, and review. Reuse the cart and retain it on failed submission. Test rural and unsupported postcodes, stale quotes, exact cents, concurrent orders contending for the last item, and cancellation racing payment.

### 3. Hosted card payment and invoice posting

- [ ] Add `IPaymentGateway` under `src/backend/Application/Checkout` and `StripePaymentGateway` under `src/backend/Infrastructure/Checkout`. Support starting, expiring, and looking up a hosted payment, verifying events, and initiating and looking up refunds. Add audited payment attempts with unique provider references and a processed-event table with unique provider event IDs.
- [ ] Add `POST /api/checkout/orders/{id}/payment-session` for an authorized shopper and `POST /api/payments/webhooks/stripe`. Persist the attempt before calling Stripe. Retry with the same provider idempotency key and reconcile ambiguous results. Verify signatures over the raw body and check environment, merchant, NZD amount in minor units, and order reference. Keep provider calls outside database transactions.
- [ ] Finalize a verified capture under the order lock. Post one payment and one invoice, and enqueue invoice delivery in the same transaction. Use the provider timestamp as metadata and current server time for posting. If the reservation was released, reacquire it atomically or record the capture for staff reconciliation. Show payment pending after redirect until server status confirms payment.
- [ ] Test invalid signatures, wrong amount or merchant, duplicate and out-of-order events, a crash after session creation, and late capture with and without available stock.

### 4. Customer order history and notifications

- [ ] Add `GET /api/customer/orders`, `GET /api/customer/orders/{id}`, and `GET /api/customer/orders/{id}/invoice`. Add a rate-limited `POST /api/guest/orders/access` to exchange an emailed expiring token for narrowly scoped access. Apply ownership checks and private no-store responses to order and document reads.
- [ ] Add confirmation, order history and tracking screens under `src/public-web/src/components`. Show delivery fees, shipment status, tracking, credits, and pending or completed refunds. Pay-later confirmations show the unpaid status, due time, and configured payment instructions.
- [ ] Add `IEmailSender` and an audited notification outbox with unique message keys, retry leases, and failure visibility. Reuse `InvoicePdfRenderer` for invoice delivery. Document SPF, DKIM, DMARC, sender and reply-to checks. Test recovery after a worker interruption, retry after provider failure, and guest access to PDFs.

### 5. Online refunds and customer requests

- [ ] Add `POST /api/customer/orders/{id}/return-requests` with the same order-scoped access for verified guests. Add a staff review queue in `src/admin-web/src/features/sales`. Customer submissions create requests for review and do not post a return, credit, or refund.
- [ ] Extend `AfterSaleService.cs` and `AfterSaleControllers.cs` with gateway refund initiation from the existing approval workflow. Add a migration for customer requests and gateway refund attempts linked to the original captured payment. Reserve the refundable amount under existing locks, persist intent before calling the gateway, and reconcile partial, full, pending and failed refunds through verified events or lookup. Financial refunds leave stock movements to the separate physical return workflow.
- [ ] Test concurrent partial refunds, duplicate events, provider timeouts, unauthorized approval, and reconciliation after failed or late events.

### 6. Verify and document

- [ ] Add `CheckoutTests.cs` under `tests/Backend.UnitTests`, `CheckoutPostgresTests.cs` under `tests/Backend.IntegrationTests`, storefront component tests and `tests/browser/checkout.spec.ts`. Run the backend suites against disposable PostgreSQL, apply the migrations to a Phase 5 database, and verify existing admin sales, storefront cart, and staff authentication behavior.
- [ ] Run storefront and admin test suites and builds, then `npm run test:browser -- --grep checkout` on desktop and mobile. Exercise successful, cancelled and expired sandbox payments, pay-later confirmation, and one partial refund. Verify emails in a test inbox and confirm pending and failed operations are visible to staff.
- [ ] Record approved decisions in `docs/decisions`, plus sandbox configuration, event replay, expiry, and refund reconciliation instructions in `docs/phase6-checkout.md`. Use environment configuration for secrets. Live payment activation requires separate authorization.
