using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class IssuanceSplits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_CollectionId_Kind",
                table: "CollectionOutputs");

            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_CollectionId_OutputIndex",
                table: "CollectionOutputs");

            migrationBuilder.AddColumn<long>(
                name: "SourceMessageId",
                table: "CollectionOutputs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                UPDATE "CollectionOutputs" AS o
                SET "SourceMessageId" = c."CreationMessageId"
                FROM "Collections" AS c WHERE c."Id" = o."CollectionId";
                """);

            migrationBuilder.CreateTable(
                name: "IssuanceSplits",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    ParentOutputId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuanceSplits", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_IssuanceSplits_CollectionOutputs_ParentOutputId",
                        column: x => x.ParentOutputId,
                        principalTable: "CollectionOutputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssuanceSplits_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_CollectionId_Kind",
                table: "CollectionOutputs",
                columns: new[] { "CollectionId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_SourceMessageId_OutputIndex",
                table: "CollectionOutputs",
                columns: new[] { "SourceMessageId", "OutputIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuanceSplits_ParentOutputId",
                table: "IssuanceSplits",
                column: "ParentOutputId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionOutputs_Messages_SourceMessageId",
                table: "CollectionOutputs",
                column: "SourceMessageId",
                principalTable: "Messages",
                principalColumn: "TransactionId",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionOutputs_Messages_SourceMessageId",
                table: "CollectionOutputs");

            migrationBuilder.DropTable(
                name: "IssuanceSplits");

            migrationBuilder.Sql("""
                DELETE FROM "CollectionOutputs" AS o
                USING "Collections" AS c
                WHERE c."Id" = o."CollectionId" AND o."SourceMessageId" <> c."CreationMessageId";
                UPDATE "Messages" SET "Status" = 4 WHERE "Operation" = 18 AND "Status" = 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_CollectionId_Kind",
                table: "CollectionOutputs");

            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_SourceMessageId_OutputIndex",
                table: "CollectionOutputs");

            migrationBuilder.DropColumn(
                name: "SourceMessageId",
                table: "CollectionOutputs");

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
        }
    }
}
