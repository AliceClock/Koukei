using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Koukei.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistsAndUserMediaState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DateCreated = table.Column<long>(type: "INTEGER", nullable: false),
                    DateLastSaved = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserMediaStates",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserRating = table.Column<int>(type: "INTEGER", nullable: true),
                    PlaybackPositionTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPlayedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastOpenedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DateModified = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMediaStates", x => x.ItemId);
                    table.CheckConstraint("CK_UserMediaStates_PlayCount", "\"PlayCount\" >= 0");
                    table.CheckConstraint("CK_UserMediaStates_UserRating", "\"UserRating\" IS NULL OR (\"UserRating\" >= 1 AND \"UserRating\" <= 5)");
                    table.ForeignKey(
                        name: "FK_UserMediaStates_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    DateAdded = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_ItemId",
                table: "PlaylistItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_ItemId",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistId_SortOrder",
                table: "PlaylistItems",
                columns: new[] { "PlaylistId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_Name",
                table: "Playlists",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_SortName",
                table: "Playlists",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_UserMediaStates_IsFavorite",
                table: "UserMediaStates",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_UserMediaStates_LastPlayedAt",
                table: "UserMediaStates",
                column: "LastPlayedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "UserMediaStates");

            migrationBuilder.DropTable(
                name: "Playlists");
        }
    }
}
