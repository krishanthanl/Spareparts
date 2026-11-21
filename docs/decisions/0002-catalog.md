# ADR 0002: Phase 2 catalogue

Status: accepted (approved proposal `Plan/Phase2-Design-Proposal.md`, 2026-11-16).

## Decisions

- **D1 Phase order.** Catalogue management endpoints require the `catalog.manage` permission claim. Until Phase 1 adds sign-in, a placeholder authentication scheme (`NoSignInYet`) never authenticates, so they fail closed with 401. Phase 1 replaces the scheme; the policy name stays.
- **D2 Part brand vs vehicle.** `Item.PartBrand` is the part maker. Vehicles are separate lookups.
- **D3 Fitments.** `VehicleMake → VehicleModel` lookup tables plus `ItemFitment (item, model, YearFrom?, YearTo?)`. An item can fit many models and year ranges.
- **D4 Gallery.** `ItemImage` rows with exactly one primary per item (partial unique index). Categories keep one image.
- **D5 Images.** SkiaSharp (MIT). Uploads must decode as JPEG/PNG/WebP, ≤ 5 MB, ≤ 4000×4000 px. Camera orientation is applied; everything is re-encoded to WebP (drops metadata such as GPS) with a 480 px thumbnail. File names are random; client names never reach paths.
- **D6 Search.** Case-insensitive `ILIKE` with btree expression indexes on `lower(Name)` and `lower(PartBrand)`. No `pg_trgm` until Prisma Postgres support is confirmed.
- **D7 SKU.** Stored trimmed and upper-case (also a check constraint). Unique among non-deleted items; a deleted SKU can be reused and restoring a clashing item returns 409.
- **D8 Category delete.** Blocked (409) while it has non-deleted items.
- **D9 Public read API (brought forward from Phase 5).** `GET /api/public/categories` and `/api/public/items` (anonymous, read-only). Only active, non-deleted items in active, non-deleted categories. DTOs carry no cost, stock, audit actors or internal ids beyond the item id.
- **D10 Years.** Inclusive; either end optional; 1950 to next year; From ≤ To (validator and check constraint).

## Implementation notes

- Concurrency: categories and items use PostgreSQL `xmin` as a row version. Clients send `version`; a stale write returns 409.
- Storage: `IFileStorage` with `LocalDiskFileStorage` (`Storage:LocalRoot`, default `src/backend/Api/.data/media`, git-ignored), served read-only at `/media` with `nosniff` and long immutable caching. Azure Blob is a later implementation of the same interface.
- File/DB consistency: new files are deleted if the database save fails; replaced files are deleted only after the save succeeds.
- Admin UI uses plain MUI tables rather than `@mui/x-data-grid` to avoid adding a package whose v9 compatibility was not verified.
- Sample data: `tools/CatalogImport` loads `test-project` (`parts.json` + images). `part-001` → SKU `TP-001`; category description = the shared item description; `inStock` ignored; Showcase imported as an inactive category. Idempotent by SKU (`--update` overwrites), audit actor `system:import`, one transaction.

## Open items

- Done in Phase 1 (ADR 0003): `catalog.manage` is now a permission resolved server-side per user, not a token claim.
- Migration and import verified on hosted Prisma Postgres (2026-11-17). Integration tests still need a disposable database; see `docs/testing.md`.
- New child rows (images, fitments) must be added to their DbSet explicitly, because entity ids are set on construction.
- Thumbnails for existing images if thumbnail size changes later would need a regeneration command.
