using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemOwnerLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CollectionOutputs_Network_OwnerKey_CollectionId",
                table: "CollectionOutputs",
                columns: new[] { "Network", "OwnerKey", "CollectionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CollectionOutputs_Network_OwnerKey_CollectionId",
                table: "CollectionOutputs");
        }
    }
}
