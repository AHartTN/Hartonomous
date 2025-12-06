using System;
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
            // Create get_nearby_constants function
            migrationBuilder.Sql(@"
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
                ) AS $$
                BEGIN
                    RETURN QUERY
                    SELECT 
                        c.id, c.hash, c.data, c.size, c.content_type, c.status,
                        c.hilbert_high, c.hilbert_low, c.hilbert_precision,
                        c.quantized_entropy, c.quantized_compressibility, c.quantized_connectivity,
                        c.location, c.reference_count, c.frequency,
                        c.is_duplicate, c.canonical_constant_id,
                        c.projected_at, c.activated_at, c.deduplicated_at,
                        c.first_seen_at, c.last_accessed_at, c.error_message,
                        c.created_at, c.created_by, c.updated_at, c.updated_by,
                        c.is_deleted, c.deleted_at, c.deleted_by
                    FROM constants c
                    WHERE NOT c.is_deleted
                    AND SQRT(
                        POWER(ST_Y(c.location) - p_entropy, 2) +
                        POWER(ST_Z(c.location) - p_compressibility, 2) +
                        POWER(ST_M(c.location) - p_connectivity, 2)
                    ) <= p_radius
                    ORDER BY SQRT(
                        POWER(ST_Y(c.location) - p_entropy, 2) +
                        POWER(ST_Z(c.location) - p_compressibility, 2) +
                        POWER(ST_M(c.location) - p_connectivity, 2)
                    )
                    LIMIT p_max_results;
                END;
                $$ LANGUAGE plpgsql STABLE;
            ");

            // Create get_k_nearest_constants function
            migrationBuilder.Sql(@"
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
                ) AS $$
                BEGIN
                    RETURN QUERY
                    SELECT 
                        c.id, c.hash, c.data, c.size, c.content_type, c.status,
                        c.hilbert_high, c.hilbert_low, c.hilbert_precision,
                        c.quantized_entropy, c.quantized_compressibility, c.quantized_connectivity,
                        c.location, c.reference_count, c.frequency,
                        c.is_duplicate, c.canonical_constant_id,
                        c.projected_at, c.activated_at, c.deduplicated_at,
                        c.first_seen_at, c.last_accessed_at, c.error_message,
                        c.created_at, c.created_by, c.updated_at, c.updated_by,
                        c.is_deleted, c.deleted_at, c.deleted_by,
                        SQRT(
                            POWER(ST_Y(c.location) - p_entropy, 2) +
                            POWER(ST_Z(c.location) - p_compressibility, 2) +
                            POWER(ST_M(c.location) - p_connectivity, 2)
                        )::FLOAT AS distance
                    FROM constants c
                    WHERE NOT c.is_deleted
                    ORDER BY distance
                    LIMIT p_k;
                END;
                $$ LANGUAGE plpgsql STABLE;
            ");

            // Create get_by_hilbert_range function
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION get_by_hilbert_range(
                    p_hilbert_start BIGINT,
                    p_hilbert_end BIGINT,
                    p_max_results INT DEFAULT 1000
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
                ) AS $$
                BEGIN
                    RETURN QUERY
                    SELECT 
                        c.id, c.hash, c.data, c.size, c.content_type, c.status,
                        c.hilbert_high, c.hilbert_low, c.hilbert_precision,
                        c.quantized_entropy, c.quantized_compressibility, c.quantized_connectivity,
                        c.location, c.reference_count, c.frequency,
                        c.is_duplicate, c.canonical_constant_id,
                        c.projected_at, c.activated_at, c.deduplicated_at,
                        c.first_seen_at, c.last_accessed_at, c.error_message,
                        c.created_at, c.created_by, c.updated_at, c.updated_by,
                        c.is_deleted, c.deleted_at, c.deleted_by
                    FROM constants c
                    WHERE NOT c.is_deleted
                    AND c.hilbert_low >= p_hilbert_start
                    AND c.hilbert_low <= p_hilbert_end
                    ORDER BY c.hilbert_low
                    LIMIT p_max_results;
                END;
                $$ LANGUAGE plpgsql STABLE;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_nearby_constants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_k_nearest_constants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_by_hilbert_range;");
        }
    }
}
