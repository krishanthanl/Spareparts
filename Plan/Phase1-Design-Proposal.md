# Phase 1 Auth & RBAC — Design Proposal

Status: **draft, awaiting approval.** Nothing is built until you approve or amend the decisions below.

Spec: `Plan/Masterprompt.md`, `Plan/Phase1-Authenticationauthorization.md`.
Builds on: Phase 0 foundation and Phase 2 catalogue (the `catalog.manage` policy already exists and fails closed with 401).

---

## 1. Decisions needed before coding

| # | Decision | Recommendation |
|---|---|---|
| D1 | **ASP.NET Core Identity or custom user tables.** | **Identity (core only)**: `AddIdentityCore<AppUser>()` with `Guid` keys, no Identity UI and no Identity cookies. It gives us proven password hashing (PBKDF2, upgradeable), lockout, security stamps and normalised email lookups. Writing those by hand is risky. We use our own JWT/refresh code on top. `AppDbContext` becomes `IdentityDbContext<AppUser, AppRole, Guid, …>` with custom join/claim entity types so **every Identity table also gets the four audit columns**. Unused Identity tables (user logins, user tokens) are not mapped. |
| D2 | **Login name.** | Email address (case-insensitive, unique). Staff only in this phase. A `UserType` column (`Staff` now; `Customer` added in Phase 6) keeps future customers out of every staff permission. |
| D3 | **Access token.** | JWT, HS256, **10 minutes**. Claims: `sub` (user id), `sid` (session/family id), `stamp` (security stamp hash), `jti`. Issuer, audience, signature, expiry validated, clock skew 30 s. Signing key ≥ 256 bits from user-secrets / environment (Azure Key Vault later). Startup fails without it outside Development. **Permissions are not put in the JWT** (see D7). Kept in memory only on the client. |
| D4 | **Refresh token.** | 256-bit random value, stored as **SHA-256 hash** in `refresh_tokens`. Sliding **14 days**, absolute family limit **30 days**. Sent as cookie `sp_refresh`: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. In Development (`http://localhost`) `Secure` still works on localhost in Chrome/Edge. |
| D5 | **Rotation and replay.** | Each refresh consumes the token **atomically** (`UPDATE … SET consumed_at = now() WHERE token_hash = @h AND consumed_at IS NULL AND revoked_at IS NULL RETURNING …`) and inserts a replacement linked by `replaced_by_id`, in one transaction. Presenting an already-consumed token = **replay → revoke the whole family** (all devices for that login session), and log a security event. **Concurrent tabs:** the admin app serialises refresh with the browser Web Locks API, so two tabs never send the same token. A **5-second grace window** after rotation returns 409 "retry" instead of revoking, to cover a network retry. |
| D6 | **CSRF on refresh/logout.** | Cookie is `SameSite=Strict`, **plus** the server requires an `Origin` header matching the configured CORS origins, **plus** a custom header `X-CSRF-Token: 1` (forces a CORS preflight that other sites cannot pass). No CSRF token store needed. Requires the admin app and API to be on the **same site** (e.g. `admin.shop.co.nz` + `api.shop.co.nz`). Confirm in D14. |
| D7 | **Permission model.** | Flat **permission keys** defined in code (e.g. `catalog.manage`, `users.manage`). **Roles** hold a set of keys (`role_permissions`). **Menus** each require one view permission. **Per-user overrides** (`user_permission_overrides`: Allow or Deny). Effective permissions = role keys ∪ user Allows − user Denies. **Deny wins.** No override row = inherit from roles. **No role inheritance** (flat roles are easier to reason about). **SuperAdmin bypasses everything** and cannot be denied. Every API endpoint uses a policy per key, so hiding a menu never replaces server checks. |
| D8 | **Spec table "UserMenuPermission".** | The spec names a per-user menu table. Proposal: the **Menu Permission screen** (checkbox tree per user) writes to `user_permission_overrides` for each menu's permission key, so menu access and API access can never disagree. One table instead of two parallel ones. **Needs your yes**, since it differs from the spec's wording. |
| D9 | **When permission changes take effect.** | Permissions are resolved server-side per request from a short memory cache (**15 s**) keyed by user + `permission_version`. Changing a user's roles/overrides bumps the version, so access changes **within 15 seconds** across all Azure instances, without forcing logout. **Immediate full logout** (revoke all refresh families + new security stamp) on: password change/reset, deactivation, soft delete, SuperAdmin role removal. The `stamp` claim is checked against the cache too, so old access tokens die within 15 s. |
| D10 | **Default roles.** | Seeded system roles: **SuperAdmin** (all), **Admin** (all except managing SuperAdmins), **StoreKeeper** (catalogue, later inventory), **Cashier** (later sales). Roles are **configurable**: Admin can create custom roles and edit permission sets. System roles cannot be deleted or renamed; SuperAdmin's permission set cannot be edited. |
| D11 | **Bootstrap SuperAdmin.** | No seeded password. A one-time tool `tools/AdminBootstrap` (same style as `CatalogImport`) creates the first SuperAdmin from `--email` and a password read from a hidden prompt or `BOOTSTRAP_PASSWORD` env var. It **refuses to run if any active SuperAdmin exists**. Audit actor `system:bootstrap`. |
| D12 | **Final SuperAdmin protection.** | Deactivating, deleting, or removing the SuperAdmin role from the **last active SuperAdmin** returns 409. Enforced inside a transaction holding a PostgreSQL advisory lock (`pg_advisory_xact_lock`) so two admins doing it at once cannot both succeed. Only a SuperAdmin can grant or remove SuperAdmin. |
| D13 | **Login protection.** | Identity lockout: **5 failures → 15-minute lock**. Rate limit (`Microsoft.AspNetCore.RateLimiting`): **10 login attempts/minute per IP**, 5/minute per email. Wrong email, wrong password, locked and disabled all return the **same** generic 401 "Email or password is incorrect." (lockout is logged, not revealed). Password policy: **minimum 12 characters**, no forced symbol rules (NIST guidance), cannot equal email. Admin-set passwords force a change on next login. |
| D14 | **Hosting assumption for cookies.** | Admin app and API share a registrable domain in production (needed for `SameSite=Strict`). If they must be on unrelated domains, the fallback is `SameSite=None` + a stored CSRF token. **Please confirm the planned domains.** |
| D15 | **Refund approval roles (prep only).** | Permission keys are created now, business screens come in Phases 4/6. Proposal: `refunds.create` (Cashier, Admin) and `refunds.approve` (Admin) are separate keys, and a user **cannot approve their own refund** (rule implemented in Phase 4). **Needs confirmation**, per the spec. |

