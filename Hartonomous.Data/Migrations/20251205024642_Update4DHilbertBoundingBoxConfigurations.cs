using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class Update4DHilbertBoundingBoxConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "ix_constants_frequency",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_hilbert_index",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_is_deleted",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_last_accessed",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "IX_constant_tokens_ConstantsId",
                table: "constant_tokens");

            migrationBuilder.DropColumn(
                name: "center_hilbert_index",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_x",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_y",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_z",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "coordinate_x",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "coordinate_y",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "coordinate_z",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "hilbert_index",
                table: "constants");

            migrationBuilder.RenameColumn(
                name: "center_hilbert_precision",
                table: "landmarks",
                newName: "center_precision");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "constants",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "ix_constants_location_spatial",
                table: "constants",
                newName: "ix_constants_location_gist");

            migrationBuilder.RenameIndex(
                name: "ix_constants_hash",
                table: "constants",
                newName: "uq_constants_hash");

            migrationBuilder.AlterColumn<int>(
                name: "center_precision",
                table: "landmarks",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 21);

            migrationBuilder.AddColumn<decimal>(
                name: "center_hilbert_high",
                table: "landmarks",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "center_hilbert_low",
                table: "landmarks",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "center_quantized_compressibility",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "center_quantized_connectivity",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "center_quantized_entropy",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "reference_count",
                table: "constants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Point>(
                name: "location",
                table: "constants",
                type: "geometry(PointZM, 4326)",
                nullable: true,
                comment: "Materialized POINTZM view: X=spatial, Y=entropy, Z=compressibility, M=connectivity",
                oldClrType: typeof(Point),
                oldType: "geometry(PointZ)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_duplicate",
                table: "constants",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "constants",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<long>(
                name: "frequency",
                table: "constants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<decimal>(
                name: "hilbert_high",
                table: "constants",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hilbert_low",
                table: "constants",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantized_compressibility",
                table: "constants",
                type: "integer",
                nullable: true,
                comment: "Kolmogorov complexity [0, 2^21-1]: gzip compression ratio");

            migrationBuilder.AddColumn<int>(
                name: "quantized_connectivity",
                table: "constants",
                type: "integer",
                nullable: true,
                comment: "Graph connectivity [0, 2^21-1]: log2(reference_count)");

            migrationBuilder.AddColumn<int>(
                name: "quantized_entropy",
                table: "constants",
                type: "integer",
                nullable: true,
                comment: "Shannon entropy [0, 2^21-1]: content randomness");

            migrationBuilder.AddColumn<LineString>(
                name: "composition_geometry",
                table: "bpe_tokens",
                type: "geometry(LineStringZM, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "path_length",
                table: "bpe_tokens",
                type: "double precision",
                precision: 18,
                scale: 6,
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks",
                columns: new[] { "center_hilbert_high", "center_hilbert_low" })
                .Annotation("Npgsql:IndexMethod", "btree");

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
                name: "ix_constants_last_accessed_recent",
                table: "constants",
                column: "last_accessed_at",
                filter: "last_accessed_at > NOW() - INTERVAL '7 days'");

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
                name: "uq_constant_tokens",
                table: "constant_tokens",
                columns: new[] { "ConstantsId", "ComposingTokensId" },
                unique: true);

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
                name: "ix_bpe_tokens_merge_sequence_deleted",
                table: "bpe_tokens",
                columns: new[] { "merge_level", "sequence_length", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_bpe_tokens_path_length",
                table: "bpe_tokens",
                column: "path_length",
                filter: "path_length IS NOT NULL");

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
                name: "content_boundaries");

            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "hierarchical_content");

            migrationBuilder.DropTable(
                name: "neural_network_layers");

            migrationBuilder.DropIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "ix_constants_compressibility",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_connectivity",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_content_type",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_entropy",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_frequency_hot",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_hilbert4d",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_is_deleted",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_is_duplicate",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_last_accessed_recent",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_metadata_composite",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "ix_constants_size",
                table: "constants");

            migrationBuilder.DropIndex(
                name: "uq_constant_tokens",
                table: "constant_tokens");

            migrationBuilder.DropIndex(
                name: "ix_bpe_tokens_active_frequency_deleted",
                table: "bpe_tokens");

            migrationBuilder.DropIndex(
                name: "ix_bpe_tokens_composition_gist",
                table: "bpe_tokens");

            migrationBuilder.DropIndex(
                name: "ix_bpe_tokens_merge_sequence_deleted",
                table: "bpe_tokens");

            migrationBuilder.DropIndex(
                name: "ix_bpe_tokens_path_length",
                table: "bpe_tokens");

            migrationBuilder.DropColumn(
                name: "center_hilbert_high",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_hilbert_low",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_quantized_compressibility",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_quantized_connectivity",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "center_quantized_entropy",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "hilbert_high",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "hilbert_low",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "quantized_compressibility",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "quantized_connectivity",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "quantized_entropy",
                table: "constants");

            migrationBuilder.DropColumn(
                name: "composition_geometry",
                table: "bpe_tokens");

            migrationBuilder.DropColumn(
                name: "path_length",
                table: "bpe_tokens");

            migrationBuilder.RenameColumn(
                name: "center_precision",
                table: "landmarks",
                newName: "center_hilbert_precision");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "constants",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "uq_constants_hash",
                table: "constants",
                newName: "ix_constants_hash");

            migrationBuilder.RenameIndex(
                name: "ix_constants_location_gist",
                table: "constants",
                newName: "ix_constants_location_spatial");

            migrationBuilder.AlterColumn<int>(
                name: "center_hilbert_precision",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 21,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<long>(
                name: "center_hilbert_index",
                table: "landmarks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "center_x",
                table: "landmarks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "center_y",
                table: "landmarks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "center_z",
                table: "landmarks",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AlterColumn<long>(
                name: "reference_count",
                table: "constants",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<Point>(
                name: "location",
                table: "constants",
                type: "geometry(PointZ)",
                nullable: true,
                oldClrType: typeof(Point),
                oldType: "geometry(PointZM, 4326)",
                oldNullable: true,
                oldComment: "Materialized POINTZM view: X=spatial, Y=entropy, Z=compressibility, M=connectivity");

            migrationBuilder.AlterColumn<bool>(
                name: "is_duplicate",
                table: "constants",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "constants",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<long>(
                name: "frequency",
                table: "constants",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "coordinate_x",
                table: "constants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "coordinate_y",
                table: "constants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "coordinate_z",
                table: "constants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "hilbert_index",
                table: "constants",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_landmarks_center_hilbert_index",
                table: "landmarks",
                column: "center_hilbert_index")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_frequency",
                table: "constants",
                column: "frequency");

            migrationBuilder.CreateIndex(
                name: "ix_constants_hilbert_index",
                table: "constants",
                column: "hilbert_index")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_constants_is_deleted",
                table: "constants",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_constants_last_accessed",
                table: "constants",
                column: "last_accessed_at");

            migrationBuilder.CreateIndex(
                name: "IX_constant_tokens_ConstantsId",
                table: "constant_tokens",
                column: "ConstantsId");
        }
    }
}
