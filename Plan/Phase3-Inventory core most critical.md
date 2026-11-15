Phase 3: Inventory (GRN, ledger, adjustments).
Design the DB first and show an ERD + the SQL for the "stock at date" query before coding.
Tables (propose final names/columns):
- Grn (GrnNumber, SupplierName (free text, no purchase order), GrnDate, Notes, Status)
- GrnLine (GrnId, ItemId, Quantity, UnitCost, SellingPriceAtDate)
- ItemPriceHistory (ItemId, EffectiveFrom, CostPrice, SellingPrice, SourceType, SourceId)
- StockMovement: append-only ledger (ItemId, MovementType [Grn, Adjustment, Issue,
  Return], QtyChange (+/-), OccurredAt, RefType, RefId, UnitCost, CreatedAt, CreatedBy)
- StockAdjustment + StockAdjustmentLine (Reason, Quantity +/-)
Rules:
- Posting a GRN inserts StockMovement rows and ItemPriceHistory rows in one DB transaction.
- No UPDATE/DELETE on StockMovement. Corrections are done by reversal movements.
- Stock adjustments (damage, count correction, etc.) also create movements.
- Backdating is forbidden. Server assigns posting time; source/supplier dates may be retained as metadata but cannot determine ledger effective time.
- Negative on-hand and negative available stock are forbidden with no user-configurable bypass.
Endpoints: create GRN, view/list GRN, create adjustment, current stock list,
`GET /stock?asOf=2026-03-31` (point-in-time), stock movement history per item,
low-stock report (items at or below ReorderLevel, showing ReorderQuantity).
Admin UI: GRN entry screen (add lines quickly, item search), adjustment screen,
stock report with an "As of date" picker, low-stock list, item stock card (ledger view).
Tests: stock at several dates, rejection of backdated/future-effective posting, concurrent postings, price history captured.

## Decisions and database guarantees
- One shop and one stock location only; NZD and Pacific/Auckland. Negative stock and backdating are forbidden. Approve quantity precision and valuation before schema creation.
- Use Draft/Posted/Reversed states and explicit posting/reversal actions. Posted lines and receipt-date prices are immutable.
- Add source-line references, reversal links and unique idempotency/posting keys. Every table has all four audit fields; immutable rows retain creation-time update metadata under the documented convention.
- Post document status, movements and price history in one transaction. Enforce ledger immutability through an approved PostgreSQL trigger/privilege strategy.
- Serialize conflicting writes per item with deterministic lock ordering. A read-then-insert check alone cannot prevent concurrent overselling.
- Clients cannot choose ledger effective timestamps. Reversals, deductions and opening balances post at the server-controlled current UTC time; no rewrite of historical balances.
- Define deterministic price ordering and corrections. Posted price history uses the GRN posting time; neither backdated nor future-effective posting is supported.

## Stock query contract
Present executable parameterized SQL before coding:
SELECT item_id, COALESCE(SUM(qty_change), 0) AS quantity
FROM stock_movements WHERE occurred_at <= @as_of_utc GROUP BY item_id;
Use an item LEFT JOIN to include zero-stock items and index (item_id, occurred_at).
asOf means an explicit instant. A calendar-date picker converts the next shop-local midnight to an exclusive UTC boundary using <.
Null reorder thresholds are excluded; equality triggers low stock. Phase 4 reservations affect available stock, never on-hand.

## Acceptance
Real PostgreSQL tests cover immutable-row protection, atomic rollback, duplicate posting, concurrent deductions, historical balances, date boundaries and reversals. Receipt prices remain unchanged after later GRNs. Do not implement sales/returns in this phase.


## Phase working agreement
Read Masterprompt.md first. Before coding, submit files, DB changes, endpoints, screens and tests for approval. Use FluentValidation, ProblemDetails and new EF migrations. Finish with built features, run/test instructions and actual results, open questions and logical commits when Git is configured.



## Confirmed shop constraints
- Exactly one shop with one stock location. No branches, warehouse tables/selectors, stock transfers or multi-location balances.
- GST status: not registered. No GST charged on goods or delivery; retain NotRegistered document snapshots. Future registration requires separate approval.
- Currency: NZD; locale: en-NZ; business timezone: Pacific/Auckland with daylight saving. Persist UTC timestamptz and render/report in shop time, independent of the user's computer timezone.
- Negative on-hand and available stock are never permitted. Backdating is forbidden; all posted business events use trusted server-controlled current UTC time.
- Historical as-of reads remain supported; they do not authorize historical writes. Source dates are informational only and never override posting, tax-document or ledger effective dates.
- Azure hosts the application; Prisma Postgres hosts the PostgreSQL database, starting on the Free plan. EF Core/Npgsql and EF migrations remain authoritative; Prisma ORM is not introduced.

## Strict posting and stock acceptance
- Drafts may be edited; posting time is assigned only when successfully posted. Reject submitted effective timestamps or document dates intended to backdate/future-date a posting.
- Keep supplier source date separate from GrnDate/posting time. Opening stock posts now, so the system makes no claim about stock before initialization.
- A reversal of an old receipt occurs now and must be rejected if it would breach on-hand or reserved availability constraints.
- Deductions preserve active reservations; issuing against one's own reservation consumes that reservation atomically rather than subtracting it twice.
- Transaction checks and ledger insertions must serialize per item even when separate Azure instances/workers submit requests.
- Price EffectiveFrom is server posting time; source dates never retrospectively change prices.
- Test backdating attempts, future-effective attempts, insufficient stock, receipt reversal with reservations, concurrent deductions, and NZ daylight-saving/calendar boundaries. Historical queries before first posting return zero.

