using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class CollectionControl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionChanges",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    CollectionId = table.Column<long>(type: "bigint", nullable: false),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    Operation = table.Column<byte>(type: "smallint", nullable: false),
                    PreviousKeyImage = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    OutputIndex = table.Column<byte>(type: "smallint", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    KeyImage = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    OwnerKey = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    NominalAmount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    LocationsAttachmentId = table.Column<long>(type: "bigint", nullable: true),
                    Revealed = table.Column<bool>(type: "boolean", nullable: false),
                    CollectionMetadataUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ItemsMetadataUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PlaceholderUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionChanges", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_CollectionChanges_Attachments_LocationsAttachmentId",
                        column: x => x.LocationsAttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CollectionChanges_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionChanges_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionChanges_CollectionId",
                table: "CollectionChanges",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionChanges_LocationsAttachmentId",
                table: "CollectionChanges",
                column: "LocationsAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionChanges_Network_KeyImage",
                table: "CollectionChanges",
                columns: new[] { "Network", "KeyImage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionChanges_Network_PreviousKeyImage",
                table: "CollectionChanges",
                columns: new[] { "Network", "PreviousKeyImage" },
                unique: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionChanges");
        }
    }
}
