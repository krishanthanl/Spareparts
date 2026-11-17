# Database connection configuration

Store real hosts, accounts, passwords and working connection strings in user-secrets or deployment environment settings. This file documents placeholders only.

## Configuration contract
Proposed backend key: `ConnectionStrings:DefaultConnection`; environment equivalent: `ConnectionStrings__DefaultConnection`. Confirm the name during Phase 0.

```text
Host=<database-host>;Port=<database-port>;Database=<database-name>;Username=<application-user>;Password=<secret>
```

Host-run API uses the published local PostgreSQL port. Compose-run API uses the database service name/container port, not localhost. Production follows provider TLS/certificate requirements; do not disable validation to bypass errors. Separate restricted runtime credentials from migration privileges.

## Local setup and verification
After Api exists, initialize its user-secrets and set the connection key locally. Supply Compose credentials through ignored environment files; commit placeholder-only `.env.example` files. Vite variables are public and must never contain secrets.

Avoid logging credentials or placing them in screenshots/shared shell history. Rotate credentials previously committed; deletion does not erase Git history. Verify readiness, safe authentication failures and absence of secrets in source/build artifacts. Add exact commands to the Phase 0 README once configuration is approved.

## Confirmed hosting arrangement
Azure hosts the applications; Prisma Postgres hosts PostgreSQL on its Free plan initially. Keep EF Core/Npgsql and EF migrations; Prisma hosting does not require Prisma ORM.
Use the provider's standard PostgreSQL TCP credentials with verified TLS; a prisma:// or prisma+postgres:// proxy URL is not an Npgsql connection string. Map credentials into Npgsql configuration without exposing them.
Prefer a provider-supported pooled endpoint for runtime after compatibility testing; use direct PostgreSQL connectivity for migrations/backup. Budget total connections across all Azure instances/workers against current provider limits.

Verify restricted-role and trigger permissions on the actual plan. Keep local Docker PostgreSQL for development/tests. Recheck limits and independent backups before release.
Reference: https://www.prisma.io/docs/postgres/database/connecting-to-your-database



