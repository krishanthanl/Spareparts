Phase 5: Public site (catalog browsing).
- Attractive, modern, mobile-first UX with a distinctive design (not a generic template):
  home page with featured categories, hero, brand/vehicle selector
  ("Select Brand -> Model -> Year" quick finder), skeleton loaders, good empty states.
- Public read-only API (no auth) for categories and items; never expose cost price or stock internals
  (only "In stock / Low stock / Out of stock").
- Search by text plus filters: Category, Brand, Model, Make year, price range, availability.
  Sorting and pagination, URL-driven filters (shareable links).
- Item detail page with image gallery and related items.
- Cart (state in Zustand + localStorage), cart drawer/page, quantity limits by available stock.
- SEO basics: meta tags, clean URLs, sitemap.
Tests: filter combinations, cart behavior.

## Public API and scope
- Proposed routes: /api/public/categories, /api/public/items, detail and vehicle lookups. Use dedicated DTOs excluding costs, suppliers, staff identities and ledger/raw-stock data.
- Hide deleted/inactive records under an agreed category policy. Select effective selling prices; define missing-price behavior and low-stock labels for null reorder levels.
- Availability accounts for reservations. Reconcile cart limits with stock privacy using a bounded requested-quantity validation response rather than exposing exact stock.
- Confirm featured-category selection/gallery storage. Any additive schema needs approval and a new migration.
- No order submission, customer registration or payment processing in this phase.

## Frontend behavior
Agree visual direction before coding. Add keyboard accessibility, mobile filters, responsive images and useful loading/error/empty states. Filters live in URLs; dependent vehicle selections stay valid.
Version persisted cart state, storing IDs/quantities with stale display snapshots. Handle corrupt/malicious localStorage, deleted items and changed prices. Checkout revalidates server-side later.
Document SPA metadata/crawler limitations; propose prerendering only if agreed SEO requirements justify it. Define cache freshness for prices/availability.

## Acceptance
Test combined filters, fitment boundaries, paging, public data privacy, cart persistence/revalidation, mobile/keyboard use, image failures and console errors. Do not imply checkout works before Phase 6.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## New Zealand customer information
Display NZD clearly using en-NZ formatting. The shop is not GST-registered: show final item prices and delivery charges without GST. Do not display 'GST included', 'plus GST' or a zero-rated label. Where helpful state 'Not GST registered - no GST charged'.
Provide delivery coverage/charges information and a returns/refunds policy page with owner-approved content; do not invent commercial terms or remove statutory rights.
Show delivery estimates as estimates; final delivery/tax totals are calculated in Phase 6. Customer refund requests and paid checkout are not implemented here.


## Review 2026-11-17: what already exists, and what is missing

### Already built early (during Phase 2): finish it, don't rebuild it
- Public API: `GET /api/public/categories` and `GET /api/public/items` (category filter, text search, paging; active and non-deleted only; no cost or stock data).
- Storefront home: hero with the pickup photo, live categories, part search and photo grid, contact section.
- Item thumbnails and full images (WebP) are already generated and served from `/media`.
- `src/public-web/index.html` still has `<meta name="robots" content="noindex,nofollow">`. Remove it in this phase, together with the sitemap, once item pages exist.

### Still to do
- Item detail page and gallery, related items, the vehicle quick finder (makes, models and fitment years already exist in the database), URL-driven filters, price and availability display, cart.
- **Prices depend on Phase 3.** Use the current selling price from `ItemPriceHistory`. Items without a price show "Ask for price" with call/email buttons and cannot go in the cart.
- **Enquiry path.** Many parts customers phone first. Add an "Ask about this part" action on item pages that opens phone or email with the SKU and part name filled in. No stored form or customer data in this phase.
- **Availability labels** come from on-hand minus reservations (Phases 3-4): In stock / Low stock (at or below reorder level) / Out of stock. Items with no reorder level never show Low stock.
- **Performance budget** for a mobile-first shop: aim for under about 200 KB of JavaScript on first load of the home page, lazy-loaded images with width and height set, and short cache headers on public API responses (e.g. 60 s) so prices stay fresh.
- **Analytics** (optional, needs owner approval): privacy-friendly page and search tracking to learn what customers look for. No third-party trackers without a privacy notice.
