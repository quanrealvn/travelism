using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SummaryText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    At = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TravelTimeCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromPlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToPlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Meters = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FetchedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelTimeCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    BudgetAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InviteCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Places",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Lat = table.Column<double>(type: "REAL", nullable: false),
                    Lng = table.Column<double>(type: "REAL", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TimeSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCost = table.Column<long>(type: "INTEGER", nullable: true),
                    OpenHoursText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SkipReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Places_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActualCost = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false),
                    UpdatedByMemberId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlaceLikes",
                columns: table => new
                {
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", maxLength: 33, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceLikes", x => new { x.PlaceId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_PlaceLikes_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceLikes_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_TripId_At",
                table: "ActivityLogs",
                columns: new[] { "TripId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_PlaceId",
                table: "ItineraryItems",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripId_Date",
                table: "ItineraryItems",
                columns: new[] { "TripId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripId_PlaceId_Date",
                table: "ItineraryItems",
                columns: new[] { "TripId", "PlaceId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_TripId",
                table: "Members",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Members_TripId_DisplayName",
                table: "Members",
                columns: new[] { "TripId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceLikes_MemberId",
                table: "PlaceLikes",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_TripId_IsDeleted",
                table: "Places",
                columns: new[] { "TripId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelTimeCaches_FromPlaceId",
                table: "TravelTimeCaches",
                column: "FromPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelTimeCaches_FromPlaceId_ToPlaceId_Mode",
                table: "TravelTimeCaches",
                columns: new[] { "FromPlaceId", "ToPlaceId", "Mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelTimeCaches_ToPlaceId",
                table: "TravelTimeCaches",
                column: "ToPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelTimeCaches_TripId",
                table: "TravelTimeCaches",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_InviteCode",
                table: "Trips",
                column: "InviteCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "ItineraryItems");

            migrationBuilder.DropTable(
                name: "PlaceLikes");

            migrationBuilder.DropTable(
                name: "TravelTimeCaches");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Places");

            migrationBuilder.DropTable(
                name: "Trips");
        }
    }
}
