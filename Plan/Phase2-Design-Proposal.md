# Phase 2 Catalog — Design Proposal

Status: **proposed, awaiting approval.** Nothing in this file is approved until you say so. Approved decisions will move to `docs/decisions/0002-catalog.md`.

Spec: `Plan/Masterprompt.md`, `Plan/Phase2-CatalogCategoriesItems.md`.
Sample data: `D:\GitLabRepos\test-project` (100 images, `src/data/parts.json`, `scripts/generate-catalog.mjs`).

---

## 1. Decisions needed before coding

| # | Decision | Recommendation |
|---|---|---|
| D1 | **Phase order.** Phase 1 (auth/roles) isn't built. Phase 2 admin create/edit/delete endpoints need authorization. | Build Phase 2 with endpoints behind an `catalog.manage` policy that **fails closed** until Phase 1 is done. Integration tests use a test auth handler. The admin screens become usable once Phase 1 lands. Alternative: do Phase 1 first. |
| D2 | **Part brand vs vehicle make.** | Keep them separate. `Item.PartBrand` is the part maker (e.g. "Depo", "Bosch"). The vehicle (Toyota Prius 2016–2022) goes in fitments (D3). |
| D3 | **One vehicle or many per item.** | Many. A headlight often fits several models and year ranges. Use lookup tables `VehicleMake → VehicleModel` plus an `ItemFitment` table (item, model, year from/to). The Phase 5 "Brand → Model → Year" finder then filters cleanly, and makes/models are spelled consistently. |
| D4 | **Image gallery now or later.** | Now. Phase 5 needs an item gallery, and adding it later means a second migration and data move. Use an `ItemImage` table with one primary image per item. Category keeps a single image. |
| D5 | **Image library.** | **SkiaSharp** (MIT) to decode, check size and make thumbnails. ImageSharp is nicer to use but has a split commercial licence; avoid it unless you confirm the licence fits. |
| D6 | **Search indexes.** | Start with normal btree indexes plus case-insensitive `ILIKE` search. Leave `pg_trgm` out until we confirm Prisma Postgres Free supports it (needs a separate approved migration). At 100s–1000s of items this is fast enough. |
| D7 | **SKU rules.** | Stored trimmed and upper-case. Unique among **non-deleted** items (partial unique index). A deleted item's SKU can be reused; restoring it fails with a clear error if the SKU is now taken. |
| D8 | **Deleting a category with active items.** | Blocked (409 with a ProblemDetails message). Items must be moved or deleted first. |
| D9 | **Showing items on the public site.** | Public catalogue pages are Phase 5. Your request to show item images on the storefront brings part of Phase 5 forward. Proposal: add only `GET /api/public/categories` and `GET /api/public/items` (read-only, no stock/cost, active and non-deleted only) plus an item grid on the home page. Full filters, item detail, cart stay in Phase 5. **Needs your yes.** |
| D10 | **Year semantics.** | `YearFrom` and `YearTo` are both optional. Only From = "this year onwards". Only To = "up to this year". Both = inclusive range, From ≤ To. Allowed range 1950 to current year + 1. |

## 2. Database changes (one new migration: `Phase2Catalog`)

All tables have `CreatedAt, CreatedBy, UpdatedAt, UpdatedBy` (existing interceptor). Soft-delete tables use the existing `ISoftDeletable` global filter.

