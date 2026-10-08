using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.Scheduling.Migrations
{
    /// <inheritdoc />
    internal partial class AddTimeOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimeOff",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOff", x => x.Id);
                    table.CheckConstraint("CK_TimeOff_EndAfterStart", "\"End\" > \"Start\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeOff_BusinessId_End",
                schema: "scheduling",
                table: "TimeOff",
                columns: new[] { "BusinessId", "End" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeOff",
                schema: "scheduling");
        }
    }
}
