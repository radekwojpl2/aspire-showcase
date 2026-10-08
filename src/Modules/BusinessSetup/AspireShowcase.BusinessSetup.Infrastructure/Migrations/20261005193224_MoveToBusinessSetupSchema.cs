using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.BusinessSetup.Migrations
{
    /// <inheritdoc />
    internal partial class MoveToBusinessSetupSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "business_setup");

            migrationBuilder.RenameTable(
                name: "Businesses",
                newName: "Businesses",
                newSchema: "business_setup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Businesses",
                schema: "business_setup",
                newName: "Businesses");
        }
    }
}
