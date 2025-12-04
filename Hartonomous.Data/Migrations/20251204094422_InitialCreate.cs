using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:plpython3u", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "bpe_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_id = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    constant_sequence = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    TotalSize = table.Column<int>(type: "integer", nullable: false),
                    sequence_length = table.Column<int>(type: "integer", nullable: false),
                    frequency = table.Column<long>(type: "bigint", nullable: false),
                    merge_level = table.Column<int>(type: "integer", nullable: false),
                    ParentTokenIds = table.Column<List<int>>(type: "integer[]", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    CompressionRatio = table.Column<double>(type: "double precision", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    vocabulary_rank = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bpe_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_ingestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    original_size = table.Column<long>(type: "bigint", nullable: false),
                    constant_count = table.Column<int>(type: "integer", nullable: false),
                    unique_constant_count = table.Column<int>(type: "integer", nullable: false),
                    deduplication_ratio = table.Column<double>(type: "double precision", nullable: false),
                    processing_time_ms = table.Column<long>(type: "bigint", nullable: false),
                    source_identifier = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_successful = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConstantIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_ingestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "landmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    center_x = table.Column<double>(type: "double precision", nullable: false),
                    center_y = table.Column<double>(type: "double precision", nullable: false),
                    center_z = table.Column<double>(type: "double precision", nullable: false),
                    location = table.Column<Point>(type: "geometry(PointZ)", nullable: false),
                    radius = table.Column<double>(type: "double precision", nullable: false),
                    constant_count = table.Column<long>(type: "bigint", nullable: false),
                    AverageDistance = table.Column<double>(type: "double precision", nullable: false),
                    LastStatisticsUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    density = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landmarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "constants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    size = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    projected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    coordinate_x = table.Column<double>(type: "double precision", nullable: true),
                    coordinate_y = table.Column<double>(type: "double precision", nullable: true),
                    coordinate_z = table.Column<double>(type: "double precision", nullable: true),
                    location = table.Column<Point>(type: "geometry(PointZ)", nullable: true),
                    hilbert_index = table.Column<long>(type: "bigint", nullable: true),
                    reference_count = table.Column<long>(type: "bigint", nullable: false),
                    CanonicalConstantId = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_constant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_duplicate = table.Column<bool>(type: "boolean", nullable: false),
                    deduplicated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    frequency = table.Column<long>(type: "bigint", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_accessed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    LandmarkId = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_constants_constants_canonical_constant_id",
                        column: x => x.canonical_constant_id,
                        principalTable: "constants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_constants_landmarks_LandmarkId",
                        column: x => x.LandmarkId,
                        principalTable: "landmarks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "constant_tokens",
                columns: table => new
                {
                    ComposingTokensId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstantsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constant_tokens", x => new { x.ComposingTokensId, x.ConstantsId });
                    table.ForeignKey(
                        name: "FK_constant_tokens_bpe_tokens_ComposingTokensId",
                        column: x => x.ComposingTokensId,
                        principalTable: "bpe_tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_constant_tokens_constants_ConstantsId",
                        column: x => x.ConstantsId,
                        principalTable: "constants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_frequency",
                table: "bpe_tokens",
                column: "frequency");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_hash",
                table: "bpe_tokens",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_is_active",
                table: "bpe_tokens",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_is_deleted",
                table: "bpe_tokens",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_last_used",
                table: "bpe_tokens",
                column: "last_used_at");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_merge_level",
                table: "bpe_tokens",
                column: "merge_level");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_sequence_length",
                table: "bpe_tokens",
                column: "sequence_length");

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_token_id",
                table: "bpe_tokens",
                column: "token_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_vocabulary_rank",
                table: "bpe_tokens",
                column: "vocabulary_rank");

            migrationBuilder.CreateIndex(
                name: "IX_constant_tokens_ConstantsId",
                table: "constant_tokens",
                column: "ConstantsId");

            migrationBuilder.CreateIndex(
                name: "IX_constants_canonical_constant_id",
                table: "constants",
                column: "canonical_constant_id");

            migrationBuilder.CreateIndex(
                name: "ix_constants_frequency",
                table: "constants",
                column: "frequency");

            migrationBuilder.CreateIndex(
                name: "ix_constants_hash",
                table: "constants",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_constants_hilbert_index",
                table: "constants",
                column: "hilbert_index");

            migrationBuilder.CreateIndex(
                name: "ix_constants_is_deleted",
                table: "constants",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_constants_LandmarkId",
                table: "constants",
                column: "LandmarkId");

            migrationBuilder.CreateIndex(
                name: "ix_constants_last_accessed",
                table: "constants",
                column: "last_accessed_at");

            migrationBuilder.CreateIndex(
                name: "ix_constants_location_spatial",
                table: "constants",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_constants_status",
                table: "constants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_content_hash",
                table: "content_ingestions",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_content_type",
                table: "content_ingestions",
                column: "content_type");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_is_deleted",
                table: "content_ingestions",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_is_successful",
                table: "content_ingestions",
                column: "is_successful");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_source_identifier",
                table: "content_ingestions",
                column: "source_identifier");

            migrationBuilder.CreateIndex(
                name: "ix_content_ingestions_started_at",
                table: "content_ingestions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_constant_count",
                table: "landmarks",
                column: "constant_count");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_density",
                table: "landmarks",
                column: "density");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_is_active",
                table: "landmarks",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_is_deleted",
                table: "landmarks",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_location_spatial",
                table: "landmarks",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_name",
                table: "landmarks",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_radius",
                table: "landmarks",
                column: "radius");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "constant_tokens");

            migrationBuilder.DropTable(
                name: "content_ingestions");

            migrationBuilder.DropTable(
                name: "bpe_tokens");

            migrationBuilder.DropTable(
                name: "constants");

            migrationBuilder.DropTable(
                name: "landmarks");
        }
    }
}
