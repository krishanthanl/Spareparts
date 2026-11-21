# Phase 4a: Sell and get paid

Decisions: [ADR 0005](decisions/0005-sales.md). Proposal: `Plan/Phase4a-Design-Proposal.md`.

## What was built

- **Backend.** Customers, orders/invoices, stock issue, payments, voids, quick sale, invoice PDF and daily takings. Migration `Phase4aSales`, with tables, check constraints and guard triggers in `Infrastructure/Persistence/SalesSql.cs`.
- **Admin web** (`src/admin-web/src/features/sales`). Order list with filters; order editor with live server totals and available stock; invoice view with Issue stock, Record payment, Void, Cancel, Copy and Print/PDF; Counter sale; Customers; Daily takings (print and CSV).
- **Inventory changes.** Available stock = on-hand − reserved, shown on the stock report. Adjustments and GRN reversals cannot take reserved stock.

## Before using it on the shop database

1. Fill in the `Shop` section (trading name, address, phone, email, bank account, payment terms) in App Service configuration or `appsettings.json`. Without it, invoices show only "Spare Parts".
2. Run the migration: `dotnet ef database update --project src/backend/Infrastructure --startup-project src/backend/Infrastructure` with `ConnectionStrings__MigrationConnection` set. As in Phase 3, it creates triggers, so trigger support on Prisma Postgres must be confirmed first.
3. Restart the API once so the seeder adds the new permission keys to the system roles and switches on the Sales menus.

## API

| Method | Route | Permission |
|---|---|---|
| GET/POST/PUT | `/api/customers`, `/api/customers/{id}` | read: sales.invoice; write: customers.manage |
| GET | `/api/orders?q&status&paymentStatus&fulfillmentStatus&source&from&to&sort&page` | sales.invoice |
| GET | `/api/orders/items?q` (item search with available stock) | sales.invoice |
| POST | `/api/orders/preview` (server totals for an unsaved draft) | sales.invoice |
| POST/PUT | `/api/orders`, `/api/orders/{id}` (drafts) | sales.invoice (+ sales.discount for discounts) |
| POST | `/api/orders/{id}/confirm`, `/cancel`, `/duplicate` | sales.invoice (cancel after Confirm: sales.cancel) |
| POST | `/api/orders/{id}/issues` | sales.issue |
| POST | `/api/orders/{id}/payments` | payments.record |
| POST | `/api/payments/{id}/void` | payments.void |
| POST | `/api/orders/quick-sale` | sales.invoice + sales.issue + payments.record |
| GET | `/api/orders/{id}/pdf`, `/api/stock-issues/{id}` | sales.invoice |
| GET | `/api/reports/daily-takings?from&to[&format=csv]` | sales.reports |

## Tests (results 2026-11-21, on the dev machine)

- `dotnet test tests/Backend.UnitTests`: **179 passed**. New tests cover rounding, cent allocation, payment, fulfilment and order status rules, validators (including rejecting injected GST or total fields), the PDF writer, and translation of the reservation query to SQL.
- `npm run test --workspace=@spareparts/admin-web`: **33 passed** (4 new sales tests). Typecheck and lint are clean.
- `tests/Backend.IntegrationTests/SalesPostgresTests.cs`: **11 new PostgreSQL tests, not run yet.** This machine has no Docker, so they run in GitHub Actions like the Phase 3 tests. They cover:
  - racing confirms for the last unit
  - reserved stock protected from adjustments
  - partial issue, over-issue and idempotent issue
  - racing issues posting once
  - cancel rules, including the database trigger
  - partial, idempotent and concurrent payments, voids and overpayment
  - frozen prices, snapshots and triggers
  - discount permission and reasons
  - gapless invoice numbers under concurrency
  - quick sale, including rollback and the PDF
  - daily takings by method
- **Not checked in a browser.** The screens have not been clicked through against a running API, because that needs a PostgreSQL database with the new migration.

## Left for Phase 4b

Delivery zones and quotes, shipments, returns, credit notes, manual refunds (and the refunds column in daily takings), and cancelling invoices that have payments (needs refunds).
