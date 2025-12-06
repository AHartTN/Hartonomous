using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixColumnNamesToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "constants",
                newName: "error_message");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                table: "constants",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "constants",
                newName: "ErrorMessage");

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "constants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
