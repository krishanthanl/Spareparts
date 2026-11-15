# Foundation architecture

## Layout and references

```text
src/backend/
  Domain/             Audit and soft-delete contracts; no external references
  Application/        Actor abstraction, shop-time rules; references Domain
  Infrastructure/     EF/Npgsql persistence and audit interceptor; references Application
  Api/                HTTP, DI, errors, validation, health, logging; composes the above
src/admin-web/        React/TypeScript/Vite + free MUI admin foundation
src/public-web/       Independent React/TypeScript/Vite public shell
scripts/              Local services/configuration helpers
 tests/               Backend unit/API, opt-in PostgreSQL and browser tests
Plan/                 User requirements and approval-gated phases
 docs/                Architecture, configuration, testing and decisions
```

Application uses FluentValidation contracts but does not reference ASP.NET Core. Api scans Application validators; its async MVC filter executes them before controller actions. No business validators/controllers exist yet. Test-only controllers exercise the complete validation/error flow and are never included in the deployed API.

## Persistence conventions

AppDbContext currently has no entities/tables. Future entities inherit AuditableEntity; join/support entities must also satisfy the master audit contract. Add business configurations before calling the base OnModelCreating so shared conventions are applied. Decimal properties default to numeric(18,2); future quantity fields may explicitly override precision after approval. DateTimeOffset uses PostgreSQL timestamp with time zone (timestamptz).

AuditSaveChangesInterceptor sets CreatedAt/By and UpdatedAt/By on insert, preserves persisted creation fields on updates and updates the modification fields. Removing an ISoftDeletable entity becomes an update of IsDeleted/DeletedAt/DeletedBy. Global query filters exclude deleted rows; restricted later workflows may explicitly bypass them. Future restore handlers must clear all deletion metadata.

Actors are system outside HTTP, anonymous before authentication and the authenticated name-identifier once Phase 1 supplies it. No user table is implied by the reserved system/anonymous strings.

The interceptor is not a ledger-immutability mechanism. Phase 3 must separately enforce immutable stock movements at database level, retain creation-time update metadata for immutable rows and introduce transactional/idempotent postings. No stock balance or ledger exists in Phase 0.

## Time and money

ShopTime converts explicit instants to Pacific/Auckland and local calendar days to UTC [start, next-day-start) boundaries. DST days can be 23/25 hours; never add 24 hours to a local-day boundary. A shared injected TimeProvider supplies server posting/audit time. Frontends use en-NZ/NZD and explicit shop-time formatting.

NotRegistered tax treatment and zero sales GST are confirmed requirements for later document entities; this foundation does not implement a tax engine or mutable tax administration.

## HTTP foundation

- /health/live: process health, independent of PostgreSQL.
- /health/ready: PostgreSQL connectivity; generic health response, 503 on failure. This is not yet schema-version readiness because no schema exists.
- /swagger and /swagger/v1/swagger.json: development only. No business API operations yet.
- Other routes: ProblemDetails 404. Validation failures: 400 with field errors; unexpected failures: safe 500.
- Requests carry a validated/generated X-Correlation-ID. ProblemDetails adds traceId and correlationId. Logs omit request bodies, query strings, auth/cookies, connection strings and exception messages.
- Exact configured frontend origins, explicit headers/methods and credential-capable CORS. Authentication/CSRF/session policy belongs to Phase 1.

## Frontends

Separate npm workspaces prevent cross-app bundle coupling. Each app owns its Axios client, TanStack Query provider, router, recoverable error boundary and shop formatting. Access-token storage is not implemented. MUI Material is the free admin component library; no commercial templates/components were copied. Keep package licenses and attribution when distributing dependencies.

## Hosting boundary

Development uses Docker PostgreSQL/pgAdmin. Release target is Azure applications plus external Prisma Postgres, initially Free. EF Core/Npgsql and EF migrations remain authoritative. Validate provider permissions/extensions, pool budgets, TLS, worker reliability, independent backups and NZ-adjacent latency before hosted release. Azure Blob Storage is proposed for production media; no cloud SDK or storage resources are added now.
