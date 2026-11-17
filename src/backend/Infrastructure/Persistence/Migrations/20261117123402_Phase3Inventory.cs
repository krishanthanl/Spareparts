using System;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_counters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_counters", x => x.Id);
                    table.CheckConstraint("ck_document_counters_next", "\"NextValue\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "grns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrnNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierDocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grns", x => x.Id);
                    table.CheckConstraint("ck_grns_posted", "(\"Status\" = 1 OR \"Status\" = 4) = (\"GrnNumber\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "item_price_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CostPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_price_history", x => x.Id);
                    table.CheckConstraint("ck_item_price_history_cost", "\"CostPrice\" IS NULL OR \"CostPrice\" >= 0");
                    table.CheckConstraint("ck_item_price_history_price", "\"SellingPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_item_price_history_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "permission_seeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_seeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<int>(type: "integer", nullable: false),
                    QtyChange = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AverageCostAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostingKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("ck_stock_movements_cost", "\"UnitCost\" >= 0 AND \"AverageCostAfter\" >= 0");
                    table.CheckConstraint("ck_stock_movements_qty", "\"QtyChange\" <> 0");
                    table.ForeignKey(
                        name: "FK_stock_movements_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_movements_ReversesMovementId",
                        column: x => x.ReversesMovementId,
                        principalTable: "stock_movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocktakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grn_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrnId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grn_lines", x => x.Id);
                    table.CheckConstraint("ck_grn_lines_cost", "\"UnitCost\" >= 0");
                    table.CheckConstraint("ck_grn_lines_price", "\"SellingPrice\" IS NULL OR \"SellingPrice\" > 0");
                    table.CheckConstraint("ck_grn_lines_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_grn_lines_grns_GrnId",
                        column: x => x.GrnId,
                        principalTable: "grns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_grn_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StocktakeId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_documents", x => x.Id);
                    table.CheckConstraint("ck_stock_documents_posted", "(\"Status\" = 1 OR \"Status\" = 4) = (\"Number\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_stock_documents_stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountedQty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SystemQtyAtPost = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocktake_lines", x => x.Id);
                    table.CheckConstraint("ck_stocktake_lines_counted", "\"CountedQty\" >= 0");
                    table.ForeignKey(
                        name: "FK_stocktake_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stocktake_lines_stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    QtyChange = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_document_lines", x => x.Id);
                    table.CheckConstraint("ck_stock_document_lines_cost", "\"UnitCost\" IS NULL OR \"UnitCost\" >= 0");
                    table.CheckConstraint("ck_stock_document_lines_price", "\"SellingPrice\" IS NULL OR \"SellingPrice\" > 0");
                    table.CheckConstraint("ck_stock_document_lines_qty", "\"QtyChange\" <> 0");
                    table.ForeignKey(
                        name: "FK_stock_document_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_stock_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "stock_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_counters_DocumentType",
                table: "document_counters",
                column: "DocumentType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grn_lines_GrnId_LineNo",
                table: "grn_lines",
                columns: new[] { "GrnId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grn_lines_ItemId",
                table: "grn_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_grns_GrnNumber",
                table: "grns",
                column: "GrnNumber",
                unique: true,
                filter: "\"GrnNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_grns_Status_PostedAt",
                table: "grns",
                columns: new[] { "Status", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_grns_SupplierName",
                table: "grns",
                column: "SupplierName");

            migrationBuilder.CreateIndex(
                name: "IX_item_price_history_ItemId_Sequence",
                table: "item_price_history",
                columns: new[] { "ItemId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_item_price_history_Sequence",
                table: "item_price_history",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permission_seeds_PermissionKey",
                table: "permission_seeds",
                column: "PermissionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_DocumentId_LineNo",
                table: "stock_document_lines",
                columns: new[] { "DocumentId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_ItemId",
                table: "stock_document_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_Kind_Status_PostedAt",
                table: "stock_documents",
                columns: new[] { "Kind", "Status", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_Number",
                table: "stock_documents",
                column: "Number",
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_StocktakeId",
                table: "stock_documents",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId_OccurredAt",
                table: "stock_movements",
                columns: new[] { "ItemId", "OccurredAt" })
                .Annotation("Npgsql:IndexInclude", new[] { "QtyChange" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId_Sequence",
                table: "stock_movements",
                columns: new[] { "ItemId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_PostingKey",
                table: "stock_movements",
                column: "PostingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RefType_RefId",
                table: "stock_movements",
                columns: new[] { "RefType", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ReversesMovementId",
                table: "stock_movements",
                column: "ReversesMovementId",
                unique: true,
                filter: "\"ReversesMovementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_Sequence",
                table: "stock_movements",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_one_opening",
                table: "stock_movements",
                column: "ItemId",
                unique: true,
                filter: "\"MovementType\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_stocktake_lines_ItemId",
                table: "stocktake_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stocktake_lines_StocktakeId_ItemId",
                table: "stocktake_lines",
                columns: new[] { "StocktakeId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stocktakes_Number",
                table: "stocktakes",
                column: "Number",
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.Sql(InventorySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InventorySql.Down);

            migrationBuilder.DropTable(
                name: "document_counters");

            migrationBuilder.DropTable(
                name: "grn_lines");

            migrationBuilder.DropTable(
                name: "item_price_history");

            migrationBuilder.DropTable(
                name: "permission_seeds");

            migrationBuilder.DropTable(
                name: "stock_document_lines");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "stocktake_lines");

            migrationBuilder.DropTable(
                name: "grns");

            migrationBuilder.DropTable(
                name: "stock_documents");

            migrationBuilder.DropTable(
                name: "stocktakes");
        }
    }
}
