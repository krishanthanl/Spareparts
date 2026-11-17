# Phase 3 inventory

The approved inventory implementation includes goods received notes, adjustments, opening stock with CSV preview/import, stocktakes, current and historical stock reports, low-stock reporting, item stock cards and selling-price changes. Quantities allow three decimal places; money and weighted-average cost use two. Shop dates use Pacific/Auckland and currency is NZD.

## Run

Use the database and authentication setup in [configuration](configuration.md). With the migration connection set securely in the environment, apply the EF migrations:

```powershell
./scripts/dotnet.ps1 ef database update --project src/backend/Infrastructure --startup-project src/backend/Infrastructure
./scripts/dotnet.ps1 run --project src/backend/Api
```

In a second terminal:

```powershell
npm run dev:admin
```

Sign in and open Inventory. Admins can change selling prices and reverse receipts; StoreKeepers have receiving, adjustments and reporting by default. Existing roles receive the new inventory permissions and menus through the idempotent identity seeder.

The migrations are `Phase3Inventory` and `Phase3InventoryProtection`. The second migration prevents moving an immutable posted line into a draft and locks the parent document while checking whether lines are editable. The hosted database has not been changed in this completion pass. Confirm hosted trigger compatibility before applying migrations there.

## Workflows

- Receive stock: create a goods received draft, search for items, enter quantities and costs, then confirm Post. A missing selling price uses the current price and is saved on the receipt when posted. An item with no price needs a price on its first receipt. Later receipts and price changes leave receipt snapshots unchanged.
- Adjust stock: choose a reason and enter positive or negative quantities. Posting rejects insufficient stock. Posted records remain read-only.
- Initialize stock: use Opening stock once per item that has no movements. Upload a CSV, check its dry run, create a draft, review and post. Import never posts automatically.
- Count stock: create a stocktake, add items and enter counts. Posting calculates differences against stock under the item locks and creates one Count adjustment. A warning identifies stock that moved since counting started.
- Report: filter by SKU/name or category, choose a date for end-of-day NZ stock, and follow an item to its ledger. Low stock includes equality with the reorder level and shows reorder quantity. Document lists have page controls; stock cards return the full selected date range.
- Correct a receipt: Admins can reverse it with a reason. The reversal posts now and fails if it would take stock below zero. Change selling prices separately.

CSV example (SKU values must exist):

```csv
sku,quantity,unit_cost,selling_price
TP-001,1.500,12.50,20.00
TP-002,3,5.00,
```

Client-controlled effective timestamps are rejected by inventory input contracts. Post/cancel commands accept no body. Supplier document dates are informational only. Stock and price history are append-only; postings, numbering and document transitions commit atomically. Reservations and sales belong to Phase 4.

## Verification

Run the commands in [testing](testing.md). Set `TEST_POSTGRES_CONNECTION` to a disposable PostgreSQL server with create/drop database permission, never the shop database. Tests create randomly named databases and remove them. Docker/Testcontainers remains supported with `RUN_POSTGRES_TESTS=1`.

This completion pass used a portable PostgreSQL 17 server bound to loopback, stored under ignored `.local`, without installing a system service. Regression tests were observed failing before fixes for receipt-price snapshots, posted-line reassignment, silent stock-card truncation, client timestamps and inaccessible older document pages. Actual final results are recorded in [testing](testing.md).
