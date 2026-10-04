using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    public partial class ItemIdentities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ItemId",
                table: "CollectionOutputs",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_Network_ItemId",
                table: "CollectionOutputs",
                columns: new[] { "Network", "ItemId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_Network_ItemId",
                table: "CollectionOutputs");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "CollectionOutputs");
        }
    }
}
