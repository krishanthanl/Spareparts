# Phase 4b: After the sale

Delivery zones and quotes, shipments, returns, credit notes and manual refunds. Decisions: `docs/decisions/0006-after-sale.md`. Design: `Plan/Phase4b-Design-Proposal.md`.

## What staff can do

- **Sales → Delivery settings** (Admin): turn delivery and pickup on or off, set a free-delivery threshold, and add zones made of postcode ranges with a charge, a rural surcharge and a service promise.
- **Order editor**: for delivery, enter the postcode (and tick rural). The quote appears, and *Use quote* fills the charge. A different charge needs a reason.
- **Invoice screen → after-sale panel**: the balance (credits, refunds, owed to the customer), plus *New shipment*, *Return goods*, *Credit note* and *Refund*, with lists of each.
- **Sales → Shipments**: dispatch (stock goes down once) and mark delivered (no stock change).
- **Sales → Returns / Credit notes / Refunds**: lists with search. Credit note PDF. Refund approve, paid-out and failed actions.
- **Daily takings**: the refunds column and refund rows are now filled.

## API

| Method | Route | Permission |
|---|---|---|
| GET / PUT | `/api/delivery-settings` | sales.invoice / delivery.configure |
| GET / POST / PUT | `/api/delivery-zones[/{id}]` | sales.invoice / delivery.configure |
| POST | `/api/delivery-quote` | sales.invoice |
| GET | `/api/orders/{id}/after-sale`, `/api/orders/{id}/balance` | sales.invoice |
| GET / POST | `/api/shipments`, `/api/orders/{id}/shipments` | fulfillment.update |
| POST | `/api/shipments/{id}/dispatch` \| `/deliver` \| `/cancel` | fulfillment.update |
| GET / POST | `/api/sales-returns[/{id}]`, `/api/orders/{id}/returns` | sales.return |
| GET | `/api/credit-notes[/{id}]`, `/api/credit-notes/{id}/pdf` | sales.invoice |
| POST | `/api/orders/{id}/credit-notes` | creditnotes.issue |
| GET / POST | `/api/refunds[/{id}]`, `/api/orders/{id}/refunds` | refunds.create |
| POST | `/api/refunds/{id}/approve` \| `/cancel` | refunds.approve |
| POST | `/api/refunds/{id}/complete` \| `/fail` | refunds.execute |

Migration: `Phase4bAfterSale` (new tables, new order columns, triggers in `AfterSaleSql`). Its Down restores the 4a order guard.

## Tests (results 2026-11-21, on the dev machine)

- `./scripts/dotnet.ps1 test tests/Backend.UnitTests`: **198 passed** (19 new). They cover postcode parsing, zone matching and overlap, quotes with threshold and rural surcharge, cumulative return credit (whole line credits exactly), price-adjustment caps, refund limit and allocation, balance and payment status with credits and refunds, rejection of injected GST and total fields, validators, and the migration Down guard.
- PostgreSQL integration tests, run against a **disposable local PostgreSQL 17** (`TEST_POSTGRES_CONNECTION`): **49 passed**. That is the 43 earlier tests (including the 11 Phase 4a sales tests, run on a real database for the first time) plus 6 new ones:
  - delivery quotes, unsupported postcode, overlap rejected, quote enforcement and permission, invoice unchanged after a zone edit
  - dispatch issuing exactly once under three concurrent retries, delivered not moving stock, pending shipments holding quantity
  - restock versus write-off stock, an idempotent return retry, two racing returns for the last unit, exact credit total ($29.00 over three single-unit returns), over-return rejected, credit note PDF
  - standalone line and delivery credit caps, the invoice total unchanged, credit notes immutable in the database
  - a concurrent over-refund (exactly one wins), the requester cannot approve, idempotent completion, a voided receipt blocked once refunded, the takings refund column
  - a cancellation refund followed by cancelling the invoice, which releases the reservation
- `npm run test --workspace=@spareparts/admin-web`: **37 passed** (1 new for returns and own-refund approval). Typecheck, lint, Prettier and build are clean.
- **Browser check** against the API on a throwaway local database (not the hosted one): a return with one restocked and one written-off line issued CN-000001 for $57.50, and the balance showed $57.50 owed. A refund request showed *Approve* disabled for the requester. The delivery settings page listed the zone. A new delivery order showed the Waikato quote.

## Not done / open

- Approving and paying out a refund as a second admin was tested in PostgreSQL, not clicked through in the browser.
- The hosted Prisma database has not been migrated to `Phase4bAfterSale`. Run the migration there when you deploy.
- Online checkout, gateway refunds and emailing documents remain Phase 6.
