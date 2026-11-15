# Project: Vehicle Spare Part Shop ERP

## Tech stack
- Backend: .NET 10 (ASP.NET Core Web API), EF Core with Npgsql, PostgreSQL
- Frontend: React (latest) + TypeScript + Vite, two apps in one monorepo:
  - `admin-web` (use a proven React admin dashboard template/library, e.g. MUI or shadcn/ui based)
  - `public-web` (attractive, modern, mobile-first storefront UX)
- Auth: JWT access token + refresh token, role-based, plus per-user menu permissions

## Repo layout
/src/backend/{Domain,Application,Infrastructure,Api}   (Clean Architecture)
/src/admin-web
/src/public-web
/docs  (architecture decisions, ERD, API notes)
/docker-compose.yml  (PostgreSQL + pgAdmin)

## Domain rules (must always hold)
1. Soft delete for Categories and Items (IsDeleted, DeletedAt, DeletedBy). Global EF query filter.
2. Stock is NEVER stored as an editable number. All stock changes are rows in an
   append-only `stock_movements` ledger. Stock at any date = SUM(qty_change) WHERE occurred_at <= date.
3. Every GRN line stores the unit cost and selling price as of the GRN date (price history for later reference).
4. Reorder level and reorder quantity are optional (nullable) per item.
5. Money uses `numeric(18,2)`. All timestamps stored as UTC (`timestamptz`).
6. Every table has audit columns: CreatedAt, CreatedBy, UpdatedAt, UpdatedBy.
7. Never put secrets in the repo. Use user-secrets / environment variables.

## Working rules
- Work ONLY on the current phase. Do not build features from later phases.
- Before coding: write a short plan (files to create, DB changes, endpoints). Wait for my approval.
- Use EF Core migrations; never edit a migration already committed.
- Write unit/integration tests for business logic (stock calculation, GRN, invoice, returns).
- Validate all inputs (FluentValidation). Return ProblemDetails for errors.
- After each phase: list what was built, how to run it, how to test it, and open questions.
- Commit in small logical steps with clear messages.

## Cross-phase design and delivery rules
- Existing Phase 0-7 files define the sequence. Editing plans does not approve implementation; select and approve each phase before coding.
- Each implementation plan must list files, migrations, endpoints, UI, tests and unresolved decisions. Record approved decisions in /docs.
- Audit every persisted table, including join/Identity tables, tokens and integration jobs. Define anonymous/system actors and immutable-row UpdatedAt/UpdatedBy semantics.
- Posting is transactional and idempotent. Database constraints and an explicit PostgreSQL concurrency strategy prevent duplicate postings, overselling and over-returning.
- Enforce ledger immutability at database level. Correct posted documents using linked reversals; never overwrite stock or delete financial history.
- On-hand derives only from movements. Available stock subtracts active unexpired reservations; reservations never alter on-hand.
- Preserve receipt/sale prices and document/contact snapshots. Currency is NZD; agree quantity precision, discount/delivery rounding and valuation before the relevant phase.
- Distinguish business-effective time from recording time. Use Pacific/Auckland with daylight-saving-aware calendar-date boundaries while persisting UTC timestamps.
- Menu visibility never replaces server authorization. Define role inheritance, action permissions and user override precedence.
- Public DTOs exclude costs, staff identities and stock internals. Checkout recalculates all totals server-side.
- Use real PostgreSQL integration tests for transactions, constraints, migrations and concurrency.

## Decisions to resolve before the relevant phase
Part manufacturer versus vehicle make; multiple fitments; quantity precision and valuation; order/payment/fulfillment lifecycles; reservation expiry; delivery zones/fees; discount/delivery rounding; refund approval policy; guest accounts; NZ payment-provider choice; Azure service/SKU and recovery objectives. Single location, NZD/Pacific-Auckland, no negative stock, no backdating, no GST registration, Azure application hosting and Prisma Postgres initially on its Free plan are confirmed. Other proposals remain unapproved until confirmed.


## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Required delivery, GST and refund coverage
- Phase 4 owns delivery/tax settings, delivery fulfillment, non-GST invoices/credit notes and manual refund accounting; Phase 6 adds public shipping quotes, hosted payments and gateway refunds; Phase 7 verifies deployment and recovery.
- Confirmed: the shop is not GST-registered. Charge no GST on items or delivery; do not describe prices as GST-inclusive, issue a GST tax invoice, or model these sales as zero-rated. Persist tax treatment as NotRegistered and sales GST amount as zero. Any future registration requires separate approval and prospective configuration/migrations; historical documents remain unchanged.
- No backdated invoice, payment, refund, issue, return or adjustment postings. Supplier/bank external event dates remain metadata. Late gateway events record both provider time and current system posting time.
- Cloud assumption: Azure application services plus externally hosted Prisma Postgres, not Azure SQL Database. Start with Free database service and explicitly review limits, backup gaps and upgrade triggers before release.

## Reference sources (recheck during implementation)
- [Prisma PostgreSQL connections](https://www.prisma.io/docs/postgres/database/connecting-to-your-database)
- [Prisma pricing](https://www.prisma.io/pricing)
- [Prisma backup/Free-plan guidance](https://www.prisma.io/docs/full-stack-tutorial)
- [IRD charging GST](https://www.ird.govt.nz/gst/charging-gst)
- [IRD taxable supply information](https://www.ird.govt.nz/gst/tax-invoices-for-gst)
- [IRD supply corrections](https://www.ird.govt.nz/gst/tax-invoices-for-gst/credit-and-debit-notes)