## 2. Permission keys (seeded, stable)

| Key | Purpose | Default roles |
|---|---|---|
| `dashboard.view` | Admin home | all staff |
| `catalog.manage` | Categories, items, vehicles (existing policy) | Admin, StoreKeeper |
| `catalog.deleted.view` | See/restore deleted catalogue records | Admin |
| `users.view` / `users.manage` | User list / create, edit, deactivate, reset password | Admin |
| `roles.manage` | Custom roles and their permissions | Admin |
| `permissions.manage` | Per-user menu/permission overrides | Admin |
| `inventory.*` placeholders | `inventory.grn`, `inventory.adjust`, `inventory.reports` (used in Phase 3) | StoreKeeper, Admin |
| `sales.*` placeholders | `sales.invoice`, `sales.return` (Phase 4) | Cashier, Admin |
| `delivery.configure` / `fulfillment.update` | Phase 4/6 prep | Admin / StoreKeeper, Admin |
| `refunds.create` / `refunds.approve` / `refunds.execute` | Phase 4/6 prep (D15) | Cashier+Admin / Admin / Admin |

Placeholder keys exist so roles can be set up once; nothing uses them until their phase. Menu items for later phases are seeded **hidden** (`is_enabled = false`) so the sidebar doesn't show unbuilt screens.

