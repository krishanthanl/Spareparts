# Phase 5 public storefront

The existing Vite storefront uses the Phase 2 media store, the current price from price history, and available stock from the ledger less confirmed reservations. No database migration is required. Prices are in NZD and no GST is charged.

- Home keeps the existing hero and shows up to eight eligible categories in catalog order.
- `/parts` offers shareable filters and pagination. Vehicle fitment years include both endpoints. A missing start or end year is open-ended.
- `/parts/{id}` uses the stable item ID. It shows a gallery, fitments, up to four related parts from the same category, and enquiry links.
- `/cart` and the cart drawer save a versioned cart on the device. A cart checks requested quantities without reserving stock. Changed prices and unavailable parts are shown when the cart opens.
- `/delivery-and-returns` shows contact guidance until the shop supplies approved copy. Contact details and the two policy descriptions are public frontend configuration.

## Public API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/public/categories` | Active categories containing active items |
| GET | `/api/public/items` | Text (`q`), `category`, `brand`, `makeId`, `modelId`, `year`, `minPrice`, `maxPrice`, `availability`, `sort`, `page`, `pageSize` |
| GET | `/api/public/items/{id}` | Item detail, fitment, gallery and related items |
| GET | `/api/public/vehicles/makes` | Makes |
| GET | `/api/public/vehicles/models?makeId=…` | Models for the selected make |
| POST | `/api/public/cart/validate` | Check up to 25 distinct lines with quantities from 1 to 99 |
| GET | `/api/public/sitemap.xml` | Live sitemap for the configured storefront origin |

Availability is `InStock`, `LowStock`, or `OutOfStock`. A positive quantity at or below a configured reorder level is low stock. Unpriced items show “Ask for price” and cannot be saved to the cart. Public responses omit exact inventory counts, costs, supplier data and staff details. Catalog reads advertise a 60-second cache lifetime. Cart validation is uncached.

## Run and verify

Start the API and a migrated local PostgreSQL database using the repository setup instructions. Start the storefront with `npm run dev:public`. Its development URL is `http://localhost:5174`.

- `./scripts/dotnet.ps1 test tests/Backend.UnitTests`
- `./scripts/dotnet.ps1 test tests/Backend.IntegrationTests -- xUnit.ParallelizeTestCollections=false` with a disposable PostgreSQL test connection.
- `npm run test --workspace=@spareparts/public-web -- --pool=threads --maxWorkers=1 --no-isolate`
- `npm run build --workspace=@spareparts/public-web`
- `npm run test:browser -- --grep storefront`

Local verification passed with 200 backend unit tests, 24 targeted PostgreSQL tests for catalog and sales, and 10 storefront tests. Desktop and mobile Playwright checks passed against the production build with catalog fixtures. The first-load JavaScript is approximately 123 KB gzipped and 375 KB uncompressed. EF Core reported no pending model changes.

## Release configuration

1. Set the API setting `Storefront__Origin` to the public storefront's HTTPS origin. The API sitemap uses it for every link. The storefront build emits `robots.txt` with the API sitemap URL when `VITE_API_BASE_URL` is set. Verify the sitemap in the site's search console if the API and storefront are on different hosts.
2. Set the public `VITE_SHOP_*`, `VITE_DELIVERY_INFORMATION` and `VITE_RETURNS_INFORMATION` variables before publishing. The Azure workflow reads these from repository variables. Use the owner's real contact details and approved delivery and returns wording.
3. Confirm a direct item URL loads on the deployed site and the API sitemap responds with the deployed storefront URLs. The SPA renders per-item metadata with JavaScript. Social preview crawlers that read only the initial HTML need a future prerendering decision.

Order placement, customer accounts and payment remain Phase 6.
