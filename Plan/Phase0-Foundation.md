Phase 0: Foundation.
1. Create the solution with Clean Architecture (Domain, Application, Infrastructure, Api) on .NET 10.
2. Add docker-compose.yml with PostgreSQL and pgAdmin.
3. Configure EF Core + Npgsql, base entity with audit fields, soft-delete interface,
   global query filters, and a SaveChanges interceptor that fills audit fields.
4. Add Serilog, global exception handling (ProblemDetails), Swagger/OpenAPI, health check, CORS for two frontends.
5. Scaffold `admin-web` and `public-web` (React + TS + Vite), with ESLint/Prettier,
   an API client layer (axios), and React Query.
6. Write /docs/architecture.md with a folder diagram and the conventions used.
Do not implement any business feature yet. Show your plan first.

## Implementation detail
- Domain has no infrastructure dependencies; Application depends on Domain; Infrastructure implements Application abstractions; Api composes dependencies.
- Add backend unit and real PostgreSQL integration test projects under /tests, frontend test setup, lockfiles and placeholder-only environment examples.
- Define anonymous/system audit actors, UTC and decimal conventions. Prepare query-filter/audit behavior without introducing catalogue tables.
- Runtime and migration accounts have separate privileges. Create migrations only for actual approved schema.
- Logs use correlation IDs and redact secrets/tokens. Production ProblemDetails omit stack traces. CORS permits exact configured frontend origins.
- Propose the admin library/template and license before adoption. No authentication or business screens yet.

## Files and endpoints
Plan the four backend projects, two frontend shells, tests, Compose, README and /docs/architecture.md. Proposed endpoints: GET /health/live and GET /health/ready. Readiness checks PostgreSQL without disclosing connection details.

## Acceptance
Backend and both frontend apps build; lint passes; Compose starts; readiness detects database failure/recovery. Verify audit conventions, error shapes and allowed/rejected CORS origins. Document prerequisites, startup, migration and test commands.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## NZ and cloud foundation
Configure an injectable clock, Pacific/Auckland conversion and NZD/en-NZ display defaults. Add test infrastructure for NZ daylight-saving boundaries; do not build tax/delivery business features yet.
Retain local Docker PostgreSQL/pgAdmin for development and automated tests. Document Azure-to-Prisma TLS connectivity, pooled runtime versus direct migration connections and conservative total connection budgeting across API/workers.
Verify Prisma-supported triggers, locking, roles and required extensions before relying on them. Do not weaken ledger rules to fit hosting limits; raise unsupported requirements.
Use external durable object storage for production images/PDFs (Azure Blob is the proposed choice); local disk is development-only. Select compatible PostgreSQL versions after checking the target service.

