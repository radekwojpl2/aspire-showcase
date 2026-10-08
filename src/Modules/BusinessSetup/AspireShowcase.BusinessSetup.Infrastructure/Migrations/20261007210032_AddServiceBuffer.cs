using System;
using AspireShowcase.BusinessSetup.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.BusinessSetup.Migrations
{
    /// <inheritdoc />
    internal partial class AddServiceBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "Buffer",
                schema: "business_setup",
                table: "Services",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Buffer",
                schema: "business_setup",
                table: "Services");
        }
    }
}
