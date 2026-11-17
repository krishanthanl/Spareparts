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

Docker is not installed. Phase 3 was verified using portable PostgreSQL 17 with an explicitly disposable local server (see the completion results below). Without opt-in configuration, database tests remain skipped. Hosted Prisma compatibility, Compose startup, restricted-role SQL and healthy database readiness/recovery remain explicit release checks.

The restricted execution environment cannot connect the .NET test runner to its local test host; local-network-enabled execution is required there. This does not require production access and does not change application behavior.

Browser tests use the installed Chrome channel by default; PLAYWRIGHT_CHANNEL can choose msedge or downloaded chromium. Screenshots/reports and temporary test server logs are ignored. The storefront shell remains noindex until the public catalogue phase.

## Later-phase tests

Stock and GRN implementation and PostgreSQL tests exist in Phase 3. Invoice/return/reservation behavior belongs to the later approved phases and must also use real PostgreSQL for database guarantees.

## Recorded Phase 0 results (2026-11-15)

20 backend unit/API tests, 10 frontend tests and 6 browser smoke checks passed. Both frontend production builds, type checks, lint and formatting passed. Backend builds pass. The real PostgreSQL test compiles but is explicitly skipped (1); live Compose/database/role-grant/readiness-recovery verification remains pending infrastructure. Compose configuration parsed successfully with the available standalone plugin. These results do not establish production or hosted Prisma readiness.

## Recorded Phase 2 results (2026-11-16)

- Backend: 77 unit/API tests passed — validators (years, reorder, SKU, paging/sort whitelist), image decoding and rejection (non-images, SVG/HTML, oversize bytes and pixels), storage key safety, model/migration snapshot, fail-closed authorization (401/403), field errors.
- PostgreSQL: 7 integration tests compile and are skipped here (no Docker). They apply the real migrations to a new throwaway database and cover SKU reuse/restore conflicts, soft-delete filtering, category delete blocking, case-insensitive names, fitment year boundaries, check constraints and public visibility. **Not yet run against a real database.**
- Import tool: dry run over the 100 sample images passed (all decode within limits). The real import has not been run (no database).
- Frontend: admin 10 and public 8 Vitest tests passed; both builds, type checks, lint and format check passed. Browser smoke checks: 6 passed (desktop and mobile).
- Not verified: admin screens against a signed-in API (sign-in is Phase 1).

## Hosted database run (2026-11-17, Prisma Postgres)

- Migration `Phase2Catalog` applied to the hosted database over `SSL Mode=VerifyFull`.
- First real import failed with `DbUpdateConcurrencyException` and rolled back cleanly. Cause: entity ids are set on construction, so EF treated a new `ItemImage` added only through `item.Images` as an existing row and sent an UPDATE. The same bug affected the admin API's image upload and fitment changes. Fixed by adding child rows explicitly; a unit test now covers it (78 backend tests pass).
- Import: 6 categories and 100 items created with 200 image files (full + thumbnail). A second run skipped all 100.
- Public API: 5 active categories, 95 items; Showcase (5 items) hidden as intended. Images served as `image/webp`. Admin API still returns 401.
- Public site in the browser: categories and the parts grid load from the database with real photos; the category filter works.
- The PostgreSQL integration tests were **not** run: they create and drop databases and must not point at the shop database. They still need a disposable server.

Enable the PostgreSQL tests with RUN_POSTGRES_TESTS=1 (Docker) or TEST_POSTGRES_CONNECTION; the catalogue tests also need permission to create and drop a database.

## Recorded Phase 1 results (2026-11-17)

