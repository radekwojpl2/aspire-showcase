using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireShowcase.BusinessSetup.Migrations
{
    /// <inheritdoc />
    public partial class AddStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffMembers",
                schema: "business_setup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DoesAllServices = table.Column<bool>(type: "boolean", nullable: false),
                    ServiceIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    WorkingHours = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMembers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "business_setup",
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_BusinessId_Name",
                schema: "business_setup",
                table: "StaffMembers",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            // Businesses started before staff existed get their owner as a staff member, as new ones
            // do when they start: doing every service, whenever the business is open. Named "Owner"
            // until the owner renames themselves, since the business doesn't know their name.
            migrationBuilder.Sql("""
                INSERT INTO business_setup."StaffMembers"
                    ("Id", "BusinessId", "Name", "UserId", "DoesAllServices", "ServiceIds", "WorkingHours", "CreatedAt")
                SELECT gen_random_uuid(), "Id", 'Owner', "OwnerId", true, '{}', NULL, now()
                FROM business_setup."Businesses";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffMembers",
                schema: "business_setup");
        }
    }
}
