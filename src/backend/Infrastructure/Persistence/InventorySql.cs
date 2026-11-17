namespace Infrastructure.Persistence;

/// <summary>
/// Database-level stock guarantees for the Phase3Inventory migration (ADR 0004). They hold even if
/// application code has a bug: posting times come from the database clock, ledger rows cannot change,
/// lines of posted documents cannot change, and no item's stock can be negative at commit.
/// </summary>
public static class InventorySql
{
    public const string Up = """
        CREATE FUNCTION inv_stamp_movement() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."OccurredAt" := now();
            RETURN NEW;
        END $$;

        CREATE FUNCTION inv_stamp_price() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."EffectiveFrom" := now();
            RETURN NEW;
        END $$;

        CREATE FUNCTION inv_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Rows in % cannot be changed or deleted; post a reversal instead.', TG_TABLE_NAME
                USING ERRCODE = 'P0001';
        END $$;

        CREATE FUNCTION inv_no_negative_stock() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE balance numeric;
        BEGIN
            SELECT COALESCE(SUM("QtyChange"), 0) INTO balance FROM stock_movements WHERE "ItemId" = NEW."ItemId";
            IF balance < 0 THEN
                RAISE EXCEPTION 'Stock for item % would be negative (%).', NEW."ItemId", balance USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE FUNCTION inv_stamp_document() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NEW."Status" = 2 AND OLD."Status" = 1 THEN
                NEW."PostedAt" := now();
            ELSIF NEW."Status" IS DISTINCT FROM OLD."Status" OR NEW."PostedAt" IS DISTINCT FROM OLD."PostedAt" THEN
                -- Only Draft->Posted, Draft->Cancelled and (GRN) Posted->Reversed are allowed.
                IF NOT ((OLD."Status" = 1 AND NEW."Status" = 4) OR (OLD."Status" = 2 AND NEW."Status" = 3 AND TG_TABLE_NAME = 'grns')) THEN
                    RAISE EXCEPTION 'Invalid status change on %.', TG_TABLE_NAME USING ERRCODE = 'P0001';
                END IF;
                NEW."PostedAt" := OLD."PostedAt";
            END IF;
            IF TG_TABLE_NAME = 'grns' AND NEW."Status" = 3 AND OLD."Status" = 2 THEN
                NEW."ReversedAt" := now();
            END IF;
            RETURN NEW;
        END $$;

        -- TG_ARGV[0] = parent table, TG_ARGV[1] = foreign key column. Lines may change only while the parent is a draft.
        CREATE FUNCTION inv_draft_lines_only() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE parent_status int;
        DECLARE parent_id uuid;
        BEGIN
            IF TG_OP = 'DELETE' THEN
                parent_id := (to_jsonb(OLD) ->> TG_ARGV[1])::uuid;
            ELSE
                parent_id := (to_jsonb(NEW) ->> TG_ARGV[1])::uuid;
            END IF;
            EXECUTE format('SELECT "Status" FROM %I WHERE "Id" = $1', TG_ARGV[0]) INTO parent_status USING parent_id;
            IF parent_status IS DISTINCT FROM 1 THEN
                RAISE EXCEPTION 'Lines of a posted or cancelled document cannot change (%).', TG_TABLE_NAME USING ERRCODE = 'P0001';
            END IF;
            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $$;

        CREATE TRIGGER trg_grn_lines_draft BEFORE INSERT OR UPDATE OR DELETE ON grn_lines
            FOR EACH ROW EXECUTE FUNCTION inv_draft_lines_only('grns', 'GrnId');
        CREATE TRIGGER trg_stock_document_lines_draft BEFORE INSERT OR UPDATE OR DELETE ON stock_document_lines
            FOR EACH ROW EXECUTE FUNCTION inv_draft_lines_only('stock_documents', 'DocumentId');
        CREATE TRIGGER trg_stocktake_lines_draft BEFORE INSERT OR UPDATE OR DELETE ON stocktake_lines
            FOR EACH ROW EXECUTE FUNCTION inv_draft_lines_only('stocktakes', 'StocktakeId');

        CREATE TRIGGER trg_stock_movements_stamp BEFORE INSERT ON stock_movements FOR EACH ROW EXECUTE FUNCTION inv_stamp_movement();
        CREATE TRIGGER trg_stock_movements_immutable BEFORE UPDATE OR DELETE ON stock_movements FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_stock_movements_no_truncate BEFORE TRUNCATE ON stock_movements FOR EACH STATEMENT EXECUTE FUNCTION inv_immutable();
        CREATE CONSTRAINT TRIGGER trg_stock_movements_not_negative AFTER INSERT ON stock_movements
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION inv_no_negative_stock();

        CREATE TRIGGER trg_item_price_history_stamp BEFORE INSERT ON item_price_history FOR EACH ROW EXECUTE FUNCTION inv_stamp_price();
        CREATE TRIGGER trg_item_price_history_immutable BEFORE UPDATE OR DELETE ON item_price_history FOR EACH ROW EXECUTE FUNCTION inv_immutable();
        CREATE TRIGGER trg_item_price_history_no_truncate BEFORE TRUNCATE ON item_price_history FOR EACH STATEMENT EXECUTE FUNCTION inv_immutable();

        CREATE TRIGGER trg_grns_stamp BEFORE UPDATE ON grns FOR EACH ROW EXECUTE FUNCTION inv_stamp_document();
        CREATE TRIGGER trg_stock_documents_stamp BEFORE UPDATE ON stock_documents FOR EACH ROW EXECUTE FUNCTION inv_stamp_document();
        CREATE TRIGGER trg_stocktakes_stamp BEFORE UPDATE ON stocktakes FOR EACH ROW EXECUTE FUNCTION inv_stamp_document();
        """;

    public const string Down = """
        DROP TRIGGER IF EXISTS trg_stocktake_lines_draft ON stocktake_lines;
        DROP TRIGGER IF EXISTS trg_stock_document_lines_draft ON stock_document_lines;
        DROP TRIGGER IF EXISTS trg_grn_lines_draft ON grn_lines;
        DROP FUNCTION IF EXISTS inv_draft_lines_only();
        DROP TRIGGER IF EXISTS trg_stocktakes_stamp ON stocktakes;
        DROP TRIGGER IF EXISTS trg_stock_documents_stamp ON stock_documents;
        DROP TRIGGER IF EXISTS trg_grns_stamp ON grns;
        DROP TRIGGER IF EXISTS trg_item_price_history_no_truncate ON item_price_history;
        DROP TRIGGER IF EXISTS trg_item_price_history_immutable ON item_price_history;
        DROP TRIGGER IF EXISTS trg_item_price_history_stamp ON item_price_history;
        DROP TRIGGER IF EXISTS trg_stock_movements_not_negative ON stock_movements;
        DROP TRIGGER IF EXISTS trg_stock_movements_no_truncate ON stock_movements;
        DROP TRIGGER IF EXISTS trg_stock_movements_immutable ON stock_movements;
        DROP TRIGGER IF EXISTS trg_stock_movements_stamp ON stock_movements;
        DROP FUNCTION IF EXISTS inv_stamp_document();
        DROP FUNCTION IF EXISTS inv_no_negative_stock();
        DROP FUNCTION IF EXISTS inv_immutable();
        DROP FUNCTION IF EXISTS inv_stamp_price();
        DROP FUNCTION IF EXISTS inv_stamp_movement();
        """;
}
