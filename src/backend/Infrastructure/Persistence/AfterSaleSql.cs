namespace Infrastructure.Persistence;

/// <summary>
/// Database-level after-sale guarantees for the Phase4bAfterSale migration (ADR 0006). Returns and credit
/// notes are append-only with database timestamps; nobody can return more than was issued, credit more
/// than was charged, or allocate a refund beyond what a receipt brought in, even if application code has a bug.
/// </summary>
public static class AfterSaleSql
{
    // Same rules as Phase 4a, plus the two after-sale running totals the server keeps on a confirmed invoice.
    private const string GuardBody = """
        DECLARE
            mutable text[] := ARRAY['Status', 'PaymentStatus', 'FulfillmentStatus', 'AmountPaid', 'CreditTotal', 'RefundedTotal',
                'CancelledAt', 'CancelledBy', 'CancelReason', 'ConfirmedAt', 'UpdatedAt', 'UpdatedBy'];
        BEGIN
            IF NEW."Status" IS DISTINCT FROM OLD."Status" THEN
                IF NOT ((OLD."Status" = 1 AND NEW."Status" IN (2, 4))
                     OR (OLD."Status" = 2 AND NEW."Status" IN (3, 4))
                     OR (OLD."Status" = 3 AND NEW."Status" = 2)) THEN
                    RAISE EXCEPTION 'Invalid sales order status change (% to %).', OLD."Status", NEW."Status" USING ERRCODE = 'P0001';
                END IF;
                IF NEW."Status" = 4 THEN
                    IF EXISTS (SELECT 1 FROM stock_issues WHERE "OrderId" = NEW."Id") THEN
                        RAISE EXCEPTION 'An order with issued goods cannot be cancelled.' USING ERRCODE = 'P0001';
                    END IF;
                    IF NEW."AmountPaid" - NEW."RefundedTotal" <> 0 THEN
                        RAISE EXCEPTION 'Refund the payments before cancelling.' USING ERRCODE = 'P0001';
                    END IF;
                    NEW."CancelledAt" := now();
                END IF;
            END IF;
            IF OLD."Status" = 1 AND NEW."Status" = 2 THEN
                NEW."ConfirmedAt" := now();
            ELSE
                NEW."ConfirmedAt" := OLD."ConfirmedAt";
            END IF;
            IF OLD."Status" <> 1 AND (to_jsonb(NEW) - mutable) IS DISTINCT FROM (to_jsonb(OLD) - mutable) THEN
                RAISE EXCEPTION 'A confirmed invoice cannot be edited.' USING ERRCODE = 'P0001';
            END IF;
            IF OLD."Status" = 4 THEN
                RAISE EXCEPTION 'A cancelled order cannot change.' USING ERRCODE = 'P0001';
            END IF;
            RETURN NEW;
        END
        """;

