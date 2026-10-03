using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class CollectionCreation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    MerkleRoot = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    TotalLength = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<byte>(type: "smallint", nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    Height = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    PreviousHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsProcessed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BlockId = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    NativeData = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte>(type: "smallint", nullable: false),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    Operation = table.Column<byte>(type: "smallint", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_Messages_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    ProtocolId = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ConfigHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    TermsAttachmentId = table.Column<long>(type: "bigint", nullable: false),
                    LocationsAttachmentId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxSupply = table.Column<long>(type: "bigint", nullable: false),
                    MetadataMode = table.Column<byte>(type: "smallint", nullable: false),
                    ManagerPermissions = table.Column<byte>(type: "smallint", nullable: false),
                    PrimaryPayout = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false),
                    RoyaltyPayout = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false),
                    RoyaltyBps = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPrice = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    SaleStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_Attachments_LocationsAttachmentId",
                        column: x => x.LocationsAttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_Attachments_TermsAttachmentId",
                        column: x => x.TermsAttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Collections_Messages_CreationMessageId",
                        column: x => x.CreationMessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataChunks",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    ConfigHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    AttachmentId = table.Column<long>(type: "bigint", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataChunks", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_DataChunks_Attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DataChunks_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionOutputs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CollectionId = table.Column<long>(type: "bigint", nullable: false),
                    Network = table.Column<byte>(type: "smallint", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    OutputIndex = table.Column<byte>(type: "smallint", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    KeyImage = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    OwnerKey = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    NominalAmount = table.Column<decimal>(type: "numeric(20,0)", precision: 20, scale: 0, nullable: false),
                    RangeStart = table.Column<long>(type: "bigint", nullable: true),
                    RangeEnd = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionOutputs_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Network_Hash_TotalLength_MerkleRoot",
                table: "Attachments",
                columns: new[] { "Network", "Hash", "TotalLength", "MerkleRoot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_Network_Hash",
                table: "Blocks",
                columns: new[] { "Network", "Hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_Network_Height",
                table: "Blocks",
                columns: new[] { "Network", "Height" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_CollectionId_Kind",
                table: "CollectionOutputs",
                columns: new[] { "CollectionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_CollectionId_OutputIndex",
                table: "CollectionOutputs",
                columns: new[] { "CollectionId", "OutputIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_Network_KeyImage",
                table: "CollectionOutputs",
                columns: new[] { "Network", "KeyImage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_CreationMessageId",
                table: "Collections",
                column: "CreationMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_LocationsAttachmentId",
                table: "Collections",
                column: "LocationsAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Network_ProtocolId",
                table: "Collections",
                columns: new[] { "Network", "ProtocolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TermsAttachmentId",
                table: "Collections",
                column: "TermsAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DataChunks_AttachmentId_Count_Index",
                table: "DataChunks",
                columns: new[] { "AttachmentId", "Count", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BlockId_Position",
                table: "Transactions",
                columns: new[] { "BlockId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Hash",
                table: "Transactions",
                column: "Hash");
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionOutputs");

            migrationBuilder.DropTable(
                name: "DataChunks");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Blocks");
        }
    }
}
