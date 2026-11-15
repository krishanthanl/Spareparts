# Phase 7: Hardening, deployment and handover

## Scope and approval
Stabilize Phases 0-6 without adding business modules. Azure application hosting and Prisma Postgres Free initially are confirmed. Agree Azure services/SKUs, regions, traffic/data expectations, recovery objectives, operational owner and release blockers. Present files, configuration, migrations and checks before implementation; wait for approval.

## Security and reliability
- Review permissions, customer isolation, JWT/refresh/CSRF, rate limits, upload safety, public DTOs and password recovery needs.
- Configure HTTPS, exact CORS origins, safe headers, secret rotation and separate runtime/migration accounts. Do not expose pgAdmin publicly.
- Review dependencies/licenses and scan for secrets; redact personal/payment data and define retention.
- Verify ledger protection, posting races, worker restart recovery, gateway reconciliation and durable retries.

## Deployment
- Production builds/configuration for API and both frontends; document persistent images, TLS/domain, database, gateway and email setup.
- Keep local Compose assumptions separate from production architecture.
- CI builds/lints, runs unit and PostgreSQL integration tests and key browser journeys. Untrusted pull requests receive no production secrets.
- Review migration SQL and use a controlled migration step; never edit committed migrations or silently auto-migrate production.
- Add readiness/liveness checks, correlation logs, monitoring and meaningful alerts.

## Recovery and release checks
- Back up database/images with retention; demonstrate isolated restoration and record measured recovery time/data-loss window.
- Document app rollback separately from database recovery; binary rollback cannot reverse financial records.
- Agree performance targets and test realistic catalogue, stock-at-date, ledger and order-list workloads.
- Exercise login/permissions, soft deletion, GRN, issue, return, cart, sandbox payments and invoice delivery.
- Verify mobile/accessibility, PDFs, ledger reconciliation and payment reconciliation.

## Acceptance and handover
Deliver runbook, deployment/recovery instructions, final ERD/API notes, setup guide, actual test results, limitations and open questions. Production deployment requires separate approval of a concrete release candidate. Never test with unauthorized real charges.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Azure applications and Prisma Postgres deployment
- Proposed Azure services: App Service (or an approved container alternative) for API, static hosting for both Vite apps, Blob Storage for media and Key Vault/application settings for secrets. Confirm service tiers/costs and NZ-adjacent regions before provisioning.
- Prisma Postgres is the external database provider, initially Free. Do not substitute Azure SQL or Prisma ORM. Test actual EF/Npgsql transactions, locks, immutable-ledger triggers and migration permissions against a disposable hosted database.
- Use verified-TLS PostgreSQL connectivity, bounded pools and a direct migration connection. Check Azure outbound networking, latency and actual provider region choices.
- Run reservation expiry/reconciliation/email workers on a hosting tier that reliably executes them; do not assume a sleeping free web host provides background execution.
- Recheck official storage/operation/connection quotas and exhaustion behavior before launch. Set monitoring and upgrade triggers for projected quota, capacity, latency and recovery needs; do not hard-code changing Free limits.
- Free Prisma backups must not be assumed. Schedule independent encrypted pg_dump backups over a direct connection into controlled durable storage; test restore and retention. Verify paid-plan features before upgrading rather than assuming point-in-time recovery exists.
- Upgrade to paid Prisma only after cost approval. Document credential rotation, backup verification, connection/configuration changes if any, smoke checks and rollback/recovery.
- Release checks cover single-location totals, no negative stock, rejection of backdating, NZ DST boundaries, non-GST totals/credit reconciliation, delivery and manual/gateway refund recovery.

