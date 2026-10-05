using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Businesses started before this are closed every day, in UTC, until the owner sets their hours.
            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "Businesses",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Businesses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Businesses");
        }
    }
}
