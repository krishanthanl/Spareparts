# ADR 0001: Phase 0 foundation

Status: accepted through the approved Phase 0 implementation plan.

- Keep the four-layer .NET solution and two separate Vite apps. Use npm workspaces, not extra monorepo orchestration.
- Use free MUI Material components for admin. Public receives a minimal mobile-first shell; full catalogue design is Phase 5.
- Keep PostgreSQL-specific persistence using EF Core/Npgsql. Prisma is the future database host, not the ORM.
- No production tables/migrations until a real domain model exists. Test entities isolate convention verification.
- System/anonymous actors are reserved strings; authenticated IDs arrive in Phase 1.
- NZD/Pacific-Auckland and non-GST shop behavior are fixed defaults. Business backdating and negative stock are forbidden, without an admin bypass.
- Reuse the approved checkout on phase0-foundation and make local commits. No remote, push, deployment or paid service creation is authorized by this phase.
- Use checkout-local SDK/package caches where needed. npm 11.6.2 is a local install runner because installed npm 10.9.0 crashed in peer resolution. No global upgrade.

References: [MUI free templates](https://mui.com/material-ui/getting-started/templates/), [Prisma connections](https://www.prisma.io/docs/postgres/database/connecting-to-your-database).
