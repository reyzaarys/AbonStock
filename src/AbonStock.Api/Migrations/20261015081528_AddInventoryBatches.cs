using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AbonStock.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchBalanceAfter",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryBatchId",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    BatchNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LocationId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "LOC_KDR_PROD"),
                    InitialQuantity = table.Column<int>(type: "integer", nullable: false),
                    PhysicalQuantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBatches", x => x.Id);
                    table.CheckConstraint("CK_InventoryBatches_InitialQuantity_Positive", "\"InitialQuantity\" > 0");
                    table.CheckConstraint("CK_InventoryBatches_PhysicalQuantity_NonNegative", "\"PhysicalQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_InventoryBatchId",
                table: "StockMovements",
                column: "InventoryBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBatches_ProductId_BatchNo",
                table: "InventoryBatches",
                columns: new[] { "ProductId", "BatchNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_InventoryBatches_InventoryBatchId",
                table: "StockMovements",
                column: "InventoryBatchId",
                principalTable: "InventoryBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_InventoryBatches_InventoryBatchId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "InventoryBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_InventoryBatchId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BatchBalanceAfter",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "InventoryBatchId",
                table: "StockMovements");
        }
    }
}
