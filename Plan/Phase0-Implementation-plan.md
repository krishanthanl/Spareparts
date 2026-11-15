# Phase 0 Foundation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task after user approval. Steps use checkboxes for tracking. No delegation is requested.

**Goal:** Build a runnable API and two frontend shells, with database/configuration conventions and foundation tests, without implementing business features.

**Architecture:** Domain is independent; Application references Domain; Infrastructure implements Application interfaces; Api composes the application. Both React apps use a root npm workspace and separate entry points.

**Tech Stack:** .NET 10, EF Core 10 with compatible Npgsql provider, PostgreSQL, React/TypeScript/Vite, Axios, TanStack Query, Serilog, FluentValidation, xUnit and frontend Vitest/Testing Library.

**Spec:** `Plan/Masterprompt.md` and `Plan/Phase0-Foundation.md`.

**Status:** Implementation approved and delivered. Real PostgreSQL/Compose runtime verification remains pending a working Docker engine or disposable database. See docs/testing.md and docs/phase0-progress.md.

## Global constraints
- One shop, one stock location; no branch/warehouse features.
- NZD, en-NZ, Pacific/Auckland; persist UTC timestamptz and numeric(18,2) money.
- Not GST-registered; no GST charged. Negative stock and backdating are forbidden.
- All persisted tables require CreatedAt, CreatedBy, UpdatedAt, UpdatedBy.
- No secrets in source. EF migrations only; never edit a committed migration.
- Azure application hosting and Prisma Postgres Free initially; no cloud provisioning in Phase 0.
- No authentication, catalogue, inventory, sales, delivery, refund or checkout implementation in this phase.

## Current environment and proposed choices
- .NET 10 SDK and Node/npm are available. Docker is not currently available on PATH; database-backed verification requires a working Docker engine or a separately supplied disposable PostgreSQL database.
- No Git repository exists yet. Initialize the current folder after approval, retain existing Plan/.agents files and create logical local commits. No push or remote creation.
- Recommend Material UI's free dashboard layout/components for admin, verifying license and retaining required attribution; no paid components. Public app receives a minimal responsive foundation shell, with storefront design deferred to Phase 5.
- Root npm workspaces avoid extra monorepo tooling. Pin resolved compatible stable versions and commit lockfiles.
- Default audit actor is `system` for non-request operations and `anonymous` for unauthenticated requests; Phase 1 supplies authenticated user IDs.

## Review focus
1. Database outage: readiness returns failure safely; liveness remains healthy.
2. Invalid configuration/CORS: fail clearly without leaking secrets; reject unauthorized origins.
3. Audit/soft-delete behavior: inherited and test entities follow the same conventions.
4. NZ daylight-saving boundaries: calendar dates map to correct UTC instants.
5. Frontend failures: network errors render a usable shell without exposing internals.

## Task 1: Repository and solution
**Create:** `.gitignore`, `.editorconfig`, `global.json`, `Spareparts.slnx`, `Directory.Build.props`, `package.json`, lockfiles; projects at `src/backend/{Domain,Application,Infrastructure,Api}`; `tests/Backend.UnitTests` and `tests/Backend.IntegrationTests`.
- [x] Initialize Git; add solution/workspace configuration and project references with the required boundaries.
- [x] Add architecture dependency checks; restore/build and verify project references.
- [x] Commit the repository/solution foundation without secrets or generated outputs.

## Task 2: Persistence, audit and shop time
**Create:** Domain audit/soft-delete contracts; Application `ICurrentActor` and shop-time abstractions; Infrastructure `AppDbContext`, model conventions, audit interceptor, design-time factory and dependency registration; focused tests in both backend test projects.
**Interfaces:** `ICurrentActor.ActorId : string`; injectable .NET `TimeProvider`; shop-time conversion accepts DateOnly and returns UTC start/exclusive-end boundaries.
- [x] Write tests for creation/update audit fields, soft-delete filtering, numeric/timestamp metadata and Pacific/Auckland DST boundaries; observe failures before implementation.
- [x] Implement the contracts and conventions; use test-only entities/schema to verify database behavior without introducing business tables.
- [ ] Run unit and PostgreSQL tests. **Unit tests passed; real PostgreSQL execution is pending available infrastructure.** No application migration is required until an actual persistent model exists; do not add a dummy business table or empty migration.
- [x] Commit persistence/time foundation.

## Task 3: API and local services
**Create:** Api startup/configuration, exception handler, correlation middleware, validation integration, health checks and configuration examples; `docker-compose.yml`, `.env.example`; Infrastructure database role bootstrap script and configuration guidance.
**Endpoints:** `GET /health/live` (process only), `GET /health/ready` (PostgreSQL dependency). Development OpenAPI/UI only; no business endpoints.
- [x] Add tests for ProblemDetails, safe production errors, allowed/disallowed CORS origins, configuration validation and healthy/unhealthy database responses.
- [x] Implement Serilog redaction, correlation IDs, FluentValidation integration, exact frontend origins and checks. Use test-only endpoints to exercise errors/validation.
- [x] Configure PostgreSQL/pgAdmin with external secrets, persistent volumes and health checks; document distinct restricted runtime versus migration credentials.
- [ ] Verify API tests and Compose startup when Docker is available. **API checks pass and Compose configuration validates; service startup remains pending the missing Docker engine.**
- [x] Commit API/local-service foundation.

## Task 4: Frontend shells
**Create in each `src/{admin-web,public-web}`:** `package.json`, `index.html`, Vite/TypeScript/ESLint configuration, `src/main.tsx`, `src/App.tsx`, routing, `src/api/client.ts`, query provider, error boundary, NZ formatting helpers and shell tests; root Prettier configuration.
- [x] Add tests for shell navigation, loading/error behavior and NZD/date formatting.
- [x] Build admin navigation/layout with free MUI components and a minimal public shell. Do not create login/catalogue pages or fabricated business metrics.
- [x] Configure independent development ports, environment-based public API URL, Axios and TanStack Query. No secrets in Vite configuration.
- [x] Run frontend tests, TypeScript checks, lint and production builds for both workspaces; verify shells in a browser when available.
- [x] Commit frontend foundation.

## Task 5: Documentation and final verification
**Create:** `README.md`, `docs/architecture.md`, `docs/configuration.md`, `docs/testing.md`, `docs/decisions/0001-foundation.md`.
- [x] Document setup/run/test commands, dependency boundaries, actor semantics, UTC/NZ conventions, secret handling and migration workflow.
- [x] Explain local Docker versus hosted Prisma configuration, verified TLS, pooled runtime/direct migration connections, pool budgeting and permission/extension verification required before hosted release.
- [x] Document future Azure media persistence and independent database backups without provisioning them now.
- [x] Run backend build/tests and frontend checks; validate configuration examples and check for accidental secrets. Record any Docker-dependent verification still blocked.
- [x] Commit documentation and provide delivered files, exact run/test instructions, actual results and open questions. Stop before Phase 1.

## Approval requested
Approve Phase 0 scope, free MUI admin foundation, npm workspaces, local Git initialization and the audit actor conventions above. Docker availability affects integration verification, not authorization to implement the remaining foundation.
