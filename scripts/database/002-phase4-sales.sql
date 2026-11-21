START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE customers (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Phone" character varying(50),
        "Email" character varying(200),
        "Company" character varying(200),
        "BillingAddress" character varying(500),
        "DeliveryAddress" character varying(500),
        "Notes" character varying(1000),
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_customers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE sales_orders (
        "Id" uuid NOT NULL,
        "InvoiceNumber" character varying(20),
        "Source" integer NOT NULL,
        "Status" integer NOT NULL,
        "PaymentStatus" integer NOT NULL,
        "FulfillmentStatus" integer NOT NULL,
        "PaymentTerms" integer NOT NULL,
        "CustomerId" uuid,
        "CustomerName" character varying(200),
        "CustomerPhone" character varying(50),
        "CustomerEmail" character varying(200),
        "CustomerCompany" character varying(200),
        "BillingAddress" character varying(500),
        "DeliveryMethod" integer NOT NULL,
        "DeliveryAddress" character varying(500),
        "DeliveryCharge" numeric(18,2) NOT NULL,
        "Subtotal" numeric(18,2) NOT NULL,
        "LineDiscountTotal" numeric(18,2) NOT NULL,
        "OrderDiscount" numeric(18,2) NOT NULL,
        "DiscountReason" character varying(500),
        "Total" numeric(18,2) NOT NULL,
        "AmountPaid" numeric(18,2) NOT NULL,
        "TaxTreatment" integer NOT NULL,
        "GstAmount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Notes" character varying(1000),
        "ConfirmedAt" timestamp with time zone,
        "ConfirmedBy" character varying(200),
        "CancelledAt" timestamp with time zone,
        "CancelledBy" character varying(200),
        "CancelReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_sales_orders" PRIMARY KEY ("Id"),
        CONSTRAINT ck_sales_orders_currency CHECK ("Currency" = 'NZD'),
        CONSTRAINT ck_sales_orders_money CHECK ("Subtotal" >= 0 AND "LineDiscountTotal" >= 0 AND "OrderDiscount" >= 0 AND "DeliveryCharge" >= 0 AND "Total" >= 0),
        CONSTRAINT ck_sales_orders_no_gst CHECK ("TaxTreatment" = 1 AND "GstAmount" = 0),
        CONSTRAINT ck_sales_orders_number CHECK (("Status" <> 1 OR "InvoiceNumber" IS NULL) AND ("Status" NOT IN (2, 3) OR "InvoiceNumber" IS NOT NULL)),
        CONSTRAINT ck_sales_orders_total CHECK ("Total" = "Subtotal" - "LineDiscountTotal" - "OrderDiscount" + "DeliveryCharge"),
        CONSTRAINT "FK_sales_orders_customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES customers ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE payments (
        "Id" uuid NOT NULL,
        "ReceiptNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "Method" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Reference" character varying(200),
        "ReceivedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "IdempotencyKey" character varying(100) NOT NULL,
        "VoidsPaymentId" uuid,
        "VoidReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_payments" PRIMARY KEY ("Id"),
        CONSTRAINT ck_payments_amount CHECK (("VoidsPaymentId" IS NULL AND "Amount" > 0) OR ("VoidsPaymentId" IS NOT NULL AND "Amount" < 0)),
        CONSTRAINT "FK_payments_payments_VoidsPaymentId" FOREIGN KEY ("VoidsPaymentId") REFERENCES payments ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_payments_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE sales_order_lines (
        "Id" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "LineNo" integer NOT NULL,
        "ItemId" uuid NOT NULL,
        "Sku" character varying(64) NOT NULL,
        "Description" character varying(300) NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "ListPrice" numeric(18,2) NOT NULL,
        "UnitPrice" numeric(18,2) NOT NULL,
        "LineDiscount" numeric(18,2) NOT NULL,
        "OrderDiscountShare" numeric(18,2) NOT NULL,
        "LineTotal" numeric(18,2) NOT NULL,
        "DiscountReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_sales_order_lines" PRIMARY KEY ("Id"),
        CONSTRAINT ck_sales_order_lines_money CHECK ("ListPrice" >= 0 AND "UnitPrice" >= 0 AND "LineDiscount" >= 0 AND "OrderDiscountShare" >= 0 AND "LineTotal" >= 0),
        CONSTRAINT ck_sales_order_lines_qty CHECK ("Quantity" > 0),
        CONSTRAINT "FK_sales_order_lines_items_ItemId" FOREIGN KEY ("ItemId") REFERENCES items ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_sales_order_lines_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE stock_issues (
        "Id" uuid NOT NULL,
        "IssueNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "PostedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "IdempotencyKey" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_stock_issues" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_stock_issues_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE TABLE stock_issue_lines (
        "Id" uuid NOT NULL,
        "IssueId" uuid NOT NULL,
        "OrderLineId" uuid NOT NULL,
        "ItemId" uuid NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "MovementId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_stock_issue_lines" PRIMARY KEY ("Id"),
        CONSTRAINT ck_stock_issue_lines_qty CHECK ("Quantity" > 0),
        CONSTRAINT "FK_stock_issue_lines_sales_order_lines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES sales_order_lines ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_stock_issue_lines_stock_issues_IssueId" FOREIGN KEY ("IssueId") REFERENCES stock_issues ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_stock_issue_lines_stock_movements_MovementId" FOREIGN KEY ("MovementId") REFERENCES stock_movements ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_customers_Name" ON customers ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_customers_Phone" ON customers ("Phone");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_payments_IdempotencyKey" ON payments ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_payments_OrderId_ReceivedAt" ON payments ("OrderId", "ReceivedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_payments_ReceiptNumber" ON payments ("ReceiptNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_payments_ReceivedAt" ON payments ("ReceivedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_payments_VoidsPaymentId" ON payments ("VoidsPaymentId") WHERE "VoidsPaymentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_sales_order_lines_ItemId" ON sales_order_lines ("ItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_sales_order_lines_OrderId_LineNo" ON sales_order_lines ("OrderId", "LineNo");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_sales_orders_CustomerId" ON sales_orders ("CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_sales_orders_InvoiceNumber" ON sales_orders ("InvoiceNumber") WHERE "InvoiceNumber" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_sales_orders_Status_ConfirmedAt" ON sales_orders ("Status", "ConfirmedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_sales_orders_UpdatedAt" ON sales_orders ("UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_stock_issue_lines_IssueId" ON stock_issue_lines ("IssueId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_stock_issue_lines_MovementId" ON stock_issue_lines ("MovementId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_stock_issue_lines_OrderLineId" ON stock_issue_lines ("OrderLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_stock_issues_IdempotencyKey" ON stock_issues ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE UNIQUE INDEX "IX_stock_issues_IssueNumber" ON stock_issues ("IssueNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    CREATE INDEX "IX_stock_issues_OrderId" ON stock_issues ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121064438_Phase4aSales') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261121064438_Phase4aSales', '10.0.15');
    END IF;
END $EF$;
COMMIT;

-- Phase 4a and 4b. Generated by EF Core for PostgreSQL.
-- Requires the Phase 3 inventory protection migration. Run with ON_ERROR_STOP.
DO $preflight$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20261117165846_Phase3InventoryProtection'
    ) THEN
        RAISE EXCEPTION 'Apply 20261117165846_Phase3InventoryProtection before Phase 4.';
    END IF;
END
$preflight$;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE stock_issues ADD "ShipmentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "CreditTotal" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "DeliveryChargeReason" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "DeliveryIsRural" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "DeliveryPostcode" character varying(4);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "DeliveryPromise" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "DeliveryZoneName" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD "RefundedTotal" numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE delivery_settings (
        "Id" uuid NOT NULL,
        "DeliveryEnabled" boolean NOT NULL,
        "PickupEnabled" boolean NOT NULL,
        "FreeDeliveryThreshold" numeric(18,2),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_delivery_settings" PRIMARY KEY ("Id"),
        CONSTRAINT ck_delivery_settings_money CHECK ("FreeDeliveryThreshold" IS NULL OR "FreeDeliveryThreshold" >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE delivery_zones (
        "Id" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Charge" numeric(18,2) NOT NULL,
        "FreeDeliveryThreshold" numeric(18,2),
        "RuralSurcharge" numeric(18,2) NOT NULL,
        "ServicePromise" character varying(200),
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_delivery_zones" PRIMARY KEY ("Id"),
        CONSTRAINT ck_delivery_zones_money CHECK ("Charge" >= 0 AND "RuralSurcharge" >= 0 AND ("FreeDeliveryThreshold" IS NULL OR "FreeDeliveryThreshold" >= 0))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE sales_returns (
        "Id" uuid NOT NULL,
        "ReturnNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "PostedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "IdempotencyKey" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_sales_returns" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_sales_returns_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE shipments (
        "Id" uuid NOT NULL,
        "ShipmentNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "Carrier" character varying(100),
        "TrackingReference" character varying(200),
        "Notes" character varying(1000),
        "StockIssueId" uuid,
        "DispatchedAt" timestamp with time zone,
        "DispatchedBy" character varying(200),
        "DeliveredAt" timestamp with time zone,
        "DeliveredBy" character varying(200),
        "CancelledAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_shipments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_shipments_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_shipments_stock_issues_StockIssueId" FOREIGN KEY ("StockIssueId") REFERENCES stock_issues ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE delivery_zone_postcodes (
        "Id" uuid NOT NULL,
        "ZoneId" uuid NOT NULL,
        "From" integer NOT NULL,
        "To" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_delivery_zone_postcodes" PRIMARY KEY ("Id"),
        CONSTRAINT ck_delivery_zone_postcodes_range CHECK ("From" BETWEEN 0 AND 9999 AND "To" BETWEEN "From" AND 9999),
        CONSTRAINT "FK_delivery_zone_postcodes_delivery_zones_ZoneId" FOREIGN KEY ("ZoneId") REFERENCES delivery_zones ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE credit_notes (
        "Id" uuid NOT NULL,
        "CreditNoteNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "SalesReturnId" uuid,
        "Reason" character varying(500) NOT NULL,
        "Total" numeric(18,2) NOT NULL,
        "TaxTreatment" integer NOT NULL,
        "GstAmount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "IssuedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "IdempotencyKey" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_credit_notes" PRIMARY KEY ("Id"),
        CONSTRAINT ck_credit_notes_currency CHECK ("Currency" = 'NZD'),
        CONSTRAINT ck_credit_notes_no_gst CHECK ("TaxTreatment" = 1 AND "GstAmount" = 0),
        CONSTRAINT ck_credit_notes_total CHECK ("Total" > 0),
        CONSTRAINT "FK_credit_notes_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_credit_notes_sales_returns_SalesReturnId" FOREIGN KEY ("SalesReturnId") REFERENCES sales_returns ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE sales_return_lines (
        "Id" uuid NOT NULL,
        "ReturnId" uuid NOT NULL,
        "OrderLineId" uuid NOT NULL,
        "ItemId" uuid NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "Disposition" integer NOT NULL,
        "UnitCost" numeric(18,2) NOT NULL,
        "MovementId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_sales_return_lines" PRIMARY KEY ("Id"),
        CONSTRAINT ck_sales_return_lines_movement CHECK (("Disposition" = 1) = ("MovementId" IS NOT NULL)),
        CONSTRAINT ck_sales_return_lines_qty CHECK ("Quantity" > 0 AND "UnitCost" >= 0),
        CONSTRAINT "FK_sales_return_lines_sales_order_lines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES sales_order_lines ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_sales_return_lines_sales_returns_ReturnId" FOREIGN KEY ("ReturnId") REFERENCES sales_returns ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_sales_return_lines_stock_movements_MovementId" FOREIGN KEY ("MovementId") REFERENCES stock_movements ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE shipment_lines (
        "Id" uuid NOT NULL,
        "ShipmentId" uuid NOT NULL,
        "OrderLineId" uuid NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_shipment_lines" PRIMARY KEY ("Id"),
        CONSTRAINT ck_shipment_lines_qty CHECK ("Quantity" > 0),
        CONSTRAINT "FK_shipment_lines_sales_order_lines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES sales_order_lines ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_shipment_lines_shipments_ShipmentId" FOREIGN KEY ("ShipmentId") REFERENCES shipments ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE credit_note_lines (
        "Id" uuid NOT NULL,
        "CreditNoteId" uuid NOT NULL,
        "OrderLineId" uuid,
        "Description" character varying(300) NOT NULL,
        "Quantity" numeric(18,3) NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_credit_note_lines" PRIMARY KEY ("Id"),
        CONSTRAINT ck_credit_note_lines_amount CHECK ("Amount" > 0 AND "Quantity" >= 0),
        CONSTRAINT "FK_credit_note_lines_credit_notes_CreditNoteId" FOREIGN KEY ("CreditNoteId") REFERENCES credit_notes ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_credit_note_lines_sales_order_lines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES sales_order_lines ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE refunds (
        "Id" uuid NOT NULL,
        "RefundNumber" character varying(20) NOT NULL,
        "OrderId" uuid NOT NULL,
        "CreditNoteId" uuid,
        "Amount" numeric(18,2) NOT NULL,
        "Method" integer NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "Status" integer NOT NULL,
        "RequestedBy" character varying(200) NOT NULL,
        "ApprovedAt" timestamp with time zone,
        "ApprovedBy" character varying(200),
        "CompletedAt" timestamp with time zone,
        "CompletedBy" character varying(200),
        "EvidenceReference" character varying(200),
        "FailureReason" character varying(500),
        "IdempotencyKey" character varying(100) NOT NULL,
        "CompletionKey" character varying(100),
        "TaxTreatment" integer NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_refunds" PRIMARY KEY ("Id"),
        CONSTRAINT ck_refunds_amount CHECK ("Amount" > 0),
        CONSTRAINT ck_refunds_evidence CHECK ("Status" <> 4 OR ("EvidenceReference" IS NOT NULL AND "CompletedAt" IS NOT NULL)),
        CONSTRAINT ck_refunds_no_gst CHECK ("TaxTreatment" = 1 AND "Currency" = 'NZD'),
        CONSTRAINT ck_refunds_not_own CHECK ("ApprovedBy" IS NULL OR "ApprovedBy" <> "RequestedBy"),
        CONSTRAINT "FK_refunds_credit_notes_CreditNoteId" FOREIGN KEY ("CreditNoteId") REFERENCES credit_notes ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_refunds_sales_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES sales_orders ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE TABLE refund_allocations (
        "Id" uuid NOT NULL,
        "RefundId" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" character varying(200) NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedBy" character varying(200) NOT NULL,
        CONSTRAINT "PK_refund_allocations" PRIMARY KEY ("Id"),
        CONSTRAINT ck_refund_allocations_amount CHECK ("Amount" > 0),
        CONSTRAINT "FK_refund_allocations_payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES payments ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_refund_allocations_refunds_RefundId" FOREIGN KEY ("RefundId") REFERENCES refunds ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_stock_issues_ShipmentId" ON stock_issues ("ShipmentId") WHERE "ShipmentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD CONSTRAINT ck_sales_orders_credit CHECK ("CreditTotal" >= 0 AND "CreditTotal" <= "Total");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    ALTER TABLE sales_orders ADD CONSTRAINT ck_sales_orders_refunded CHECK ("RefundedTotal" >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_credit_note_lines_CreditNoteId" ON credit_note_lines ("CreditNoteId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_credit_note_lines_OrderLineId" ON credit_note_lines ("OrderLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_credit_notes_CreditNoteNumber" ON credit_notes ("CreditNoteNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_credit_notes_IdempotencyKey" ON credit_notes ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_credit_notes_IssuedAt" ON credit_notes ("IssuedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_credit_notes_OrderId" ON credit_notes ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_credit_notes_SalesReturnId" ON credit_notes ("SalesReturnId") WHERE "SalesReturnId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_delivery_zone_postcodes_ZoneId_From" ON delivery_zone_postcodes ("ZoneId", "From");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_delivery_zones_Name" ON delivery_zones ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_refund_allocations_PaymentId" ON refund_allocations ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_refund_allocations_RefundId_PaymentId" ON refund_allocations ("RefundId", "PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_refunds_CompletionKey" ON refunds ("CompletionKey") WHERE "CompletionKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_refunds_CreditNoteId" ON refunds ("CreditNoteId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_refunds_IdempotencyKey" ON refunds ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_refunds_OrderId_Status" ON refunds ("OrderId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_refunds_RefundNumber" ON refunds ("RefundNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_refunds_Status_CompletedAt" ON refunds ("Status", "CompletedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_sales_return_lines_MovementId" ON sales_return_lines ("MovementId") WHERE "MovementId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_sales_return_lines_OrderLineId" ON sales_return_lines ("OrderLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_sales_return_lines_ReturnId" ON sales_return_lines ("ReturnId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_sales_returns_IdempotencyKey" ON sales_returns ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_sales_returns_OrderId" ON sales_returns ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_sales_returns_PostedAt" ON sales_returns ("PostedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_sales_returns_ReturnNumber" ON sales_returns ("ReturnNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_shipment_lines_OrderLineId" ON shipment_lines ("OrderLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_shipment_lines_ShipmentId_OrderLineId" ON shipment_lines ("ShipmentId", "OrderLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_shipments_OrderId" ON shipments ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_shipments_ShipmentNumber" ON shipments ("ShipmentNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE INDEX "IX_shipments_Status_UpdatedAt" ON shipments ("Status", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE UNIQUE INDEX "IX_shipments_StockIssueId" ON shipments ("StockIssueId") WHERE "StockIssueId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    CREATE OR REPLACE FUNCTION sales_order_guard() RETURNS trigger LANGUAGE plpgsql AS $$DECLARE
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
    END $$;

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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121120649_Phase4bAfterSale') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261121120649_Phase4bAfterSale', '10.0.15');
    END IF;
END $EF$;
COMMIT;

