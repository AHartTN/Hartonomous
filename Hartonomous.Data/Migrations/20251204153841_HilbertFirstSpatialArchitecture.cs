using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class HilbertFirstSpatialArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_constants_hilbert_index",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "indexed_at",
                table: "constants");

            migrationBuilder.AddColumn<long>(
                name: "center_hilbert_index",
                table: "landmarks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "center_hilbert_precision",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 21);

            migrationBuilder.AddColumn<int>(
                name: "hilbert_precision",
                table: "constants",
                type: "integer",
                nullable: true,
                defaultValue: 21);

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks",
                column: "center_hilbert_index")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_hilbert_index",
                table: "constants",
                column: "hilbert_index")
                .Annotation("Npgsql:IndexMethod", "btree");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "ix_constants_hilbert_index",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "center_hilbert_index",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_hilbert_precision",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "hilbert_precision",
                table: "constants");

            migrationBuilder.AddColumn<DateTime>(
                name: "indexed_at",
                table: "constants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_constants_hilbert_index",
                table: "constants",
                column: "hilbert_index");
        }
    }
}
