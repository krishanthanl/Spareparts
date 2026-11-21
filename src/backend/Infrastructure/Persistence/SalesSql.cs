namespace Infrastructure.Persistence;

/// <summary>
/// Database-level sales guarantees for the Phase4aSales migration (ADR 0005). Invoice content is frozen
/// once confirmed, issues and payments are append-only with database timestamps, and no order line
/// can be issued beyond its quantity, even if application code has a bug.
/// </summary>
public static class SalesSql
{
    public const string Up = """
        CREATE FUNCTION sales_order_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            -- Columns the server may still change after Confirm.
            mutable text[] := ARRAY['Status', 'PaymentStatus', 'FulfillmentStatus', 'AmountPaid',
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
        END $$;

        CREATE FUNCTION sales_stamp_issue() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."PostedAt" := now();
            RETURN NEW;
        END $$;

        CREATE FUNCTION sales_stamp_payment() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."ReceivedAt" := now();
            RETURN NEW;
        END $$;

        CREATE FUNCTION sales_no_over_issue() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE ordered numeric; issued numeric;
        BEGIN
            SELECT "Quantity" INTO ordered FROM sales_order_lines WHERE "Id" = NEW."OrderLineId";
            SELECT COALESCE(SUM("Quantity"), 0) INTO issued FROM stock_issue_lines WHERE "OrderLineId" = NEW."OrderLineId";
            IF issued > ordered THEN
                RAISE EXCEPTION 'Order line % would be issued % of %.', NEW."OrderLineId", issued, ordered USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE TRIGGER trg_sales_orders_guard BEFORE UPDATE ON sales_orders FOR EACH ROW EXECUTE FUNCTION sales_order_guard();
        CREATE TRIGGER trg_sales_orders_no_delete BEFORE DELETE ON sales_orders FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_sales_order_lines_draft BEFORE INSERT OR UPDATE OR DELETE ON sales_order_lines
            FOR EACH ROW EXECUTE FUNCTION inv_draft_lines_only('sales_orders', 'OrderId');

        CREATE TRIGGER trg_stock_issues_stamp BEFORE INSERT ON stock_issues FOR EACH ROW EXECUTE FUNCTION sales_stamp_issue();
        CREATE TRIGGER trg_stock_issues_immutable BEFORE UPDATE OR DELETE ON stock_issues FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_stock_issue_lines_immutable BEFORE UPDATE OR DELETE ON stock_issue_lines FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE CONSTRAINT TRIGGER trg_stock_issue_lines_not_over AFTER INSERT ON stock_issue_lines
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sales_no_over_issue();

        CREATE TRIGGER trg_payments_stamp BEFORE INSERT ON payments FOR EACH ROW EXECUTE FUNCTION sales_stamp_payment();
        CREATE TRIGGER trg_payments_immutable BEFORE UPDATE OR DELETE ON payments FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_payments_no_truncate BEFORE TRUNCATE ON payments FOR EACH STATEMENT EXECUTE FUNCTION inv_immutable();
        """;

    public const string Down = """
        DROP TRIGGER IF EXISTS trg_payments_no_truncate ON payments;
        DROP TRIGGER IF EXISTS trg_payments_immutable ON payments;
        DROP TRIGGER IF EXISTS trg_payments_stamp ON payments;
        DROP TRIGGER IF EXISTS trg_stock_issue_lines_not_over ON stock_issue_lines;
        DROP TRIGGER IF EXISTS trg_stock_issue_lines_immutable ON stock_issue_lines;
        DROP TRIGGER IF EXISTS trg_stock_issues_immutable ON stock_issues;
        DROP TRIGGER IF EXISTS trg_stock_issues_stamp ON stock_issues;
        DROP TRIGGER IF EXISTS trg_sales_order_lines_draft ON sales_order_lines;
        DROP TRIGGER IF EXISTS trg_sales_orders_no_delete ON sales_orders;
        DROP TRIGGER IF EXISTS trg_sales_orders_guard ON sales_orders;
        DROP FUNCTION IF EXISTS sales_no_over_issue();
        DROP FUNCTION IF EXISTS sales_stamp_payment();
        DROP FUNCTION IF EXISTS sales_stamp_issue();
        DROP FUNCTION IF EXISTS sales_order_guard();
        """;
}
