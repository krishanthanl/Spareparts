using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6CheckoutReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckoutHoldUntil",
                table: "sales_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutKeyHash",
                table: "sales_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutRequestHash",
                table: "sales_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_orders_CheckoutHoldUntil",
                table: "sales_orders",
                column: "CheckoutHoldUntil",
                filter: "\"Status\" = 1 AND \"CheckoutHoldUntil\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_orders_CheckoutKeyHash",
                table: "sales_orders",
                column: "CheckoutKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_orders_CheckoutHoldUntil",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "IX_sales_orders_CheckoutKeyHash",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "CheckoutHoldUntil",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "CheckoutKeyHash",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "CheckoutRequestHash",
                table: "sales_orders");
        }
    }
}
