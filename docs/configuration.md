# Configuration and secrets

## Local services

Copy the root .env.example to ignored .env, replace placeholders, then run docker compose up -d. Service ports bind to loopback. PostgreSQL uses a durable volume; pgAdmin has its own volume. The initialization SQL runs only on a new PostgreSQL volume. Changing .env later does not rotate existing role passwords; rotate roles explicitly without deleting shop data.

Local roles:
- spareparts_admin: service bootstrap/admin only.
- spareparts_migrator: schema owner and DDL; use for EF migrations, never the API.
- spareparts_app: connection, schema usage and default table DML; cannot change schema. Phase 3 must revoke ledger mutation access and add database protection before stock posting exists.

## Backend settings

| Key | Purpose |
| --- | --- |
| ConnectionStrings:DefaultConnection | Restricted runtime Npgsql connection |
| ConnectionStrings:MigrationConnection | Direct migration connection (environment for EF factory) |
| Cors:Origins:0 and :1 | Exact frontend origins, no slash/path/wildcard |
| AllowedHosts | Accepted API hostnames; configure actual Azure/custom domains at release |
| ASPNETCORE_ENVIRONMENT | Development locally; Production in Azure |

Environment keys use double underscores, for example ConnectionStrings__DefaultConnection and Cors__Origins__0.

The checked-in connection is deliberately blank. Startup fails safely until a real connection is configured. Use scripts/configure-local.ps1 for an interactive local password prompt; it sends JSON to user-secrets through standard input instead of putting a password in command arguments. .NET user-secrets is local development storage, not a production vault.

Placeholder-only runtime shape:

```text
Host=localhost;Port=5432;Database=spareparts;Username=spareparts_app;Password=<local-secret>;Maximum Pool Size=10;Minimum Pool Size=0;Timeout=5
```

Never put a working value in this document, logs, screenshots or version control. Keep application and migration credentials separate. Examples are not usable credentials.

## Prisma and Azure

Use standard PostgreSQL TCP credentials converted to Npgsql key/value format. Prisma proxy URLs (prisma:// or prisma+postgres://) are not Npgsql connections. Production requires SSL Mode=VerifyFull and provider-appropriate certificate trust. Configure all CORS origins/AllowedHosts and forwarded HTTPS behavior for the selected Azure service before release.

A provider-supported pooled connection can serve runtime traffic after compatibility tests; direct connectivity is used for migrations and backups. Budget the sum of pools across API instances and background workers against current provider limits. Recheck role/trigger/locking/extension support against an isolated hosted database; never weaken stock invariants to work around hosting restrictions.

Prisma Free quotas and backup features must be verified at deployment time. Independent encrypted pg_dump backups and restoration checks are required before release. No hosted database or Azure resources are provisioned here.

## EF tools and migrations

```powershell
./scripts/dotnet.ps1 tool restore
# Set ConnectionStrings__MigrationConnection securely outside shell history first.
./scripts/dotnet.ps1 ef migrations add <ApprovedMigrationName> --project src/backend/Infrastructure --startup-project src/backend/Api
./scripts/dotnet.ps1 ef database update --project src/backend/Infrastructure --startup-project src/backend/Api
```

The design-time factory reads the migration environment key first, then the runtime environment key as a development fallback. Explicitly supply the migration key for production. No automatic startup migration runs. Review generated SQL; committed migrations are immutable. No migration is needed for Phase 0's empty production model.

## Frontends

Optional src/admin-web/.env.local and src/public-web/.env.local set VITE_API_BASE_URL (default http://localhost:5080). Every VITE_ value is public. No database, JWT signing, email or payment secrets belong there. Development ports are fixed to 5173/5174 so CORS configuration stays deterministic.
