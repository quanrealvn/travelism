using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaceDescriptionAndReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Places",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaceReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceReferences_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReferences_PlaceId_SortOrder",
                table: "PlaceReferences",
                columns: new[] { "PlaceId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceReferences");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Places");
        }
    }
}
