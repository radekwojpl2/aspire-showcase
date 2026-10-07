using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.Scheduling.Migrations
{
    /// <inheritdoc />
    internal partial class AddCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "scheduling",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            // "My bookings" (MVP-7) looks bookings up by the client's user ID, across businesses. The
            // attendee is a complex property, which EF Core can't index, so the index is plain SQL.
            migrationBuilder.Sql("CREATE INDEX \"IX_Bookings_AttendeeUserId\" ON scheduling.\"Bookings\" (\"AttendeeUserId\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX scheduling.\"IX_Bookings_AttendeeUserId\";");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "scheduling",
                table: "Bookings");
        }
    }
}
