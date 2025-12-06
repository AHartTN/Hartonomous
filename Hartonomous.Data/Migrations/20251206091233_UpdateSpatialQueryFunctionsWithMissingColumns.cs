using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSpatialQueryFunctionsWithMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update spatial query functions to include all missing columns
            // Find SQL file relative to solution root
            var assemblyLocation = typeof(UpdateSpatialQueryFunctionsWithMissingColumns).Assembly.Location;
            var assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation) ?? "";
            
            // Navigate up from artifacts/bin/.../Hartonomous.Data.dll to repository root
            var repositoryRoot = assemblyDir;
            while (!string.IsNullOrEmpty(repositoryRoot) && !System.IO.File.Exists(System.IO.Path.Combine(repositoryRoot, "Hartonomous.slnx")))
            {
                var parent = System.IO.Directory.GetParent(repositoryRoot)?.FullName;
                if (parent == null || parent == repositoryRoot) break;
                repositoryRoot = parent;
            }
            
            var sqlFilePath = System.IO.Path.Combine(repositoryRoot, "Hartonomous.Data", "Functions", "SQL", "SpatialQueryFunctions.sql");
            if (!System.IO.File.Exists(sqlFilePath))
            {
                throw new System.IO.FileNotFoundException($"Could not find SpatialQueryFunctions.sql. Searched at: {sqlFilePath}. Repository root: {repositoryRoot}. Assembly location: {assemblyLocation}");
            }
            
            migrationBuilder.Sql(System.IO.File.ReadAllText(sqlFilePath));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQL function updates are idempotent - no rollback needed
        }
    }
}