    public const string Up = """
        CREATE OR REPLACE FUNCTION sales_order_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        """ + GuardBody + """
         $$;

        CREATE FUNCTION after_sale_stamp_posted() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."PostedAt" := now();
            RETURN NEW;
        END $$;

        CREATE FUNCTION after_sale_stamp_issued() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."IssuedAt" := now();
            RETURN NEW;
        END $$;

        -- Returned quantity per order line can never exceed what was issued.
        CREATE FUNCTION after_sale_no_over_return() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE issued numeric; returned numeric;
        BEGIN
            SELECT COALESCE(SUM("Quantity"), 0) INTO issued FROM stock_issue_lines WHERE "OrderLineId" = NEW."OrderLineId";
            SELECT COALESCE(SUM("Quantity"), 0) INTO returned FROM sales_return_lines WHERE "OrderLineId" = NEW."OrderLineId";
            IF returned > issued THEN
                RAISE EXCEPTION 'Order line % would have % returned of % issued.', NEW."OrderLineId", returned, issued USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        -- Credits per goods line stay within the line total, and delivery credits within the delivery charge.
        CREATE FUNCTION after_sale_no_over_credit() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE cap numeric; credited numeric; order_id uuid;
        BEGIN
            IF NEW."OrderLineId" IS NOT NULL THEN
                SELECT "LineTotal" INTO cap FROM sales_order_lines WHERE "Id" = NEW."OrderLineId";
                SELECT COALESCE(SUM("Amount"), 0) INTO credited FROM credit_note_lines WHERE "OrderLineId" = NEW."OrderLineId";
            ELSE
                SELECT "OrderId" INTO order_id FROM credit_notes WHERE "Id" = NEW."CreditNoteId";
                SELECT "DeliveryCharge" INTO cap FROM sales_orders WHERE "Id" = order_id;
                SELECT COALESCE(SUM(l."Amount"), 0) INTO credited FROM credit_note_lines l
                    JOIN credit_notes c ON c."Id" = l."CreditNoteId" WHERE c."OrderId" = order_id AND l."OrderLineId" IS NULL;
            END IF;
            IF credited > cap THEN
                RAISE EXCEPTION 'Credits of % would exceed the original % charged.', credited, cap USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE FUNCTION after_sale_credit_note_total() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE lines numeric;
        BEGIN
            SELECT COALESCE(SUM("Amount"), 0) INTO lines FROM credit_note_lines WHERE "CreditNoteId" = NEW."Id";
            IF lines <> NEW."Total" THEN
                RAISE EXCEPTION 'Credit note % lines add up to %, not %.', NEW."CreditNoteNumber", lines, NEW."Total" USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        -- Allocations add up to the refund, and no receipt is refunded beyond its amount (live refunds only).
        CREATE FUNCTION after_sale_refund_allocations() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE total numeric; allocated numeric; paid numeric; used numeric;
        BEGIN
            SELECT "Amount" INTO total FROM refunds WHERE "Id" = NEW."RefundId";
            SELECT COALESCE(SUM("Amount"), 0) INTO allocated FROM refund_allocations WHERE "RefundId" = NEW."RefundId";
            IF allocated <> total THEN
                RAISE EXCEPTION 'Refund allocations add up to %, not %.', allocated, total USING ERRCODE = '23514';
            END IF;
            SELECT "Amount" INTO paid FROM payments WHERE "Id" = NEW."PaymentId";
            SELECT COALESCE(SUM(a."Amount"), 0) INTO used FROM refund_allocations a JOIN refunds r ON r."Id" = a."RefundId"
                WHERE a."PaymentId" = NEW."PaymentId" AND r."Status" NOT IN (5, 6);
            IF used > paid THEN
                RAISE EXCEPTION 'Receipt % would be refunded % of %.', NEW."PaymentId", used, paid USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE FUNCTION after_sale_refund_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            mutable text[] := ARRAY['Status', 'ApprovedAt', 'ApprovedBy', 'CompletedAt', 'CompletedBy', 'EvidenceReference',
                'FailureReason', 'CompletionKey', 'Method', 'UpdatedAt', 'UpdatedBy', 'xmin'];
        BEGIN
            IF (to_jsonb(NEW) - mutable) IS DISTINCT FROM (to_jsonb(OLD) - mutable) THEN
                RAISE EXCEPTION 'Only the status of a refund can change.' USING ERRCODE = 'P0001';
            END IF;
            IF NEW."Status" IS DISTINCT FROM OLD."Status" THEN
                IF NOT ((OLD."Status" = 1 AND NEW."Status" IN (2, 6))
                     OR (OLD."Status" = 2 AND NEW."Status" IN (3, 4, 5, 6))
                     OR (OLD."Status" = 3 AND NEW."Status" IN (4, 5))) THEN
                    RAISE EXCEPTION 'Invalid refund status change (% to %).', OLD."Status", NEW."Status" USING ERRCODE = 'P0001';
                END IF;
                IF NEW."Status" = 2 THEN NEW."ApprovedAt" := now(); ELSE NEW."ApprovedAt" := OLD."ApprovedAt"; END IF;
                IF NEW."Status" IN (4, 5, 6) THEN NEW."CompletedAt" := now(); END IF;
            ELSE
                IF OLD."Status" IN (4, 5, 6) THEN
                    RAISE EXCEPTION 'A finished refund cannot change.' USING ERRCODE = 'P0001';
                END IF;
                NEW."ApprovedAt" := OLD."ApprovedAt";
                NEW."CompletedAt" := OLD."CompletedAt";
            END IF;
            RETURN NEW;
        END $$;

        CREATE FUNCTION after_sale_shipment_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NEW."OrderId" <> OLD."OrderId" OR NEW."ShipmentNumber" <> OLD."ShipmentNumber" THEN
                RAISE EXCEPTION 'A shipment cannot move to another order.' USING ERRCODE = 'P0001';
            END IF;
            IF OLD."StockIssueId" IS NOT NULL AND NEW."StockIssueId" IS DISTINCT FROM OLD."StockIssueId" THEN
                RAISE EXCEPTION 'A dispatched shipment keeps its stock issue.' USING ERRCODE = 'P0001';
            END IF;
            IF NEW."Status" IS DISTINCT FROM OLD."Status" THEN
                IF NOT ((OLD."Status" = 1 AND NEW."Status" IN (2, 4)) OR (OLD."Status" = 2 AND NEW."Status" = 3)) THEN
                    RAISE EXCEPTION 'Invalid shipment status change (% to %).', OLD."Status", NEW."Status" USING ERRCODE = 'P0001';
                END IF;
                IF NEW."Status" = 2 THEN
                    IF NEW."StockIssueId" IS NULL THEN
                        RAISE EXCEPTION 'Dispatch must post a stock issue.' USING ERRCODE = 'P0001';
                    END IF;
                    NEW."DispatchedAt" := now();
                END IF;
                IF NEW."Status" = 3 THEN NEW."DeliveredAt" := now(); END IF;
                IF NEW."Status" = 4 THEN NEW."CancelledAt" := now(); END IF;
            ELSE
                IF OLD."Status" IN (3, 4) THEN
                    RAISE EXCEPTION 'A delivered or cancelled shipment cannot change.' USING ERRCODE = 'P0001';
                END IF;
                NEW."DispatchedAt" := OLD."DispatchedAt";
                NEW."DeliveredAt" := OLD."DeliveredAt";
                NEW."CancelledAt" := OLD."CancelledAt";
            END IF;
            RETURN NEW;
        END $$;

        CREATE TRIGGER trg_sales_returns_stamp BEFORE INSERT ON sales_returns FOR EACH ROW EXECUTE FUNCTION after_sale_stamp_posted();
        CREATE TRIGGER trg_sales_returns_immutable BEFORE UPDATE OR DELETE ON sales_returns FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_sales_return_lines_immutable BEFORE UPDATE OR DELETE ON sales_return_lines FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE CONSTRAINT TRIGGER trg_sales_return_lines_not_over AFTER INSERT ON sales_return_lines
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION after_sale_no_over_return();

        CREATE TRIGGER trg_credit_notes_stamp BEFORE INSERT ON credit_notes FOR EACH ROW EXECUTE FUNCTION after_sale_stamp_issued();
        CREATE TRIGGER trg_credit_notes_immutable BEFORE UPDATE OR DELETE ON credit_notes FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_credit_note_lines_immutable BEFORE UPDATE OR DELETE ON credit_note_lines FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE CONSTRAINT TRIGGER trg_credit_note_lines_not_over AFTER INSERT ON credit_note_lines
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION after_sale_no_over_credit();
        CREATE CONSTRAINT TRIGGER trg_credit_notes_total AFTER INSERT ON credit_notes
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION after_sale_credit_note_total();

        CREATE TRIGGER trg_refunds_guard BEFORE UPDATE ON refunds FOR EACH ROW EXECUTE FUNCTION after_sale_refund_guard();
        CREATE TRIGGER trg_refunds_no_delete BEFORE DELETE ON refunds FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_refund_allocations_immutable BEFORE UPDATE OR DELETE ON refund_allocations FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE CONSTRAINT TRIGGER trg_refund_allocations_check AFTER INSERT ON refund_allocations
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION after_sale_refund_allocations();

        CREATE TRIGGER trg_shipments_guard BEFORE UPDATE ON shipments FOR EACH ROW EXECUTE FUNCTION after_sale_shipment_guard();
        CREATE TRIGGER trg_shipments_no_delete BEFORE DELETE ON shipments FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_shipment_lines_immutable BEFORE UPDATE OR DELETE ON shipment_lines FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        """;

