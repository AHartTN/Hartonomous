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
                    composition_geometry = table.Column<LineString>(type: "geometry(LineStringZM, 4326)", nullable: true),
                    path_length = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: true),
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
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    hilbert_prefix_high = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    hilbert_prefix_low = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    constant_count = table.Column<long>(type: "bigint", nullable: false),
                    density = table.Column<double>(type: "double precision", nullable: false),
                    last_statistics_update = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "neural_network_layers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer_index = table.Column<int>(type: "integer", nullable: false),
                    layer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    layer_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Dense"),
                    weight_geometry = table.Column<MultiLineString>(type: "geometry(MultiLineStringZM, 4326)", nullable: false),
                    neuron_count = table.Column<int>(type: "integer", nullable: false),
                    input_dim = table.Column<int>(type: "integer", nullable: false),
                    parameter_count = table.Column<long>(type: "bigint", nullable: false),
                    bias_geometry = table.Column<LineString>(type: "geometry(LineStringZM, 4326)", nullable: true),
                    activation_function = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    initialization_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    weight_norm = table.Column<double>(type: "double precision", precision: 18, scale: 10, nullable: false),
                    is_frozen = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    epoch = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neural_network_layers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_boundaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_ingestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boundary_geometry = table.Column<Polygon>(type: "geometry(PolygonZM, 4326)", nullable: false),
                    min_x = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_x = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_y = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_y = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_z = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_z = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_m = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_m = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    boundary_area = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    boundary_perimeter = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    atom_count = table.Column<int>(type: "integer", nullable: false),
                    density = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    centroid = table.Column<Point>(type: "geometry(PointZM, 4326)", nullable: false),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    computation_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "ConvexHull"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_boundaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_boundaries_content_ingestions_content_ingestion_id",
                        column: x => x.content_ingestion_id,
                        principalTable: "content_ingestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hierarchical_content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_ingestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complete_geometry = table.Column<GeometryCollection>(type: "geometry(GeometryCollectionZM, 4326)", nullable: false),
                    min_x = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_x = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_y = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_y = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_z = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_z = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    min_m = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    max_m = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    hierarchy_level = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    atom_count = table.Column<int>(type: "integer", nullable: false),
                    child_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    descendant_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    centroid = table.Column<Point>(type: "geometry(PointZM, 4326)", nullable: false),
                    start_offset = table.Column<long>(type: "bigint", nullable: true),
                    end_offset = table.Column<long>(type: "bigint", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hierarchical_content", x => x.id);
                    table.ForeignKey(
                        name: "FK_hierarchical_content_content_ingestions_content_ingestion_id",
                        column: x => x.content_ingestion_id,
                        principalTable: "content_ingestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_hierarchical_content_hierarchical_content_parent_id",
                        column: x => x.parent_id,
                        principalTable: "hierarchical_content",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "constants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    size = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    projected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hilbert_high = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    hilbert_low = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    hilbert_precision = table.Column<int>(type: "integer", nullable: true, defaultValue: 21),
                    quantized_entropy = table.Column<int>(type: "integer", nullable: true, comment: "Shannon entropy [0, 2^21-1]: content randomness"),
                    quantized_compressibility = table.Column<int>(type: "integer", nullable: true, comment: "Kolmogorov complexity [0, 2^21-1]: gzip compression ratio"),
                    quantized_connectivity = table.Column<int>(type: "integer", nullable: true, comment: "Graph connectivity [0, 2^21-1]: log2(reference_count)"),
                    location = table.Column<Point>(type: "geometry(PointZM, 4326)", nullable: true, comment: "Materialized POINTZM view: X=spatial, Y=entropy, Z=compressibility, M=connectivity"),
                    reference_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CanonicalConstantId = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_constant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_duplicate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deduplicated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    frequency = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_accessed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    LandmarkId = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constants", x => x.id);
                    table.ForeignKey(
                        name: "FK_constants_constants_canonical_constant_id",
                        column: x => x.canonical_constant_id,
                        principalTable: "constants",
                        principalColumn: "id",
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
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    constant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vector_geometry = table.Column<MultiPoint>(type: "geometry(MultiPointZM, 4326)", nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    magnitude = table.Column<double>(type: "double precision", precision: 18, scale: 10, nullable: false),
                    is_normalized = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embeddings", x => x.id);
                    table.ForeignKey(
                        name: "FK_embeddings_constants_constant_id",
                        column: x => x.constant_id,
                        principalTable: "constants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_active_frequency_deleted",
                table: "bpe_tokens",
                columns: new[] { "is_active", "frequency", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_composition_gist",
                table: "bpe_tokens",
                column: "composition_geometry",
                filter: "composition_geometry IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "gist");

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
                name: "ix_bpe_tokens_merge_sequence_deleted",
                table: "bpe_tokens",
                columns: new[] { "merge_level", "sequence_length", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_path_length",
                table: "bpe_tokens",
                column: "path_length",
                filter: "path_length IS NOT NULL");

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
                name: "uq_constant_tokens",
                table: "constant_tokens",
                columns: new[] { "ConstantsId", "ComposingTokensId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_constants_canonical_constant_id",
                table: "constants",
                column: "canonical_constant_id");

            migrationBuilder.CreateIndex(
                name: "ix_constants_compressibility",
                table: "constants",
                column: "quantized_compressibility")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_connectivity",
                table: "constants",
                column: "quantized_connectivity")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_content_type",
                table: "constants",
                column: "content_type");

            migrationBuilder.CreateIndex(
                name: "ix_constants_entropy",
                table: "constants",
                column: "quantized_entropy")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_frequency_hot",
                table: "constants",
                column: "frequency",
                filter: "frequency > 0.001");

            migrationBuilder.CreateIndex(
                name: "ix_constants_hilbert4d",
                table: "constants",
                columns: new[] { "hilbert_high", "hilbert_low" })
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_is_deleted",
                table: "constants",
                column: "is_deleted",
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_constants_is_duplicate",
                table: "constants",
                column: "is_duplicate",
                filter: "is_duplicate = true");

            migrationBuilder.CreateIndex(
                name: "IX_constants_LandmarkId",
                table: "constants",
                column: "LandmarkId");

            migrationBuilder.CreateIndex(
                name: "ix_constants_last_accessed_recent",
                table: "constants",
                column: "last_accessed_at");

            migrationBuilder.CreateIndex(
                name: "ix_constants_location_gist",
                table: "constants",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_constants_metadata_composite",
                table: "constants",
                columns: new[] { "quantized_entropy", "quantized_compressibility", "quantized_connectivity" })
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_size",
                table: "constants",
                column: "size");

            migrationBuilder.CreateIndex(
                name: "ix_constants_status",
                table: "constants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_constants_hash",
                table: "constants",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_area",
                table: "content_boundaries",
                column: "boundary_area");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_atom_count",
                table: "content_boundaries",
                column: "atom_count");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_bbox",
                table: "content_boundaries",
                columns: new[] { "min_x", "max_x", "min_y", "max_y" });

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_centroid_gist",
                table: "content_boundaries",
                column: "centroid")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_computed_at",
                table: "content_boundaries",
                column: "computed_at");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_content_ingestion_id",
                table: "content_boundaries",
                column: "content_ingestion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_density",
                table: "content_boundaries",
                column: "density");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_geometry_gist",
                table: "content_boundaries",
                column: "boundary_geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_method",
                table: "content_boundaries",
                column: "computation_method");

            migrationBuilder.CreateIndex(
                name: "ix_content_boundaries_method_density_deleted",
                table: "content_boundaries",
                columns: new[] { "computation_method", "density", "is_deleted" });

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
                name: "ix_embeddings_constant_id",
                table: "embeddings",
                column: "constant_id");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_constant_model_deleted",
                table: "embeddings",
                columns: new[] { "constant_id", "model_name", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_dimensions",
                table: "embeddings",
                column: "dimensions");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_generated_at",
                table: "embeddings",
                column: "generated_at");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_is_normalized",
                table: "embeddings",
                column: "is_normalized");

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_model",
                table: "embeddings",
                columns: new[] { "model_name", "model_version" });

            migrationBuilder.CreateIndex(
                name: "ix_embeddings_vector_gist",
                table: "embeddings",
                column: "vector_geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_atom_count",
                table: "hierarchical_content",
                column: "atom_count");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_bbox",
                table: "hierarchical_content",
                columns: new[] { "min_x", "max_x", "min_y", "max_y" });

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_centroid_gist",
                table: "hierarchical_content",
                column: "centroid")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_content_ingestion_id",
                table: "hierarchical_content",
                column: "content_ingestion_id");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_descendant_count",
                table: "hierarchical_content",
                column: "descendant_count");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_geometry_gist",
                table: "hierarchical_content",
                column: "complete_geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_hierarchy_level",
                table: "hierarchical_content",
                column: "hierarchy_level");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_ingestion_level_ordinal_deleted",
                table: "hierarchical_content",
                columns: new[] { "content_ingestion_id", "hierarchy_level", "ordinal", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_label",
                table: "hierarchical_content",
                column: "label");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_label_level_deleted",
                table: "hierarchical_content",
                columns: new[] { "label", "hierarchy_level", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_metadata_gin",
                table: "hierarchical_content",
                column: "metadata",
                filter: "metadata IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_offsets",
                table: "hierarchical_content",
                columns: new[] { "start_offset", "end_offset" },
                filter: "start_offset IS NOT NULL AND end_offset IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_ordinal",
                table: "hierarchical_content",
                column: "ordinal");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_parent_id",
                table: "hierarchical_content",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_parent_ordinal_deleted",
                table: "hierarchical_content",
                columns: new[] { "parent_id", "ordinal", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_title",
                table: "hierarchical_content",
                column: "title",
                filter: "title IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hierarchical_content_tree_navigation",
                table: "hierarchical_content",
                columns: new[] { "content_ingestion_id", "parent_id", "hierarchy_level" });

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
                name: "uq_landmarks_hilbert_tile",
                table: "landmarks",
                columns: new[] { "hilbert_prefix_high", "hilbert_prefix_low", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_landmarks_name",
                table: "landmarks",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_activation",
                table: "neural_network_layers",
                column: "activation_function");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_bias_gist",
                table: "neural_network_layers",
                column: "bias_geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_epoch",
                table: "neural_network_layers",
                column: "epoch");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_input_dim",
                table: "neural_network_layers",
                column: "input_dim");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_is_frozen",
                table: "neural_network_layers",
                column: "is_frozen");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_model_epoch_deleted",
                table: "neural_network_layers",
                columns: new[] { "model_id", "epoch", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_model_id",
                table: "neural_network_layers",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_model_index",
                table: "neural_network_layers",
                columns: new[] { "model_id", "layer_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_model_name",
                table: "neural_network_layers",
                columns: new[] { "model_id", "layer_name" });

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_model_type_deleted",
                table: "neural_network_layers",
                columns: new[] { "model_id", "layer_type", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_neuron_count",
                table: "neural_network_layers",
                column: "neuron_count");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_parameter_count",
                table: "neural_network_layers",
                column: "parameter_count");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_type_params_deleted",
                table: "neural_network_layers",
                columns: new[] { "layer_type", "parameter_count", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_weight_gist",
                table: "neural_network_layers",
                column: "weight_geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_neural_network_layers_weight_norm",
                table: "neural_network_layers",
                column: "weight_norm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "constant_tokens");

            migrationBuilder.DropTable(
                name: "content_boundaries");

            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "hierarchical_content");

            migrationBuilder.DropTable(
                name: "neural_network_layers");

            migrationBuilder.DropTable(
                name: "bpe_tokens");

            migrationBuilder.DropTable(
                name: "constants");

            migrationBuilder.DropTable(
                name: "content_ingestions");

            migrationBuilder.DropTable(
                name: "landmarks");
        }
    }
}