| Table | Columns | Soft delete | Key constraints / indexes |
|---|---|---|---|
| `categories` | Id, Name (100), Slug (120), Description (1000, null), ImageKey (300, null), SortOrder int, IsActive | ✅ | unique `lower(name)` and unique `slug` where not deleted |
| `items` | Id, Sku (50), Name (200), Description (4000, null), CategoryId → categories, PartBrand (100, null), ReorderLevel int null (≥ 0), ReorderQuantity int null (> 0), IsActive | ✅ | unique `sku` where not deleted; index `(category_id)`, index `lower(name)`, index `lower(part_brand)` |
| `vehicle_makes` | Id, Name (80) | — | unique `lower(name)` |
| `vehicle_models` | Id, MakeId → vehicle_makes, Name (120) | — | unique `(make_id, lower(name))` |
| `item_fitments` | Id, ItemId → items, VehicleModelId → vehicle_models, YearFrom smallint null, YearTo smallint null | — | check `year_from <= year_to`; unique `(item_id, vehicle_model_id, year_from, year_to)`; index `(vehicle_model_id, year_from, year_to)` |
| `item_images` | Id, ItemId → items, StorageKey (300), ThumbnailKey (300), ContentType (50), ByteSize int, Width int, Height int, SortOrder int, IsPrimary bool, AltText (200, null) | — | partial unique `(item_id) where is_primary`; index `(item_id, sort_order)` |

Not added (per spec): stock, cost, selling price, GST fields. `inStock` in the sample data is ignored; stock comes from the Phase 3 ledger.

## 3. Image storage

- `Application/Common/IFileStorage` — `SaveAsync(stream, key)`, `OpenReadAsync(key)`, `DeleteAsync(key)`, `GetPublicUrl(key)`.
- `Infrastructure/Storage/LocalDiskFileStorage` — writes under a configured root (`Storage:LocalRoot`, outside the web root, default `./.data/media`), served read-only at `/media/...`. Azure Blob implementation later, same interface.
- Upload rules: JPEG/PNG/WebP only, checked by **decoding** the file (not by extension or header). Max 5 MB, max 4000×4000 px. New random file names (`items/{itemId}/{guid}.webp`); the original name is never used in a path. A 480 px WebP thumbnail is generated.
- Orphan cleanup: replacing or removing an image deletes its files after the database save succeeds. A failed save deletes the just-written files.

**About copying the images into `public-web/public/parts`:** with this design the images live in file storage and are served by the API, so copying them into the frontend isn't needed. The importer (section 5) reads them straight from the test-project folder. If you still want them in `public-web` as a quick demo before Phase 2 is built, say so.

## 4. API endpoints

Admin (policy `catalog.manage`, see D1). All lists take `page` (default 1), `pageSize` (default 20, max 100), `sort` from a fixed list (`name`, `-name`, `sku`, `-sku`, `updatedAt`, `-updatedAt`), and `q` search.

| Method | Route | Notes |
|---|---|---|
| GET | `/api/categories` | `includeDeleted=true` needs the policy |
| GET | `/api/categories/{id}` | |
| POST | `/api/categories` | FluentValidation |
| PUT | `/api/categories/{id}` | optimistic concurrency via Postgres `xmin` |
| DELETE | `/api/categories/{id}` | soft delete; 409 if active items (D8) |
| POST | `/api/categories/{id}/restore` | 409 on name/slug clash |
| PUT / DELETE | `/api/categories/{id}/image` | upload / remove |
| GET | `/api/items` | filters: `categoryId`, `partBrand`, `makeId`, `modelId`, `year`, `isActive`, `includeDeleted` |
| GET | `/api/items/{id}` | includes fitments and images |
| POST / PUT | `/api/items`, `/api/items/{id}` | fitments sent as part of the item |
| DELETE | `/api/items/{id}` | soft delete |
| POST | `/api/items/{id}/restore` | 409 on SKU clash (D7) |
| POST | `/api/items/{id}/images` | upload; first image becomes primary |
| PUT | `/api/items/{id}/images/{imageId}/primary` | |
| DELETE | `/api/items/{id}/images/{imageId}` | |
| GET / POST | `/api/vehicle-makes`, `/api/vehicle-makes/{id}/models` | lookups |

Public (only if D9 approved, no auth): `GET /api/public/categories`, `GET /api/public/items?categoryId=&q=&page=` — name, SKU, description, category, image URLs only.

All errors are ProblemDetails (existing handler).

## 5. Importing the sample data into the database

A small console project `tools/CatalogImport` that uses the same `AppDbContext` and `IFileStorage`:

