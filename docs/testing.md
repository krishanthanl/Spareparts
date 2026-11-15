# Foundation verification

## Automated coverage

- Backend unit/API: dependency boundaries, empty production model, decimal/timestamp metadata, soft-delete SQL/filtering and audit interception; NZ standard/DST boundaries; liveness/readiness failure; exact CORS; safe errors and validation field errors.
- PostgreSQL integration: opt-in, test-only entities and random isolated schema; real persistence, UTC, decimal round trip, audit and soft deletion. No application schema is migrated or modified.
- Frontend: navigation, shop defaults, NZD/date formatting, safe network-error retry and render-error recovery in both apps.
- Browser smoke checks: desktop/mobile shells, navigation, no horizontal overflow, console errors and API connection states. Servers use a dummy database, not development/production credentials.

## Commands

Run from the root:

```powershell
./scripts/dotnet.ps1 test Spareparts.slnx
npm test
npm run typecheck
npm run lint
npm run build
npm run format:check
npm run test:browser
```

Enable the PostgreSQL test with RUN_POSTGRES_TESTS=1 and Docker, or TEST_POSTGRES_CONNECTION for an explicitly disposable database with schema creation permission. Opting in with unavailable infrastructure must fail, not silently skip. Remove the environment value after the run.

## Environment limitations

Docker/psql are not available in the current task environment. Real PostgreSQL and Compose startup cannot be claimed verified until run in a suitable environment. Default test output reports the database test as skipped. Hosted Prisma compatibility, restricted-role SQL and healthy database readiness/recovery remain explicit release checks.

The restricted execution environment cannot connect the .NET test runner to its local test host; local-network-enabled execution is required there. This does not require production access and does not change application behavior.

Browser tests use the installed Chrome channel by default; PLAYWRIGHT_CHANNEL can choose msedge or downloaded chromium. Screenshots/reports and temporary test server logs are ignored. The storefront shell remains noindex until the public catalogue phase.

## Later-phase tests

No stock/GRN/invoice/return implementation exists yet. Their transaction, race, idempotency, non-GST and no-backdating tests belong to the approved business phases and must use real PostgreSQL for database guarantees.

## Recorded Phase 0 results (2026-11-15)

20 backend unit/API tests, 10 frontend tests and 6 browser smoke checks passed. Both frontend production builds, type checks, lint and formatting passed. Backend builds pass. The real PostgreSQL test compiles but is explicitly skipped (1); live Compose/database/role-grant/readiness-recovery verification remains pending infrastructure. Compose configuration parsed successfully with the available standalone plugin. These results do not establish production or hosted Prisma readiness.
