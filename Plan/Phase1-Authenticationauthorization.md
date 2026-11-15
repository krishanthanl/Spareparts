Phase 1: Auth & RBAC.
Backend:
- Users, Roles, UserRoles tables (custom or ASP.NET Identity, recommend and justify).
- JWT access token (short-lived) + refresh token (rotating, stored hashed, revocable).
  Refresh token in httpOnly cookie, access token kept in memory on the client.
- Roles: SuperAdmin, Admin, StoreKeeper, Cashier (configurable).
- `Menu` table (all admin menu items) and `UserMenuPermission` table so the site
  administrator can enable/disable each menu item per user. Endpoint returns the
  menu tree available to the logged-in user. API endpoints must ALSO enforce the
  permission server-side (policy-based authorization), not only hide menu items.
- Seed: one SuperAdmin user, the default roles, and the menu list.
- Endpoints: login, refresh, logout, change password, user CRUD (soft delete),
  assign roles, set menu permissions.
Admin UI:
- Login page, protected routes, layout with sidebar built from the user's allowed menus,
  User management page, and a Menu Permission screen (checkbox tree per user).
Tests: auth flow, refresh rotation, permission enforcement.

## Schema and security detail
- Prefer ASP.NET Core Identity for hashing/lockout/account management, subject to the approved comparison. Customer self-registration belongs to Phase 6.
- Audit every table, including Identity support/join tables. Refresh tokens need hash, user, expiry, session/family, revocation and replacement references.
- Consume/rotate atomically; replay revokes the family. Validate JWT signature, issuer, audience and expiry.
- Configure production Secure cookies, path, SameSite and credentialed CORS explicitly. Protect cookie-authenticated refresh/logout against CSRF.
- Rate limit login and use generic credential errors. Revoke sessions on password/security changes and account deactivation.
- Bootstrap SuperAdmin through secrets/one-time setup, never a fixed committed password. Protect the final active SuperAdmin.

## Authorization and API contract
Define missing-override behavior, role inheritance, explicit-deny precedence, SuperAdmin bypass and action permissions. Define when permission changes invalidate existing access.
Proposed route groups: /api/auth (login, refresh, logout, change-password, me, menus), /api/users, /api/roles, user role assignment and user menu permissions. Agree verbs/DTOs/policies before coding.

## Acceptance
Test lockout, expiry, simultaneous refresh/replay, logout, CSRF, disabled users, role/override combinations, final-SuperAdmin protection and direct calls to hidden-menu APIs. Verify 401/403 behavior and reload restoration without persistent access tokens.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Delivery/refund permission preparation
Include stable permission keys for delivery configuration, fulfillment updates, refund creation/approval and refund execution in the approved permission model; related business actions/screens are implemented only in Phases 4/6.
Audit/session expiry uses UTC; display audit times in Pacific/Auckland. Seed a single-shop menu without branch or warehouse selection. Refund approval roles and any separation of duties require confirmation.

