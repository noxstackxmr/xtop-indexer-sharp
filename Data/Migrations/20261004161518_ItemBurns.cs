using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class ItemBurns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemBurns",
                columns: table => new
                {
                    OutputId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBurns", x => x.OutputId);
                    table.ForeignKey(
                        name: "FK_ItemBurns_CollectionOutputs_OutputId",
                        column: x => x.OutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemBurns_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_Network_IsProcessed_Height",
                table: "Blocks",
                columns: new[] { "Network", "IsProcessed", "Height" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBurns_TransactionId",
                table: "ItemBurns",
                column: "TransactionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemBurns");

            migrationBuilder.DropIndex(
                name: "IX_Blocks_Network_IsProcessed_Height",
                table: "Blocks");
        }
    }
}
