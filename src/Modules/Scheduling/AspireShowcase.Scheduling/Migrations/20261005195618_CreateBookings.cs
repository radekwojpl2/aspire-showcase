using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.Scheduling.Migrations
{
    /// <inheritdoc />
    public partial class CreateBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "Bookings",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttendeeEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    AttendeeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AttendeeUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BusinessId_Start",
                schema: "scheduling",
                table: "Bookings",
                columns: new[] { "BusinessId", "Start" });

            // No two confirmed bookings of the same staff member may overlap; different staff members
            // can. Ranges are [start, end), so back-to-back bookings are fine. btree_gist lets the
            // exclusion constraint compare the staff member's ID by equality next to the range.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE scheduling."Bookings"
                    ADD CONSTRAINT "EX_Bookings_StaffMember_NoOverlap"
                        EXCLUDE USING gist ("StaffMemberId" WITH =, tstzrange("Start", "End") WITH &&)
                        WHERE ("Status" = 'Confirmed'),
                    ADD CONSTRAINT "CK_Bookings_EndAfterStart" CHECK ("End" > "Start");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookings",
                schema: "scheduling");
        }
    }
}