```powershell
./scripts/dotnet.ps1 run --project tools/CatalogImport -- --source D:\GitLabRepos\test-project
```

Mapping:

| Sample data | Database |
|---|---|
| `category` (6 names) | `categories.Name`, slug from name |
| category description from `DESCRIPTIONS` in `generate-catalog.mjs` | `categories.Description` (every item in a category has the same text, so it's really a category description) |
| `id` `part-001` | `items.Sku` = `TP-001` (prefix shows it's test data) |
| `name`, `description` | `items.Name`, `items.Description` |
| `public/parts/part-001.jpeg` | stored through `IFileStorage` + thumbnail, `item_images` primary row |
| `inStock` | ignored (stock = Phase 3 ledger) |
| no brand / model / years | no fitments; `PartBrand` empty |

- Safe to run again: matches on SKU and skips existing items (`--update` to overwrite).
- Audit actor is `system:import`.
- Runs in one transaction; image files written during a failed run are cleaned up.
- **Question:** the "Showcase" category (part-096 to part-100) is photos of whole vehicle builds, not parts. Recommend importing them as an **inactive** category so they're kept but not shown.

## 6. Admin UI (admin-web, MUI)

- Categories: list (search, show deleted toggle), create/edit form with image upload and preview, delete with confirm, restore.
- Items: list with thumbnail, SKU, name, category, active; filters for category, brand, vehicle; create/edit form with fitment rows (make → model → years) and image gallery upload, reorder and set-primary; delete/restore.
- Vehicle makes/models: simple list and add.
- Uses MUI Data Grid (free `@mui/x-data-grid`, MIT).

## 7. Files to create (summary)

- `Domain/Catalog/`: `Category`, `Item`, `ItemImage`, `ItemFitment`, `VehicleMake`, `VehicleModel`
- `Application/Catalog/`: DTOs, validators, list query (paging/sort/search), `IFileStorage`, image validation service
- `Infrastructure/Persistence/Configurations/`: one EF configuration per entity; `Migrations/…_Phase2Catalog`
- `Infrastructure/Storage/LocalDiskFileStorage.cs`, `Infrastructure/Imaging/SkiaImageProcessor.cs`
- `Api/Controllers/`: `CategoriesController`, `ItemsController`, `VehicleMakesController` (+ `Public*` if D9)
- `tools/CatalogImport/` console project
- `admin-web/src/features/catalog/…` pages and API hooks
- `docs/decisions/0002-catalog.md`, updates to `docs/configuration.md` (storage settings)

## 8. Tests

- Unit: validators (year From ≤ To, partial years, reorder level ≥ 0 / quantity > 0, SKU normalising, lengths), sort whitelist, paging bounds.
- Image: rejects non-images renamed to `.jpg`, too large, too many pixels, SVG/HTML; accepts JPEG/PNG/WebP; thumbnail is made.
- Integration (real PostgreSQL): soft-delete filter hides records; SKU reuse after delete; restore conflict returns 409; category delete blocked with active items; fitment filters by make/model/year boundaries; migration applies on an empty database; importer is idempotent; unauthenticated writes return 401/403.
- Frontend: admin forms validation and list rendering (Vitest); Playwright smoke for category → item create with image.

## 9. How the data gets into your database (after approval and build)

You need PostgreSQL running. Phase 0 notes say Docker wasn't available, so this hasn't been checked on a real database yet.

1. Start Postgres: `docker compose up -d` (needs `.env` with the passwords — see `docs/configuration.md`).
2. Set the migration connection (as the `spareparts_migrator` role) in your shell, not in a file.
3. Apply the migration:
   `./scripts/dotnet.ps1 ef database update --project src/backend/Infrastructure --startup-project src/backend/Api`
4. Run the importer (section 5).
5. Check in pgAdmin (`http://localhost:5050`) that `categories` has 6 rows and `items` has 100.

I can run steps 3–5 myself if Postgres is running locally and the connection is set in user-secrets/environment; I won't handle the passwords.
