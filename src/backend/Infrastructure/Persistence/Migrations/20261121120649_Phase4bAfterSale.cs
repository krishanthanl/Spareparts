using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4bAfterSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentId",
                table: "stock_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditTotal",
                table: "sales_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryChargeReason",
                table: "sales_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveryIsRural",
                table: "sales_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPostcode",
                table: "sales_orders",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPromise",
                table: "sales_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryZoneName",
                table: "sales_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedTotal",
                table: "sales_orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "delivery_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PickupEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FreeDeliveryThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_settings", x => x.Id);
                    table.CheckConstraint("ck_delivery_settings_money", "\"FreeDeliveryThreshold\" IS NULL OR \"FreeDeliveryThreshold\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FreeDeliveryThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RuralSurcharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ServicePromise = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.Id);
                    table.CheckConstraint("ck_delivery_zones_money", "\"Charge\" >= 0 AND \"RuralSurcharge\" >= 0 AND (\"FreeDeliveryThreshold\" IS NULL OR \"FreeDeliveryThreshold\" >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "sales_returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_returns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_returns_sales_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "sales_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StockIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipments_sales_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "sales_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipments_stock_issues_StockIssueId",
                        column: x => x.StockIssueId,
                        principalTable: "stock_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zone_postcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<int>(type: "integer", nullable: false),
                    To = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zone_postcodes", x => x.Id);
                    table.CheckConstraint("ck_delivery_zone_postcodes_range", "\"From\" BETWEEN 0 AND 9999 AND \"To\" BETWEEN \"From\" AND 9999");
                    table.ForeignKey(
                        name: "FK_delivery_zone_postcodes_delivery_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "delivery_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTreatment = table.Column<int>(type: "integer", nullable: false),
                    GstAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.Id);
                    table.CheckConstraint("ck_credit_notes_currency", "\"Currency\" = 'NZD'");
                    table.CheckConstraint("ck_credit_notes_no_gst", "\"TaxTreatment\" = 1 AND \"GstAmount\" = 0");
                    table.CheckConstraint("ck_credit_notes_total", "\"Total\" > 0");
                    table.ForeignKey(
                        name: "FK_credit_notes_sales_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "sales_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_notes_sales_returns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "sales_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Disposition = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_lines", x => x.Id);
                    table.CheckConstraint("ck_sales_return_lines_movement", "(\"Disposition\" = 1) = (\"MovementId\" IS NOT NULL)");
                    table.CheckConstraint("ck_sales_return_lines_qty", "\"Quantity\" > 0 AND \"UnitCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_sales_return_lines_sales_order_lines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "sales_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_return_lines_sales_returns_ReturnId",
                        column: x => x.ReturnId,
                        principalTable: "sales_returns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_return_lines_stock_movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "stock_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_lines", x => x.Id);
                    table.CheckConstraint("ck_shipment_lines_qty", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_shipment_lines_sales_order_lines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "sales_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_lines_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_note_lines", x => x.Id);
                    table.CheckConstraint("ck_credit_note_lines_amount", "\"Amount\" > 0 AND \"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_credit_note_lines_credit_notes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "credit_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_note_lines_sales_order_lines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "sales_order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CompletionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxTreatment = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.CheckConstraint("ck_refunds_amount", "\"Amount\" > 0");
                    table.CheckConstraint("ck_refunds_evidence", "\"Status\" <> 4 OR (\"EvidenceReference\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("ck_refunds_no_gst", "\"TaxTreatment\" = 1 AND \"Currency\" = 'NZD'");
                    table.CheckConstraint("ck_refunds_not_own", "\"ApprovedBy\" IS NULL OR \"ApprovedBy\" <> \"RequestedBy\"");
                    table.ForeignKey(
                        name: "FK_refunds_credit_notes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "credit_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_sales_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "sales_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refund_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_allocations", x => x.Id);
                    table.CheckConstraint("ck_refund_allocations_amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_refund_allocations_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refund_allocations_refunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_issues_ShipmentId",
                table: "stock_issues",
                column: "ShipmentId",
                unique: true,
                filter: "\"ShipmentId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_orders_credit",
                table: "sales_orders",
                sql: "\"CreditTotal\" >= 0 AND \"CreditTotal\" <= \"Total\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_orders_refunded",
                table: "sales_orders",
                sql: "\"RefundedTotal\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_lines_CreditNoteId",
                table: "credit_note_lines",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_lines_OrderLineId",
                table: "credit_note_lines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_CreditNoteNumber",
                table: "credit_notes",
                column: "CreditNoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_IdempotencyKey",
                table: "credit_notes",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_IssuedAt",
                table: "credit_notes",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_OrderId",
                table: "credit_notes",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_SalesReturnId",
                table: "credit_notes",
                column: "SalesReturnId",
                unique: true,
                filter: "\"SalesReturnId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zone_postcodes_ZoneId_From",
                table: "delivery_zone_postcodes",
                columns: new[] { "ZoneId", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zones_Name",
                table: "delivery_zones",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refund_allocations_PaymentId",
                table: "refund_allocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_refund_allocations_RefundId_PaymentId",
                table: "refund_allocations",
                columns: new[] { "RefundId", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refunds_CompletionKey",
                table: "refunds",
                column: "CompletionKey",
                unique: true,
                filter: "\"CompletionKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_CreditNoteId",
                table: "refunds",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_IdempotencyKey",
                table: "refunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refunds_OrderId_Status",
                table: "refunds",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_RefundNumber",
                table: "refunds",
                column: "RefundNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refunds_Status_CompletedAt",
                table: "refunds",
                columns: new[] { "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_MovementId",
                table: "sales_return_lines",
                column: "MovementId",
                unique: true,
                filter: "\"MovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_OrderLineId",
                table: "sales_return_lines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_ReturnId",
                table: "sales_return_lines",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_IdempotencyKey",
                table: "sales_returns",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_OrderId",
                table: "sales_returns",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_PostedAt",
                table: "sales_returns",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_ReturnNumber",
                table: "sales_returns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_OrderLineId",
                table: "shipment_lines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_ShipmentId_OrderLineId",
                table: "shipment_lines",
                columns: new[] { "ShipmentId", "OrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OrderId",
                table: "shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_ShipmentNumber",
                table: "shipments",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status_UpdatedAt",
                table: "shipments",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_StockIssueId",
                table: "shipments",
                column: "StockIssueId",
                unique: true,
                filter: "\"StockIssueId\" IS NOT NULL");

            migrationBuilder.Sql(AfterSaleSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AfterSaleSql.Down);

            migrationBuilder.DropTable(
                name: "credit_note_lines");

            migrationBuilder.DropTable(
                name: "delivery_settings");

            migrationBuilder.DropTable(
                name: "delivery_zone_postcodes");

            migrationBuilder.DropTable(
                name: "refund_allocations");

            migrationBuilder.DropTable(
                name: "sales_return_lines");

            migrationBuilder.DropTable(
                name: "shipment_lines");

            migrationBuilder.DropTable(
                name: "delivery_zones");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.DropTable(
                name: "credit_notes");

            migrationBuilder.DropTable(
                name: "sales_returns");

            migrationBuilder.DropIndex(
                name: "IX_stock_issues_ShipmentId",
                table: "stock_issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_orders_credit",
                table: "sales_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_orders_refunded",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "stock_issues");

            migrationBuilder.DropColumn(
                name: "CreditTotal",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeReason",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryIsRural",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryPostcode",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryPromise",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "DeliveryZoneName",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "RefundedTotal",
                table: "sales_orders");
        }
    }
}