## 3. Database changes (one new migration: `Phase1Auth`)

All tables get `CreatedAt, CreatedBy, UpdatedAt, UpdatedBy`. The audit interceptor moves from the `AuditableEntity` base class to an `IAuditable` interface so Identity entities (which have their own base classes) are covered. Snake_case table names, matching Phase 2.

| Table | Key columns | Notes |
|---|---|---|
| `users` | Id, Email, NormalizedEmail, DisplayName (100), PasswordHash, SecurityStamp, ConcurrencyStamp, LockoutEnd, AccessFailedCount, UserType, IsActive, MustChangePassword, PermissionVersion int, soft-delete fields | unique `normalized_email` where not deleted. Phone/2FA columns not mapped. |
| `roles` | Id, Name, NormalizedName, Description, IsSystem | unique `normalized_name` |
| `user_roles` | UserId, RoleId | PK (user_id, role_id), audited |
| `role_permissions` | RoleId, PermissionKey (100) | PK (role_id, permission_key) |
| `user_permission_overrides` | UserId, PermissionKey, Effect (`Allow`/`Deny`) | PK (user_id, permission_key) |
| `menus` | Id, Key, ParentId → menus, Title, Path, Icon, SortOrder, RequiredPermission, IsEnabled | unique `key`; seeded, not editable through the API |
| `refresh_tokens` | Id, UserId, FamilyId, TokenHash (unique), ExpiresAt, FamilyExpiresAt, ConsumedAt, RevokedAt, RevokedReason, ReplacedById, CreatedIp, UserAgent (200) | **no soft delete**; index (user_id, revoked_at); rows are never updated except consume/revoke fields. Expired rows purged after 60 days by a startup/background cleanup. |
| `security_events` | Id, UserId null, Type (LoginFailed, Lockout, TokenReplay, PasswordChanged…), OccurredAt, Ip, Details | append-only audit trail for auth events |

Actors: authenticated requests audit as the user id; anonymous login/refresh writes audit as `anonymous` (or the user id once known); tools as `system:<tool>`.

## 4. API (route groups)

All errors are ProblemDetails; all input validated with FluentValidation.

| Method & route | Auth | Purpose |
|---|---|---|
| `POST /api/auth/login` | anonymous, rate-limited | email + password → `{ accessToken, expiresAt, user, mustChangePassword }` + sets refresh cookie |
| `POST /api/auth/refresh` | cookie + CSRF checks (D6) | rotates refresh token → new access token |
| `POST /api/auth/logout` | cookie + CSRF checks | revokes current family, clears cookie |
| `POST /api/auth/logout-all` | signed in | revokes every family for the user |
| `POST /api/auth/change-password` | signed in | current + new password; revokes other sessions |
| `GET /api/auth/me` | signed in | profile + effective permission keys |
| `GET /api/auth/menus` | signed in | menu tree the user may see |
| `GET /api/users` | `users.view` | paged, search, sort whitelist, `includeDeleted` needs `users.manage` |
| `GET /api/users/{id}` | `users.view` | details, roles, overrides |
| `POST /api/users` | `users.manage` | create with temporary password (`MustChangePassword`) |
| `PUT /api/users/{id}` | `users.manage` | name, email, active flag (version-checked, 409 on stale) |
| `DELETE /api/users/{id}` / `POST /api/users/{id}/restore` | `users.manage` | soft delete / restore; last-SuperAdmin rule (D12) |
| `POST /api/users/{id}/reset-password` | `users.manage` | admin sets temporary password, revokes sessions |
| `POST /api/users/{id}/unlock` | `users.manage` | clear lockout |
| `PUT /api/users/{id}/roles` | `users.manage` (+ SuperAdmin for SuperAdmin role) | replace role set |
| `GET/PUT /api/users/{id}/permissions` | `permissions.manage` | menu tree with inherited/allow/deny state; replace overrides |
| `GET /api/roles`, `POST`, `PUT /{id}`, `DELETE /{id}` | `roles.manage` | custom roles; system roles protected |
| `GET /api/permissions` | `roles.manage` | list of permission keys with descriptions |

