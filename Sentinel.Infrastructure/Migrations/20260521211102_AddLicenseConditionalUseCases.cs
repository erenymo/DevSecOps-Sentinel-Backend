using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseConditionalUseCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProblematicUseCasesJson",
                table: "PackageLicenseInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SafeUseCasesJson",
                table: "PackageLicenseInsights",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProblematicUseCasesJson",
                table: "PackageLicenseInsights");

            migrationBuilder.DropColumn(
                name: "SafeUseCasesJson",
                table: "PackageLicenseInsights");
        }
    }
}