    /// <summary>The Phase 4a guard: same body without the after-sale totals and the refund check.</summary>
    private static readonly string LegacyGuard = "CREATE OR REPLACE FUNCTION sales_order_guard() RETURNS trigger LANGUAGE plpgsql AS $$\n"
        + GuardBody.Replace("'AmountPaid', 'CreditTotal', 'RefundedTotal',", "'AmountPaid',")
            .Replace("""
                    IF NEW."AmountPaid" - NEW."RefundedTotal" <> 0 THEN
                        RAISE EXCEPTION 'Refund the payments before cancelling.' USING ERRCODE = 'P0001';
                    END IF;

        """, "")
        + " $$;\n";

    public static readonly string Down = LegacyGuard + """
        DROP TRIGGER IF EXISTS trg_shipment_lines_immutable ON shipment_lines;
        DROP TRIGGER IF EXISTS trg_shipments_no_delete ON shipments;
        DROP TRIGGER IF EXISTS trg_shipments_guard ON shipments;
        DROP TRIGGER IF EXISTS trg_refund_allocations_check ON refund_allocations;
        DROP TRIGGER IF EXISTS trg_refund_allocations_immutable ON refund_allocations;
        DROP TRIGGER IF EXISTS trg_refunds_no_delete ON refunds;
        DROP TRIGGER IF EXISTS trg_refunds_guard ON refunds;
        DROP TRIGGER IF EXISTS trg_credit_notes_total ON credit_notes;
        DROP TRIGGER IF EXISTS trg_credit_note_lines_not_over ON credit_note_lines;
        DROP TRIGGER IF EXISTS trg_credit_note_lines_immutable ON credit_note_lines;
        DROP TRIGGER IF EXISTS trg_credit_notes_immutable ON credit_notes;
        DROP TRIGGER IF EXISTS trg_credit_notes_stamp ON credit_notes;
        DROP TRIGGER IF EXISTS trg_sales_return_lines_not_over ON sales_return_lines;
        DROP TRIGGER IF EXISTS trg_sales_return_lines_immutable ON sales_return_lines;
        DROP TRIGGER IF EXISTS trg_sales_returns_immutable ON sales_returns;
        DROP TRIGGER IF EXISTS trg_sales_returns_stamp ON sales_returns;
        DROP FUNCTION IF EXISTS after_sale_shipment_guard();
        DROP FUNCTION IF EXISTS after_sale_refund_guard();
        DROP FUNCTION IF EXISTS after_sale_refund_allocations();
        DROP FUNCTION IF EXISTS after_sale_credit_note_total();
        DROP FUNCTION IF EXISTS after_sale_no_over_credit();
        DROP FUNCTION IF EXISTS after_sale_no_over_return();
        DROP FUNCTION IF EXISTS after_sale_stamp_issued();
        DROP FUNCTION IF EXISTS after_sale_stamp_posted();
        """;
}
