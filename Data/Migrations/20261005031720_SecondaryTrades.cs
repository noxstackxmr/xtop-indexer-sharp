using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecondaryTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemTrades",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Operation = table.Column<byte>(type: "smallint", nullable: false),
                    PreviousOutputId = table.Column<long>(type: "bigint", nullable: false),
                    SuccessorOutputId = table.Column<long>(type: "bigint", nullable: false),
                    ListingId = table.Column<long>(type: "bigint", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                    SellerPayout = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: true),
                    ReturnAddress = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: true),
                    ServiceAddress = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: true),
                    FeeBps = table.Column<int>(type: "integer", nullable: true),
                    SellerAmount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                    RoyaltyAmount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true),
                    PlatformFee = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTrades", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_ItemTrades_CollectionOutputs_PreviousOutputId",
                        column: x => x.PreviousOutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTrades_CollectionOutputs_SuccessorOutputId",
                        column: x => x.SuccessorOutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTrades_ItemTrades_ListingId",
                        column: x => x.ListingId,
                        principalTable: "ItemTrades",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTrades_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemTrades_ListingId",
                table: "ItemTrades",
                column: "ListingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTrades_PreviousOutputId",
                table: "ItemTrades",
                column: "PreviousOutputId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTrades_SuccessorOutputId",
                table: "ItemTrades",
                column: "SuccessorOutputId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemTrades");
        }
    }
}
