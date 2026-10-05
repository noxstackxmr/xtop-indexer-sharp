using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class MarketplaceConfigurations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "MarketplaceConfigHash",
                table: "ItemTrades",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MarketplaceId",
                table: "ItemTrades",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MarketplacePolicy",
                table: "ItemTrades",
                type: "bytea",
                maxLength: 291,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MarketplaceId",
                table: "Collections",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MarketplacePolicy",
                table: "Collections",
                type: "bytea",
                maxLength: 234,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Marketplaces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    ProtocolId = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ManagementKey = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    RegistrationMessageId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marketplaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Marketplaces_Messages_RegistrationMessageId",
                        column: x => x.RegistrationMessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceRevisions",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    MarketplaceId = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ConfigHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    PreviousMessageId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommunicationUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ApiVersion = table.Column<int>(type: "integer", nullable: false),
                    Modes = table.Column<byte>(type: "smallint", nullable: false),
                    CreationFee = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    PrimaryFeeBps = table.Column<int>(type: "integer", nullable: false),
                    SecondaryFeeBps = table.Column<int>(type: "integer", nullable: false),
                    FeeAddress = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false),
                    CustodyAddress = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceRevisions", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_MarketplaceRevisions_MarketplaceRevisions_PreviousMessageId",
                        column: x => x.PreviousMessageId,
                        principalTable: "MarketplaceRevisions",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketplaceRevisions_Marketplaces_MarketplaceId",
                        column: x => x.MarketplaceId,
                        principalTable: "Marketplaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketplaceRevisions_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRevisions_ConfigHash",
                table: "MarketplaceRevisions",
                column: "ConfigHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRevisions_MarketplaceId_Revision",
                table: "MarketplaceRevisions",
                columns: new[] { "MarketplaceId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceRevisions_PreviousMessageId",
                table: "MarketplaceRevisions",
                column: "PreviousMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Marketplaces_Network_ProtocolId",
                table: "Marketplaces",
                columns: new[] { "Network", "ProtocolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Marketplaces_RegistrationMessageId",
                table: "Marketplaces",
                column: "RegistrationMessageId");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceRevisions");

            migrationBuilder.DropTable(
                name: "Marketplaces");

            migrationBuilder.DropColumn(
                name: "MarketplaceConfigHash",
                table: "ItemTrades");

            migrationBuilder.DropColumn(
                name: "MarketplaceId",
                table: "ItemTrades");

            migrationBuilder.DropColumn(
                name: "MarketplacePolicy",
                table: "ItemTrades");

            migrationBuilder.DropColumn(
                name: "MarketplaceId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "MarketplacePolicy",
                table: "Collections");
        }
    }
}
