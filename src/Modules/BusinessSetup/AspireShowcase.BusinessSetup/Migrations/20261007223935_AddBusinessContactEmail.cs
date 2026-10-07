using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.BusinessSetup.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessContactEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "business_setup",
                table: "Businesses",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "business_setup",
                table: "Businesses");
        }
    }
}
