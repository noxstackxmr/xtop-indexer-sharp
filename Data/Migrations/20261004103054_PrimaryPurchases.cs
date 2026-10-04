using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class PrimaryPurchases : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrimaryPurchases",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    CollectionId = table.Column<long>(type: "bigint", nullable: false),
                    Profile = table.Column<int>(type: "integer", nullable: false),
                    FeeBps = table.Column<int>(type: "integer", nullable: false),
                    CreatorAmount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrimaryPurchases", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_PrimaryPurchases_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrimaryPurchases_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrimaryPurchaseItems",
                columns: table => new
                {
                    CollectionId = table.Column<long>(type: "bigint", nullable: false),
                    Serial = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    PurchaseMessageId = table.Column<long>(type: "bigint", nullable: false),
                    PreviousOutputId = table.Column<long>(type: "bigint", nullable: false),
                    BuyerOutputId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrimaryPurchaseItems", x => new { x.CollectionId, x.Serial });
                    table.ForeignKey(
                        name: "FK_PrimaryPurchaseItems_CollectionOutputs_BuyerOutputId",
                        column: x => x.BuyerOutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrimaryPurchaseItems_CollectionOutputs_PreviousOutputId",
                        column: x => x.PreviousOutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrimaryPurchaseItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrimaryPurchaseItems_PrimaryPurchases_PurchaseMessageId",
                        column: x => x.PurchaseMessageId,
                        principalTable: "PrimaryPurchases",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryPurchaseItems_BuyerOutputId",
                table: "PrimaryPurchaseItems",
                column: "BuyerOutputId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryPurchaseItems_CollectionId_ItemId",
                table: "PrimaryPurchaseItems",
                columns: new[] { "CollectionId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryPurchaseItems_PreviousOutputId",
                table: "PrimaryPurchaseItems",
                column: "PreviousOutputId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryPurchaseItems_PurchaseMessageId",
                table: "PrimaryPurchaseItems",
                column: "PurchaseMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryPurchases_CollectionId",
                table: "PrimaryPurchases",
                column: "CollectionId");
            migrationBuilder.Sql("""
                UPDATE "Messages" SET "Status" = 0, "Error" = NULL
                WHERE "Version" = 14 AND "Operation" IN (9, 19) AND "Status" IN (2, 3, 4);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrimaryPurchaseItems");

            migrationBuilder.DropTable(
                name: "PrimaryPurchases");
        }
    }
}
