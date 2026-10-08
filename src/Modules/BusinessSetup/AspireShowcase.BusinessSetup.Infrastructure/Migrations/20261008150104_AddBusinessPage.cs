using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.BusinessSetup.Migrations
{
    /// <inheritdoc />
    internal partial class AddBusinessPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "business_setup",
                table: "Businesses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "business_setup",
                table: "Businesses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoVersion",
                schema: "business_setup",
                table: "Businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessLogos",
                schema: "business_setup",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Image = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLogos", x => x.BusinessId);
                    table.ForeignKey(
                        name: "FK_BusinessLogos_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "business_setup",
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessLogos",
                schema: "business_setup");

            migrationBuilder.DropColumn(
                name: "Address",
                schema: "business_setup",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "business_setup",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LogoVersion",
                schema: "business_setup",
                table: "Businesses");
        }
    }
}
