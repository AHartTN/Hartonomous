using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hartonomous.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpatialQueryFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All SQL is embedded directly in the migration - no external file reads
            migrationBuilder.Sql(GetSpatialQueryFunctionsSql());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_nearby_constants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_k_nearest_constants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_by_hilbert_range;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_top_by_frequency;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_stale_constants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_deduplication_stats;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_total_storage_size;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_nearby_landmarks;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_nearest_landmark;");
        }

        private static string GetSpatialQueryFunctionsSql() => @"
-- Function 1: get_nearby_constants
-- Returns all Constant entity columns for EF Core materialization
-- Filters by Euclidean distance in YZM subspace (quantized metadata)
CREATE OR REPLACE FUNCTION get_nearby_constants(
    p_entropy INT, 
    p_compressibility INT, 
    p_connectivity INT, 
    p_radius FLOAT, 
    p_max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash BYTEA,
    data BYTEA,
    size INT,
    content_type TEXT,
    status TEXT,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    location geometry(PointZM, 4326),
    reference_count BIGINT,
    frequency BIGINT,
    is_duplicate BOOLEAN,
    canonical_constant_id UUID,
    projected_at TIMESTAMPTZ,
    activated_at TIMESTAMPTZ,
    deduplicated_at TIMESTAMPTZ,
    first_seen_at TIMESTAMPTZ,
    last_accessed_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
)
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.content_type,
        c.status,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.location,
        c.reference_count,
        c.frequency,
        c.is_duplicate,
        c.canonical_constant_id,
        c.projected_at,
        c.activated_at,
        c.deduplicated_at,
        c.first_seen_at,
        c.last_accessed_at,
        c.error_message,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM constants c
    WHERE 
        c.hilbert_high IS NOT NULL
        AND c.status = 'Projected'
        AND c.is_deleted = FALSE
        AND sqrt(
            power(CAST(c.quantized_entropy AS FLOAT) - p_entropy, 2) + 
            power(CAST(c.quantized_compressibility AS FLOAT) - p_compressibility, 2) + 
            power(CAST(c.quantized_connectivity AS FLOAT) - p_connectivity, 2)
        ) <= p_radius
    ORDER BY sqrt(
        power(CAST(c.quantized_entropy AS FLOAT) - p_entropy, 2) + 
        power(CAST(c.quantized_compressibility AS FLOAT) - p_compressibility, 2) + 
        power(CAST(c.quantized_connectivity AS FLOAT) - p_connectivity, 2)
    )
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 2: get_k_nearest_constants
-- Returns k nearest neighbors in YZM subspace with distance column
-- EF Core ignores unmapped columns, so distance is safe to include
CREATE OR REPLACE FUNCTION get_k_nearest_constants(
    p_entropy INT, 
    p_compressibility INT, 
    p_connectivity INT, 
    p_k INT DEFAULT 10
)
RETURNS TABLE (
    id UUID,
    hash BYTEA,
    data BYTEA,
    size INT,
    content_type TEXT,
    status TEXT,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    location geometry(PointZM, 4326),
    reference_count BIGINT,
    frequency BIGINT,
    is_duplicate BOOLEAN,
    canonical_constant_id UUID,
    projected_at TIMESTAMPTZ,
    activated_at TIMESTAMPTZ,
    deduplicated_at TIMESTAMPTZ,
    first_seen_at TIMESTAMPTZ,
    last_accessed_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256),
    distance FLOAT
)
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.content_type,
        c.status,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.location,
        c.reference_count,
        c.frequency,
        c.is_duplicate,
        c.canonical_constant_id,
        c.projected_at,
        c.activated_at,
        c.deduplicated_at,
        c.first_seen_at,
        c.last_accessed_at,
        c.error_message,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by,
        sqrt(
            power(CAST(c.quantized_entropy AS FLOAT) - p_entropy, 2) + 
            power(CAST(c.quantized_compressibility AS FLOAT) - p_compressibility, 2) + 
            power(CAST(c.quantized_connectivity AS FLOAT) - p_connectivity, 2)
        ) as distance
    FROM constants c
    WHERE 
        c.hilbert_high IS NOT NULL
        AND c.status = 'Projected'
        AND c.is_deleted = FALSE
    ORDER BY distance
    LIMIT p_k;
END;
$$ LANGUAGE plpgsql STABLE;

-- Function 3: get_by_hilbert_range
-- Returns constants within Hilbert index range for sequential scanning
-- Uses 128-bit Hilbert comparison (high, low) for full 4D coverage
CREATE OR REPLACE FUNCTION get_by_hilbert_range(
    p_start_high BIGINT, 
    p_start_low BIGINT, 
    p_end_high BIGINT, 
    p_end_low BIGINT, 
    p_max_results INT DEFAULT 100
)
RETURNS TABLE (
    id UUID,
    hash BYTEA,
    data BYTEA,
    size INT,
    content_type TEXT,
    status TEXT,
    hilbert_high BIGINT,
    hilbert_low BIGINT,
    hilbert_precision INT,
    quantized_entropy INT,
    quantized_compressibility INT,
    quantized_connectivity INT,
    location geometry(PointZM, 4326),
    reference_count BIGINT,
    frequency BIGINT,
    is_duplicate BOOLEAN,
    canonical_constant_id UUID,
    projected_at TIMESTAMPTZ,
    activated_at TIMESTAMPTZ,
    deduplicated_at TIMESTAMPTZ,
    first_seen_at TIMESTAMPTZ,
    last_accessed_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ,
    created_by VARCHAR(256),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(256),
    is_deleted BOOLEAN,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(256)
)
AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.hash,
        c.data,
        c.size,
        c.content_type,
        c.status,
        c.hilbert_high,
        c.hilbert_low,
        c.hilbert_precision,
        c.quantized_entropy,
        c.quantized_compressibility,
        c.quantized_connectivity,
        c.location,
        c.reference_count,
        c.frequency,
        c.is_duplicate,
        c.canonical_constant_id,
        c.projected_at,
        c.activated_at,
        c.deduplicated_at,
        c.first_seen_at,
        c.last_accessed_at,
        c.error_message,
        c.created_at,
        c.created_by,
        c.updated_at,
        c.updated_by,
        c.is_deleted,
        c.deleted_at,
        c.deleted_by
    FROM constants c
    WHERE 
        c.hilbert_high IS NOT NULL
        AND c.is_deleted = FALSE
        AND (
            c.hilbert_high > p_start_high 
            OR (c.hilbert_high = p_start_high AND c.hilbert_low >= p_start_low)
        )
        AND (
            c.hilbert_high < p_end_high 
            OR (c.hilbert_high = p_end_high AND c.hilbert_low <= p_end_low)
        )
    ORDER BY c.hilbert_high, c.hilbert_low
    LIMIT p_max_results;
END;
$$ LANGUAGE plpgsql STABLE;
";
    }
}
