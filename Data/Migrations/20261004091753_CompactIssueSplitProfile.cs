using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndexerCore.Data.Migrations;

public partial class CompactIssueSplitProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Messages" SET "Status" = 0, "Error" = NULL
            WHERE "Version" = 14 AND "Operation" = 18 AND "Status" = 2;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
