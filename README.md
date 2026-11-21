# Vehicle Spare Part Shop ERP

Phase 0 provides the development foundation only: a .NET 10 API, PostgreSQL conventions and two React app shells. Authentication, catalogue, stock, sales and checkout are intentionally not implemented yet.

## Shop rules

One shop and one stock location. NZD, en-NZ and Pacific/Auckland (including daylight saving). UTC persistence. Not GST-registered: no GST on goods/delivery. Negative stock and backdating are forbidden when business posting is introduced.

## Prerequisites

- .NET SDK 10.0.404 (or a compatible 10.0.4xx patch).
- Node.js 22.12+ compatible with the pinned tools; npm 11.6.2 recommended for dependency installation. The original npm 10.9 resolver crashed on this dependency graph.
- Docker Engine/Desktop with Compose for local PostgreSQL/pgAdmin and container tests, or an explicitly disposable PostgreSQL database for integration tests.
- Git. No cloud account/production connection is needed for Phase 0.

## Start locally

From the repository root in PowerShell:

1. Copy `.env.example` to `.env` and replace every placeholder with distinct local credentials. Never commit that file.
2. Start services:

```powershell
docker compose up -d
```

3. Configure the API's local runtime password without entering it into command history. Supply the same APP_DB_PASSWORD used in .env when prompted:

```powershell
./scripts/configure-local.ps1
```

4. Restore and run the API:

```powershell
./scripts/dotnet.ps1 restore Spareparts.slnx --locked-mode
./scripts/dotnet.ps1 run --project src/backend/Api --launch-profile http
```

5. In another terminal, install and start either frontend:

```powershell
npx --yes npm@11.6.2 ci --cache .local/npm-cache
npm run dev:admin
# In a separate terminal:
npm run dev:public
```

| Service | Local address |
| --- | --- |
| API | http://localhost:5080 |
| API liveness | http://localhost:5080/health/live |
| API database readiness | http://localhost:5080/health/ready |
| Development OpenAPI UI | http://localhost:5080/swagger |
| Admin app | http://localhost:5173 |
| Public app | http://localhost:5174 |
| pgAdmin | http://localhost:5050 |

In pgAdmin, connect to host `postgres`, port 5432 and database `spareparts`; use the appropriate local account. Inside Compose, localhost is not the database host.

Readiness is expected to return 503 while PostgreSQL is unavailable. Liveness remains independent of database availability. Neither frontend has a login or usable shop transactions yet.

## Verify

```powershell
./scripts/dotnet.ps1 test Spareparts.slnx
npm test
npm run typecheck
npm run lint
npm run build
npm run format:check
```

The real PostgreSQL test is explicitly skipped by default. To run it with Docker:

```powershell
$env:RUN_POSTGRES_TESTS = '1'
./scripts/dotnet.ps1 test tests/Backend.IntegrationTests
Remove-Item Env:RUN_POSTGRES_TESTS
```

Alternatively, set `TEST_POSTGRES_CONNECTION` outside shell history to a disposable PostgreSQL database with schema-creation permission. The test creates/drops only a randomly named test schema. Never point tests at production.

Browser smoke checks use installed Chrome by default:

```powershell
npm run test:browser
```

Set `PLAYWRIGHT_CHANNEL=msedge` to use installed Edge, or `chromium` and run `npx playwright install chromium` first. Browser tests start/stop their own API and frontend servers with a dummy unavailable database; they never use the shop database. Their reports are ignored by Git.

## Database changes

The schema includes catalogue, authentication and Phase 3 inventory migrations. Apply changes through EF Core migrations. Tool restore and migration instructions are in [configuration](docs/configuration.md); inventory workflows and verification are in [Phase 3 inventory](docs/phase3-inventory.md).

## Further reading

- [Architecture](docs/architecture.md)
- [Configuration and secrets](docs/configuration.md)
- [Tests and limitations](docs/testing.md)
- [Phase 3 inventory](docs/phase3-inventory.md)
- [Foundation decisions](docs/decisions/0001-foundation.md)
- [Approved Phase 0 plan](Plan/Phase0-Implementation-plan.md)
- [Master requirements](Plan/Masterprompt.md)

Azure application deployment and Prisma Postgres Free hosting are planned for the release phase. No resources, paid services or live payments have been created.