- Backend: 113 unit/API tests passed (35 new). They run the real JWT bearer pipeline and permission policies with only the database-backed access lookup faked: expired/foreign-key/foreign-audience tokens, stamp change and deactivation (401), missing permission and hidden-menu APIs called directly (403), SuperAdmin bypass, deleted-record permission, refresh/logout Origin + CSRF header checks, cookie attributes (HttpOnly, Secure, SameSite=Strict, Path=/api/auth), generic login 401, per-IP login limit (429), permission evaluation (deny wins), validators and Identity options.
- PostgreSQL: 9 new auth tests compile and are skipped here (no Docker): rotation, simultaneous refresh (one winner), replay revokes the family, lockout, same answer for unknown/inactive/wrong password, logout, password change ends other sessions, deactivation, concurrent last-SuperAdmin removal, overrides and menus, idempotent seeding. **Not yet run against a real database.**
- Admin web: 17 Vitest tests passed (session restore on reload, token only in memory, sidebar from allowed menus, blocked pages, sign-in errors, forced password change, refresh-and-retry on 401, one shared refresh for simultaneous 401s). Type check, lint, format and production build passed.
- Browser: 6 Playwright checks passed on desktop and mobile; the admin app opens on sign-in with nothing in browser storage.
- Not verified: a real sign-in end to end (the Phase1Auth migration is not applied to the hosted database yet and no SuperAdmin exists).

## Recorded Phase 3 results (2026-11-17)

- **First real PostgreSQL run (GitHub Actions, PostgreSQL 17 via Testcontainers):** all 12 inventory tests and all 9 Phase 1 auth tests passed. One Phase 2 catalogue test failed (a rejected duplicate SKU stayed pending on a reused context); fixed in the shared save helper, see ADR 0004.
- Inventory database tests cover: stock at instants and zero before the first posting; UPDATE/DELETE of ledger and price rows rejected; a backdated insert re-stamped with the database time; the database alone rejecting negative stock; rollback of a whole posting when one line fails; five simultaneous posts of one GRN posting once; six simultaneous deductions never going below zero; reversal refused after stock was used; weighted average with a fractional receipt; price history; opening stock once per item; stocktake differences; gapless numbers under concurrency; posted lines locked; low stock at the reorder level.
- Backend unit/API: 143 passed (30 new): validators (3-decimal quantities, cents, reasons, kinds), NZ end-of-day boundaries across both 2026 daylight-saving changes, column precision, triggers present in the migration, a separate permission for each inventory area, StoreKeeper defaults without price/reverse, enum names on the API.
- Admin web: 23 Vitest tests passed (6 new): posted GRN read-only with fractional quantity and Reverse only with `inventory.reverse`, Post disabled without lines, stock report date parameter, stock card links and balances, stocktake differences and moved-stock warning, NZ number formatting. Type check, lint and format passed.
- CI runs on every push: `.github/workflows/ci.yml`. Failing tests are published as annotations on the run page.
- Not verified: the Phase3Inventory migration on the hosted Prisma database (not applied yet), and the screens against a running API with real data.

## Phase 3 completion verification (2026-11-17)

- Backend: **151 unit/API tests passed** and **32 real PostgreSQL integration tests passed**, with **zero skipped**. This includes all earlier foundation, authentication and catalogue tests. A portable PostgreSQL 17 server ran on loopback using disposable test databases; no shop database was contacted.
- New regressions prove receipt selling-price snapshots survive later repricing, posted lines cannot move into a draft, stock cards include all 2,001 movements, timestamp-bearing post bodies and price inputs return 400, and null line collections return field errors. The snapshot, reassignment, truncation and timestamp tests failed before their fixes and passed afterward.
- Frontend: **29 admin tests and 8 public tests passed**. Added coverage for all four document-list paging controls, independent page state when switching document types, and category selection on the stock report.
- Both frontend type checks, lint, formatting and production builds passed. Vite retains its existing warning about the admin JavaScript chunk exceeding 500 KB; build output is successful.
- Browser: **6 desktop/mobile smoke checks passed**. These check public navigation, admin sign-in rendering and safe API/health responses; they do not prove signed-in inventory workflows against the hosted database.
- EF reports no pending model changes. Both inventory migrations were applied by the integration suite. The original `Phase3Inventory` migration is unchanged; `Phase3InventoryProtection` closes the posted-line reassignment and edit/post race loopholes.
- Run and migration instructions: [Phase 3 inventory](phase3-inventory.md). Hosted Prisma trigger compatibility, applying the migrations there, and a signed-in inventory acceptance run remain deployment checks.
