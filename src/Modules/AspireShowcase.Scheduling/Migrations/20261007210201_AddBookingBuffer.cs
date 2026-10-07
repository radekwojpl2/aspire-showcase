using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.Scheduling.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccupiedUntil",
                schema: "scheduling",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Bookings made before buffers had none. The no-overlap constraint now covers the buffer
            // too (user story V1-2), so a booking can't start while the staff member cleans up.
            migrationBuilder.Sql("""
                UPDATE scheduling."Bookings" SET "OccupiedUntil" = "End";

                ALTER TABLE scheduling."Bookings"
                    DROP CONSTRAINT "EX_Bookings_StaffMember_NoOverlap",
                    ADD CONSTRAINT "EX_Bookings_StaffMember_NoOverlap"
                        EXCLUDE USING gist ("StaffMemberId" WITH =, tstzrange("Start", "OccupiedUntil") WITH &&)
                        WHERE ("Status" = 'Confirmed'),
                    ADD CONSTRAINT "CK_Bookings_OccupiedUntilEnd" CHECK ("OccupiedUntil" >= "End");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE scheduling."Bookings"
                    DROP CONSTRAINT "CK_Bookings_OccupiedUntilEnd",
                    DROP CONSTRAINT "EX_Bookings_StaffMember_NoOverlap",
                    ADD CONSTRAINT "EX_Bookings_StaffMember_NoOverlap"
                        EXCLUDE USING gist ("StaffMemberId" WITH =, tstzrange("Start", "End") WITH &&)
                        WHERE ("Status" = 'Confirmed');
                """);

            migrationBuilder.DropColumn(
                name: "OccupiedUntil",
                schema: "scheduling",
                table: "Bookings");
        }
    }
}
