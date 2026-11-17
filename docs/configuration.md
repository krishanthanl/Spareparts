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
| Jwt:SigningKey | Access-token signing key, at least 32 bytes. Required outside Development; secrets/environment only (`Jwt__SigningKey`) |
| Jwt:Issuer, Jwt:Audience, Jwt:AccessTokenMinutes | Defaults spareparts-api, spareparts-admin, 10 |
| Auth:* | Session/lockout limits (RefreshTokenDays 14, SessionMaxDays 30, MaxFailedAttempts 5, LockoutMinutes 15); defaults match ADR 0003 |

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

For local development against a hosted database, run `./scripts/configure-hosted.ps1` in your own terminal. It asks for the direct `postgres://` URL without echoing it, converts it to Npgsql format with `SSL Mode=VerifyFull`, and saves it to the API's user-secrets as both DefaultConnection and MigrationConnection. A single hosted user then owns the schema and runs the app; separate the roles before release where the provider allows it. Do not point the integration tests at a shop database: they create and drop throwaway databases.

Prisma Free quotas and backup features must be verified at deployment time. Independent encrypted pg_dump backups and restoration checks are required before release. No hosted database or Azure resources are provisioned here.

## EF tools and migrations

```powershell
./scripts/dotnet.ps1 tool restore
# Set ConnectionStrings__MigrationConnection securely outside shell history first.
./scripts/dotnet.ps1 ef migrations add <ApprovedMigrationName> --project src/backend/Infrastructure --startup-project src/backend/Infrastructure
./scripts/dotnet.ps1 ef database update --project src/backend/Infrastructure --startup-project src/backend/Infrastructure
```

The design-time factory reads the migration environment key first, then the runtime environment key as a development fallback. Explicitly supply the migration key for production. No automatic startup migration runs. Review generated SQL; committed migrations are immutable. Migrations so far: Phase2Catalog, Phase1Auth. The API project does not reference EF design tools, so Infrastructure is both the project and the startup project.

## Catalogue image storage

| Key | Purpose |
| --- | --- |
| Storage:LocalRoot | Folder for uploaded images. Default `.data/media` under the API content root (git-ignored). Keep it out of source control and back it up with the database. |
| Storage:PublicPath | URL path the API serves images from. Default `/media`. |

Images are re-encoded to WebP with random names, so they are served with long immutable caching. Azure Blob storage will replace local disk behind `IFileStorage` before release.

## Loading the sample catalogue

`tools/CatalogImport` loads the sample parts (`parts.json` and images) from the test-project folder. It reads ConnectionStrings:DefaultConnection from the API's user-secrets or the ConnectionStrings__DefaultConnection environment variable, and writes images into the API's storage folder.

```powershell
# Check every image first; writes nothing and needs no database.
./scripts/dotnet.ps1 run --project tools/CatalogImport -- --source D:\GitLabRepos\test-project --dry-run
# Import after the migration is applied. Safe to repeat; existing SKUs are skipped unless --update is given.
./scripts/dotnet.ps1 run --project tools/CatalogImport -- --source D:\GitLabRepos\test-project
```

## Frontends

Optional src/admin-web/.env.local and src/public-web/.env.local set VITE_API_BASE_URL (default http://localhost:5080). Every VITE_ value is public. No database, JWT signing, email or payment secrets belong there. Development ports are fixed to 5173/5174 so CORS configuration stays deterministic.

## First SuperAdmin

After applying migrations, create the first administrator once, in your own terminal:

```powershell
./scripts/dotnet.ps1 run --project tools/AdminBootstrap -- --email you@example.com --name "Your name"
```

It reads the same user-secrets connection as the API, asks for the password without echoing it (or reads `BOOTSTRAP_PASSWORD`), seeds roles and menus, and refuses to run if an active SuperAdmin already exists. Add other staff from the admin app (Users).
