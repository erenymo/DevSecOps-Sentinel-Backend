using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManyToManyComponentLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Components_Licenses_LicenseId",
                table: "Components");

            migrationBuilder.DropIndex(
                name: "IX_Components_LicenseId",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "Components");

            migrationBuilder.CreateTable(
                name: "ComponentLicense",
                columns: table => new
                {
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentLicense", x => new { x.ComponentId, x.LicenseId });
                    table.ForeignKey(
                        name: "FK_ComponentLicense_Components_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "Components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComponentLicense_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComponentLicense_LicenseId",
                table: "ComponentLicense",
                column: "LicenseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComponentLicense");

            migrationBuilder.AddColumn<Guid>(
                name: "LicenseId",
                table: "Components",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Components_LicenseId",
                table: "Components",
                column: "LicenseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Components_Licenses_LicenseId",
                table: "Components",
                column: "LicenseId",
                principalTable: "Licenses",
                principalColumn: "Id");
        }
    }
}
