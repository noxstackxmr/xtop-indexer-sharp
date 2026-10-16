using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseMintPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "PrimaryPurchases",
                type: "numeric(20,0)",
                precision: 20,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EarlyAccessPrice",
                table: "Collections",
                type: "numeric(20,0)",
                precision: 20,
                scale: 0,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "PrimaryPurchases");

            migrationBuilder.DropColumn(
                name: "EarlyAccessPrice",
                table: "Collections");
        }
    }
}
