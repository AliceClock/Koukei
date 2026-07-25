using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Koukei.Data.Migrations;

[DbContext(typeof(KoukeiDbContext))]
[Migration("20260730120000_AddLinkedMediaFile")]
public partial class AddLinkedMediaFile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LinkedFilePath",
            table: "Items",
            type: "TEXT",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LinkedFilePath",
            table: "Items");
    }
}
