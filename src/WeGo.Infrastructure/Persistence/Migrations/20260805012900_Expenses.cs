using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Expenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaidByMemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SplitType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseShares",
                columns: table => new
                {
                    ExpenseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareAmount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseShares", x => new { x.ExpenseId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_ExpenseShares_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TripId_Date",
                table: "Expenses",
                columns: new[] { "TripId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseShares_MemberId",
                table: "ExpenseShares",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseShares");

            migrationBuilder.DropTable(
                name: "Expenses");
        }
    }
}
