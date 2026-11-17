# ADR 0004: Inventory ledger, prices and stock-at-date (Phase 3)

Status: accepted 2026-11-17. `Plan/Phase3-Design-Proposal.md` approved, with the owner's answers to its open questions:
1. Average cost is rounded to **2 decimals** (the proposed 4-decimal exception was declined).
2. Price changes and GRN reversals are **Admin only**.
3. PostgreSQL tests run on **GitHub Actions** (no Docker on the dev machine).
4. **Fractional quantities happen** (e.g. litres), so quantities are decimals.
5. Trigger support on Prisma Postgres to be confirmed before the shop database is migrated.

## Decisions

- **Quantities** are `numeric(18,3)` everywhere (receipts, adjustments, counts, ledger): up to 3 decimals, e.g. 1.5 L or 0.250 kg. Item reorder level/quantity stay whole numbers.
- **Money** is `numeric(18,2)`, including the running weighted-average cost, which is rounded to cents (away from zero) after every receipt. Small rounding drift is accepted.
- **Weighted average cost.** A receipt sets `avg = (onHand × avg + qty × cost) / (onHand + qty)`; when on-hand was zero or below, the average becomes the receipt cost. Removals use the current average and do not change it. A GRN reversal removes stock at its receipt cost and recalculates; if the result would be negative or stock reaches zero, the average is left unchanged. Every movement stores `AverageCostAfter`.
- **Ledger** `stock_movements` is append-only. Database triggers: `OccurredAt` is always set to `now()` (client values ignored, so no backdating or future dating); UPDATE, DELETE and TRUNCATE raise errors; a deferred constraint trigger rejects any commit leaving an item below zero. `item_price_history` has the same protection and `EffectiveFrom = now()`.
- **Documents** (`grns`, `stock_documents` for adjustments and opening stock, `stocktakes`): Draft → Posted, Draft → Cancelled, and for GRNs Posted → Reversed. A trigger allows only these changes and stamps `PostedAt`/`ReversedAt` from the database clock. Lines can change only while their document is a draft (trigger).
- **Concurrency.** Each posting runs in one transaction and locks the affected `items` rows with `SELECT … FOR UPDATE ORDER BY "Id"`, so postings for the same item queue up, even across API instances. Posting a document also locks its header row; posting an already-posted document returns it unchanged. `PostingKey` is unique per movement.
- **Numbers** are gapless per type (`GRN-`, `ADJ-`, `OPN-`, `STK-` + 6 digits) from `document_counters`, incremented inside the posting transaction (a rollback gives the number back). Phase 4 reuses this for invoices.
- **Prices.** The current selling price is the latest `item_price_history` row. A GRN line may set a new price (required if the item has none); `POST /api/items/{id}/price` changes it with a reason (`inventory.price`). A price row records the average cost at that moment.
- **Opening stock** is a document of kind Opening; allowed only for items with no movements (also a unique index on Opening movements per item). CSV import (`sku,quantity,unit_cost,selling_price`) checks every row and creates a **draft** for review; it never posts directly.
- **Stocktake** stores counts; posting freezes the system quantity under the item locks and posts one `Count` adjustment for the differences. Lines whose item moved after the count started are flagged.
- **Stock at a date.** `date=YYYY-MM-DD` means the end of that day in Pacific/Auckland (exclusive next-midnight boundary, daylight saving handled); `asOf` is an exact instant; future dates are rejected. Before the first posting every item is 0.
- **Permissions.** New keys `inventory.price` and `inventory.reverse` (Admin by default). `permission_seeds` records keys already given to system roles, so keys added in later phases reach existing roles once and are not re-added after an admin removes them.
- **API enums** are serialized as names (`"Posted"`, `"Grn"`); numbers are still accepted on input.
- **Saving after a rejected change.** The shared catalogue save helper now clears the context's pending changes when the database rejects a save (unique or concurrency), so a later save on the same context does not retry them. Found by the first real PostgreSQL run in CI.

## Open items

- Apply `Phase3Inventory` to the hosted database only after confirming PL/pgSQL triggers work on Prisma Postgres (the migration itself is proven on PostgreSQL 17 in CI).
- Reservations (available stock) arrive in Phase 4 without changing these tables.
- The CI annotations step exists because the dev machine's GitHub CLI login has expired; with a working login, logs can be read directly.
