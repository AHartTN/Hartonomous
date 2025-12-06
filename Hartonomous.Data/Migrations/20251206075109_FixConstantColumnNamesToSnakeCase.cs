using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixConstantColumnNamesToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LandmarkId",
                table: "constants",
                newName: "landmark_id");

            migrationBuilder.RenameColumn(
                name: "FirstSeenAt",
                table: "constants",
                newName: "first_seen_at");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "constants",
                newName: "error_message");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "landmark_id",
                table: "constants",
                newName: "LandmarkId");

            migrationBuilder.RenameColumn(
                name: "first_seen_at",
                table: "constants",
                newName: "FirstSeenAt");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "constants",
                newName: "ErrorMessage");
        }
    }
}
