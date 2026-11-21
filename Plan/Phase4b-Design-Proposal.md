# Phase 4b Sales: After the Sale — Design Proposal

Status: **approved 2026-11-21** ("Approve, start coding") and built. Open questions were settled as recorded in `docs/decisions/0006-after-sale.md`. Changes while building: a cancellation needs the refund to be **paid out** (not just approved) before the invoice can be cancelled, and the delivery quote is checked when the draft is saved rather than through a separate quote-then-write step.

Spec: `Plan/Masterprompt.md`, `Plan/Phase4- Sales invoices issue returns admin.md` (delivery, non-GST, refunds sections and the 2026-11-17 review).
Builds on: Phase 4a (`sales_orders` as the invoice, derived reservations, `stock_issues` + `ISS-`, append-only `payments` with voids, `document_counters`, `ShopTime`, `SimplePdf`), Phase 3 `StockLedger` lock protocol (`MovementType.Return` already exists).

**4b scope:** delivery settings, zones and quotes; shipments (dispatch/delivery); sales returns; credit notes; manual refunds; refunds column in daily takings; cancelling invoices that have payments.
**Not in 4b:** online checkout, gateway refunds, emailing (Phase 6).

---

## 1. Decisions needed before coding

| # | Decision | Recommendation |
|---|---|---|
| D1 | **Delivery settings** | One `delivery_settings` row (audited, versioned): delivery enabled, pickup enabled, default free-delivery threshold (nullable), service-promise text. Edits never touch existing orders. Key `delivery.configure` (exists, Admin). |
| D2 | **Zones** | `delivery_zones`: name, charge, optional free-over threshold, rural surcharge, service promise ("1–2 working days"), active flag. Each zone owns postcode rules: exact postcodes or ranges (`0600-0699`). NZ postcode = 4 digits. A postcode in no active zone → quote rejected "we don't deliver there" (staff can still override with `sales.discount`-style key? — see Q3). Overlapping rules rejected on save. |
| D3 | **Rural** | A `IsRural` flag on the order's delivery address (staff tick it; NZ Post RD addresses). Rural adds the zone's rural surcharge. No external address lookup. |
| D4 | **Quote** | `POST /orders/{id}/delivery-quote` (draft only) computes charge from zone + subtotal-after-discount vs threshold + rural, and writes the **snapshot** to the order: method, zone name, postcode, rural, charge, promise, `TaxTreatment=NotRegistered`. 4a's typed-in charge stays possible only as a manual override with a reason (Admin). Frozen at Confirm like everything else. |
| D5 | **Shipments** | `shipments` (`SHP-`): order, carrier (free text + common list), tracking ref, status `Pending → Dispatched → Delivered` (or `Cancelled` before dispatch), DispatchedAt/DeliveredAt (DB time), lines → order lines + qty. **Dispatch posts the stock issue** (reuses 4a `StockIssueService` in the same transaction, `ShipmentId` on the issue). Marking delivered changes status only — stock never moves twice (unique link shipment↔issue). Counter pickups keep using plain 4a issue. Key `fulfillment.update` (exists, StoreKeeper + Admin). |
| D6 | **Returns** | `sales_returns` (`RTN-`) against one invoice: lines → original order line, qty, disposition `Restock` or `WriteOff`, reason. Limit per line = issued − prior accepted returns, checked under the order-row lock (concurrent returns serialised) plus a deferred constraint trigger. `Restock` posts a `Return` movement at the **cost of the original issue** (not today's average) so cost of goods reverses exactly; `WriteOff` posts nothing to saleable stock. Append-only. Key `sales.return` (exists, Cashier + Admin). |
| D7 | **Credit notes** | `credit_notes` (`CN-`, gapless). Created from a return (auto, one per return) or standalone for a price/goodwill adjustment (reason, no stock). Lines reference original order lines; amount per line = qty × original net unit price, with the original line discount + order-discount share allocated back by largest remainder, so crediting all units equals the original line total exactly. Delivery credit is a separate optional line, capped at the original delivery charge minus prior delivery credits, reason required. Total credits on an invoice can never exceed its total. Snapshot `NotRegistered`, GST 0. PDF "CREDIT NOTE — Not GST registered - no GST charged". Key `creditnotes.issue` (new, Admin; Cashier only via a return — see Q2). |
| D8 | **Credit ≠ cash** | A credit note lowers what the customer owes. Invoice balance = total − credits − net paid. If that goes negative the customer is owed money; it's paid out only by a refund. |
| D9 | **Refunds** | `refunds` (`RFD-`) + `refund_allocations` (refund → original payment(s), amount). States `Requested → Approved → Processing → Succeeded` / `Failed` / `Cancelled`. Created by Cashier/Admin (`refunds.create`), approved by Admin (`refunds.approve`) and **never by the requester** (server check), completed by Admin (`refunds.execute`) with method + evidence reference required. Idempotency key on create and complete. |
| D10 | **Refund cap** | Under the order-row lock: refundable = net received − (Succeeded + Approved + Processing refunds). Requested amount also capped by "owed to customer" = −balance (credits or overpayment) or, for cancellation, the full net paid. Allocation to a payment ≤ that payment's unrefunded amount. Failed/Cancelled release the hold. |
| D11 | **Cancel with payments** | Before any issue: Cancel is allowed if the net paid is zero **or** a refund for the full net paid exists (Approved+). Cancelling releases reservations. After issue, use return + credit note + refund. |
| D12 | **Daily takings** | Refunds appear on the day they **succeed**, by method, with `RFD-` and invoice numbers; net takings = payments − voids − refunds. Credit notes are listed separately (not cash). |
| D13 | **Reconciliation view** | Per invoice: total, credits, paid, refunded, balance, owed to customer. List filter "customer owed money". No GST reports. |
| D14 | **Time & immutability** | All posting timestamps from the DB; returns, credit notes, refund state changes, shipments' dispatch are append-only/guarded by triggers like 4a. |

### Permission keys

| Key | Admin | Cashier | StoreKeeper |
|---|---|---|---|
| `delivery.configure` (exists) | ✓ | | |
| `fulfillment.update` (exists) | ✓ | | ✓ |
| `sales.return` (exists) | ✓ | ✓ | |
| `creditnotes.issue` (new) | ✓ | | |
| `refunds.create` (exists) | ✓ | ✓ | |
| `refunds.approve`, `refunds.execute` (exist) | ✓ | | |

Menus enabled: Sales → Returns, Credit notes, Refunds; Settings → Delivery.

## 2. Data model (migration `Phase4bAfterSale`)

- **delivery_settings**, **delivery_zones**, **delivery_zone_postcodes** (ZoneId, From, To).
- **sales_orders** + columns: DeliveryZoneName, DeliveryPostcode, DeliveryIsRural, DeliveryPromise, DeliveryChargeOverrideReason, CreditTotal, RefundedTotal.
- **shipments**, **shipment_lines**; `stock_issues.ShipmentId` (nullable, unique).
- **sales_returns**, **sales_return_lines** (OrderLineId, Qty, Disposition, UnitCost, MovementId?).
- **credit_notes**, **credit_note_lines** (OrderLineId? null = delivery line, Qty, Amount), SalesReturnId?.
- **refunds** (Number, OrderId, CreditNoteId?, Amount, Reason, Method, Status, RequestedBy, ApprovedBy/At, CompletedBy/At, EvidenceRef, FailureReason, IdempotencyKey), **refund_allocations** (RefundId, PaymentId, Amount).
- Checks: amounts > 0, GST = 0, currency NZD, approver ≠ requester, allocations sum = refund amount (deferred trigger).

## 3. API

| Method | Route | Permission |
|---|---|---|
| GET/PUT | `/delivery-settings` | delivery.configure (GET: sales.invoice) |
| GET/POST/PUT | `/delivery-zones`, `/delivery-zones/{id}` | delivery.configure |
| POST | `/orders/{id}/delivery-quote` | sales.invoice |
| GET/POST | `/shipments`, `/orders/{id}/shipments` | fulfillment.update |
| POST | `/shipments/{id}/dispatch`, `/deliver`, `/cancel` | fulfillment.update |
| GET/POST | `/sales-returns`, `/orders/{id}/returns` | sales.return |
| GET/POST | `/credit-notes`, `/credit-notes/{id}/pdf` | creditnotes.issue (GET: sales.invoice) |
| GET/POST | `/refunds`, `/orders/{id}/refunds` | refunds.create |
| POST | `/refunds/{id}/approve`, `/complete`, `/fail`, `/cancel` | refunds.approve / refunds.execute |
| GET | `/orders/{id}/balance` | sales.invoice |

## 4. Files

Backend: `Domain/Sales/AfterSaleEntities.cs`, `Domain/Sales/DeliveryEntities.cs`; `Application/Sales/AfterSaleContracts.cs`, `AfterSaleValidators.cs`, `DeliveryMath.cs`, `CreditMath.cs`; `Infrastructure/Sales/DeliveryService.cs`, `ShipmentService.cs`, `SalesReturnService.cs`, `CreditNoteService.cs`, `RefundService.cs`, `CreditNotePdfRenderer.cs`; configs + migration; `Api/Controllers/AfterSaleControllers.cs`; seeder (keys/menus); takings report refunds.
Admin web (`features/sales/`): DeliverySettingsPage, ZonesPage, delivery quote panel in OrderEditor, Shipments panel on OrderDetail, ReturnPage, CreditNoteList/Detail, RefundList/Detail (approve/complete actions), balance panel.
Docs: `docs/decisions/0006-after-sale.md`, `docs/phase4b-after-sale.md`.

## 5. Tests

Unit: zone matching/overlap, rural + free threshold, credit allocation (full return = original total exactly; partial cents never exceed), refund cap math, state transitions, validators reject GST/total injection.
Integration (PostgreSQL, CI): unsupported postcode; quote frozen after rate edit; dispatch issues stock once, deliver doesn't; partial return, over-return rejected, concurrent returns; restock vs write-off stock; credit total ≤ invoice; concurrent over-refund (one wins); duplicate complete idempotent; requester can't approve; cancel-with-refund; takings refund column; historical invoices unchanged.

## 6. Open questions

1. D2: may an Admin override "we don't deliver there" with a manual charge, or is it a hard stop?
2. D7: should Cashiers issue credit notes from a return directly, or only Admin?
3. D6: restock at original issue cost (recommended) or current average cost?
4. Carriers to list (NZ Post, CourierPost, Mainfreight, own van…)?
5. Your zones/charges/free-delivery threshold to seed, or leave empty for you to enter?