The `NoSignInYetHandler` placeholder is removed; the existing `catalog.manage` policy starts working through the new permission handler.

## 5. Admin UI (MUI, React Router, TanStack Query — already installed)

- **Login page**: email, password, generic error, "must change password" step.
- **Auth provider**: access token in memory only; on page load calls `/api/auth/refresh` to restore the session (no token in localStorage). Refresh ~1 minute before expiry and on a 401, serialised with Web Locks. Logout clears memory and cookie.
- **Protected routes**: unauthenticated → login (returns to the page after sign-in); missing permission → 403 page.
- **Layout**: MUI sidebar built from `/api/auth/menus`; existing catalogue pages move inside it.
- **Users page**: list, search, create, edit, deactivate, delete/restore, reset password, unlock, roles.
- **Menu Permission screen**: checkbox tree per user, each node showing *inherited from role* / *allowed* / *denied*.
- **Roles page**: custom roles and their permission checkboxes.
- **Change password** dialog from the user menu.

## 6. Files (outline)

- `Domain/Identity/`: `AppUser`, `AppRole`, `AppUserRole`, `RolePermission`, `UserPermissionOverride`, `Menu`, `RefreshToken`, `SecurityEvent`, `PermissionKeys`.
- `Domain/Common/IAuditable.cs`; `AuditableEntity` implements it.
- `Application/Identity/`: contracts, validators, `IAuthService`, `IUserService`, `IPermissionService`.
- `Infrastructure/Identity/`: `TokenService` (JWT + refresh), `AuthService`, `UserService`, `PermissionService` (+ cache), `IdentityConfigurations`, `IdentitySeeder` (roles, permissions, menus — no users).
- `Api/Identity/`: `PermissionAuthorizationHandler`, policy registration per key, CSRF/origin filter, rate-limit policies, `AuthController`, `UsersController`, `RolesController`; `JwtSettings` with startup validation.
- `tools/AdminBootstrap/`.
- Migration `Phase1Auth`.
- `admin-web/src/features/auth/`, `features/users/`, `features/roles/`, `layout/`.
- `docs/decisions/0003-auth.md` after approval.

## 7. Tests

**Unit/API (in-memory host):** login success/generic failures, lockout after 5, rate limit 429, JWT expiry/wrong issuer/audience/signature → 401, missing permission → 403, Deny beats role Allow, SuperAdmin bypass, hidden-menu API called directly → 403, CSRF (missing header, wrong Origin) → 403, cookie attributes, validators.
**Real PostgreSQL:** atomic rotation, **simultaneous refresh** (two parallel requests with one token: exactly one wins), replay revokes family, logout revocation, disabled user cannot refresh, password change kills other sessions, **two concurrent attempts to remove the last two SuperAdmins** (one must fail), permission change visible within the cache window, Identity tables have audit columns.
**Frontend (Vitest):** auth provider restores session on reload without storage, sidebar from menu API, protected route redirects, permission checkbox tree states.
**Browser (Playwright):** sign in, reload stays signed in, open catalogue, sign out.

> The PostgreSQL tests still need a **disposable database** (Docker with `RUN_POSTGRES_TESTS=1`, or a separate throwaway server). They must not run against the shop database.

## 8. New packages

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.x
- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.x
- Rate limiting is built into ASP.NET Core (no package).

## 9. Open questions for you

1. Approve **D1** (Identity) and **D8** (one overrides table instead of a separate `UserMenuPermission` table)?
2. **D14**: planned production domains for admin app and API. Same registrable domain?
3. **D15**: refund roles and the "can't approve your own refund" rule.
4. Token lifetimes (10 min / 14 days / 30 days) and lockout (5 tries / 15 min) OK?
5. Can a disposable PostgreSQL be set up (Docker Desktop installed?) so the real-database tests run this phase?
