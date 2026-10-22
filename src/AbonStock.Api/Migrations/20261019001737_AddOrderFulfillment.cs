using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbonStock.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_Order_Product_Deduction",
                table: "StockMovements");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Order_Batch_Deduction",
                table: "StockMovements",
                columns: new[] { "ReferenceOrderId", "InventoryBatchId" },
                unique: true,
                filter: "\"ReferenceOrderId\" IS NOT NULL AND \"InventoryBatchId\" IS NOT NULL AND \"MovementType\" = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_Order_Batch_Deduction",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Order_Product_Deduction",
                table: "StockMovements",
                columns: new[] { "ReferenceOrderId", "ProductId" },
                unique: true,
                filter: "\"ReferenceOrderId\" IS NOT NULL AND \"MovementType\" = 3");
        }
    }
}
