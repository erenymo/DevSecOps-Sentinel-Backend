using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateComponentWithParentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependencyPath",
                table: "Components",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentName",
                table: "Components",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependencyPath",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "ParentName",
                table: "Components");
        }
    }
}
