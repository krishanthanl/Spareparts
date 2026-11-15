Phase 2: Catalog.
Entities:
- Category: Name, Description, ImageUrl.
- Item: Code/SKU, Name, CategoryId, Brand (e.g. Toyota), MakeYearFrom, MakeYearTo
  (e.g. 2023–2026), Model (e.g. "Prius JDM"), ImageUrl, optional ReorderLevel,
  optional ReorderQuantity, IsActive, soft delete.
- Consider whether Brand and Model should be lookup tables (Brand -> Model) for clean
  filtering later; propose and justify.
- Image storage behind an `IFileStorage` interface (local disk now, Azure Blob later),
  with size/type validation and thumbnail generation.
Backend: Create / Update / Soft delete / Restore / List (paging, sorting, search) endpoints.
Add PostgreSQL indexes for search (Brand, Model, year range, name; consider pg_trgm).
Admin UI: Category and Item list + create/edit forms with image upload and preview,
soft-delete with confirm, restore for deleted records.
Tests: validation (year range From <= To), soft-delete filters.

## Modelling decisions
- Distinguish part manufacturer from vehicle make. Confirm multiple vehicle fitments before choosing a single Model/year range versus an ItemFitment relation.
- Define normalized SKU uniqueness/reuse after deletion, restore conflicts and category deletion with active items.
- No editable stock or parallel mutable cost field on Item. Effective selling prices come from Phase 3.
- Decide when image-gallery schema is introduced; one ImageUrl cannot provide a gallery.

## Files, validation and API
Plan entities/configurations, validators, handlers, storage, admin pages and new migrations. All tables have audit fields; Categories/Items have soft-delete fields and global filters.
Validate lengths, required names/SKUs, category eligibility, nonnegative nullable reorder level, positive nullable reorder quantity and explicit partial-year semantics.
Uploads require size/dimension limits, decoded signature/type checks, generated filenames, safe paths, thumbnails and orphan cleanup.
Use /api/categories and /api/items with explicit restore/image actions. Bound paging and whitelist sort fields. Deleted-record views require authorization.
Choose indexes from actual queries; pg_trgm requires an approved extension/migration decision.

## Acceptance
Test restore uniqueness, category/item filter interactions, years/reorder settings, malicious uploads and permissions. Historical references survive archival. Catalogue edits never change stock.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Single-location catalogue
Do not add warehouse allocation or location-specific prices/reorder settings. Labels and later prices use NZD.
The shop is not GST-registered. Do not add item GST rates or tax-classification screens for current operations; retain only the approved non-GST document metadata introduced in Phase 4. Stock availability remains derived from the single ledger and later reservations.

