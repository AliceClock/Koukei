using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Koukei.Data.Migrations;

[DbContext(typeof(KoukeiDbContext))]
[Migration("20260722120000_OptimizeMediaImport")]
public partial class OptimizeMediaImport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AlbumTitle",
            table: "Items",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ArtistName",
            table: "Items",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "FileSize",
            table: "Items",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ChannelLayout",
            table: "MediaStreams",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CodecProfile",
            table: "MediaStreams",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "MediaStreams",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PixelFormat",
            table: "MediaStreams",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Rotation",
            table: "MediaStreams",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemKind_DateCreated",
            table: "Items",
            columns: new[] { "ItemKind", "DateCreated" });

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemKind_LastModified",
            table: "Items",
            columns: new[] { "ItemKind", "LastModified" });

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemKind_Name",
            table: "Items",
            columns: new[] { "ItemKind", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemKind_SortName",
            table: "Items",
            columns: new[] { "ItemKind", "SortName" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Items_ItemKind_DateCreated", table: "Items");
        migrationBuilder.DropIndex(name: "IX_Items_ItemKind_LastModified", table: "Items");
        migrationBuilder.DropIndex(name: "IX_Items_ItemKind_Name", table: "Items");
        migrationBuilder.DropIndex(name: "IX_Items_ItemKind_SortName", table: "Items");

        migrationBuilder.DropColumn(name: "AlbumTitle", table: "Items");
        migrationBuilder.DropColumn(name: "ArtistName", table: "Items");
        migrationBuilder.DropColumn(name: "FileSize", table: "Items");
        migrationBuilder.DropColumn(name: "ChannelLayout", table: "MediaStreams");
        migrationBuilder.DropColumn(name: "CodecProfile", table: "MediaStreams");
        migrationBuilder.DropColumn(name: "Language", table: "MediaStreams");
        migrationBuilder.DropColumn(name: "PixelFormat", table: "MediaStreams");
        migrationBuilder.DropColumn(name: "Rotation", table: "MediaStreams");
    }
}
