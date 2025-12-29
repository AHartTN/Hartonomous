#pragma once

namespace hartonomous::db {

/// SQL schema definitions for the Hartonomous database.
/// These are the canonical table and index definitions.
struct Schema {
    /// Main schema creation SQL (idempotent - uses IF NOT EXISTS)
    static constexpr const char* CREATE_SCHEMA = R"(
        CREATE EXTENSION IF NOT EXISTS postgis;

        CREATE TABLE IF NOT EXISTS atom (
            hilbert_high BIGINT NOT NULL,
            hilbert_low BIGINT NOT NULL,
            codepoint INTEGER,
            child_count SMALLINT NOT NULL DEFAULT 0,
            semantic_position GEOMETRY(POINTZM, 0),
            PRIMARY KEY (hilbert_high, hilbert_low)
        );

        CREATE TABLE IF NOT EXISTS composition (
            hilbert_high BIGINT NOT NULL,
            hilbert_low BIGINT NOT NULL,
            left_high BIGINT NOT NULL,
            left_low BIGINT NOT NULL,
            right_high BIGINT NOT NULL,
            right_low BIGINT NOT NULL,
            trajectory GEOMETRY(LINESTRINGZM, 0),
            obs_count INTEGER NOT NULL DEFAULT 1,
            PRIMARY KEY (hilbert_high, hilbert_low)
        );

        CREATE INDEX IF NOT EXISTS idx_atom_semantic_position ON atom USING GIST (semantic_position);
        CREATE INDEX IF NOT EXISTS idx_atom_codepoint ON atom (codepoint) WHERE codepoint IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_composition_left ON composition (left_high, left_low);
        CREATE INDEX IF NOT EXISTS idx_composition_right ON composition (right_high, right_low);
        CREATE INDEX IF NOT EXISTS idx_composition_trajectory ON composition USING GIST (trajectory);

        -- WEIGHTED RELATIONSHIPS: A → B with weight/trajectory
        -- For storing model weights, semantic links, knowledge graph edges
        CREATE TABLE IF NOT EXISTS relationship (
            -- Source atom/composition
            from_high BIGINT NOT NULL,
            from_low BIGINT NOT NULL,

            -- Target atom/composition
            to_high BIGINT NOT NULL,
            to_low BIGINT NOT NULL,

            -- Weight as numeric (for model weights, scores, etc.)
            weight DOUBLE PRECISION NOT NULL DEFAULT 1.0,

            -- Trajectory through semantic space (start → end as LINESTRINGZM)
            -- X,Y,Z,M at each point = page, type, base, variant
            trajectory GEOMETRY(LINESTRINGZM, 0),

            -- Relationship type (for categorizing: model_weight, semantic_link, etc.)
            rel_type SMALLINT NOT NULL DEFAULT 0,

            -- Model/context identifier (which model or context this relationship belongs to)
            context_high BIGINT DEFAULT 0,
            context_low BIGINT DEFAULT 0,

            PRIMARY KEY (from_high, from_low, to_high, to_low, context_high, context_low)
        );

        -- Indexes for relationship queries
        CREATE INDEX IF NOT EXISTS idx_relationship_from ON relationship (from_high, from_low);
        CREATE INDEX IF NOT EXISTS idx_relationship_to ON relationship (to_high, to_low);
        CREATE INDEX IF NOT EXISTS idx_relationship_context ON relationship (context_high, context_low);
        CREATE INDEX IF NOT EXISTS idx_relationship_weight ON relationship (weight);
        CREATE INDEX IF NOT EXISTS idx_relationship_trajectory ON relationship USING GIST (trajectory);
    )";

    /// COPY statement for atoms table
    static constexpr const char* COPY_ATOMS = 
        "COPY atom (hilbert_high, hilbert_low, codepoint, child_count, semantic_position) FROM STDIN";

    /// COPY statement for compositions (one row per binary composition)
    static constexpr const char* COPY_COMPOSITIONS = 
        "COPY composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low) FROM STDIN";

    /// Insert atoms from staging with conflict resolution
    static constexpr const char* INSERT_ATOMS_FROM_STAGING = R"(
        INSERT INTO atom (hilbert_high, hilbert_low, codepoint, child_count, semantic_position)
        SELECT hilbert_high, hilbert_low, codepoint, child_count, semantic_position FROM atoms_staging
        ON CONFLICT (hilbert_high, hilbert_low) DO NOTHING
    )";

    /// Insert compositions from staging with conflict resolution
    static constexpr const char* INSERT_COMPOSITIONS_FROM_STAGING = R"(
        INSERT INTO atom (hilbert_high, hilbert_low, codepoint, child_count, semantic_position)
        SELECT hilbert_high, hilbert_low, codepoint, child_count, semantic_position FROM comps_staging
        ON CONFLICT (hilbert_high, hilbert_low) DO NOTHING
    )";

    /// Insert composition relations from staging
    static constexpr const char* INSERT_RELATIONS_FROM_STAGING = R"(
        INSERT INTO composition_relation (parent_hilbert_high, parent_hilbert_low, child_index, 
                                          child_hilbert_high, child_hilbert_low, repetition_count)
        SELECT parent_hilbert_high, parent_hilbert_low, child_index, 
               child_hilbert_high, child_hilbert_low, repetition_count FROM struct_staging
        ON CONFLICT (parent_hilbert_high, parent_hilbert_low, child_index) DO NOTHING
    )";

    /// Create unlogged staging table for atoms
    static constexpr const char* CREATE_ATOMS_STAGING = 
        "CREATE UNLOGGED TABLE atoms_staging (hilbert_high BIGINT, hilbert_low BIGINT, "
        "codepoint INTEGER, child_count SMALLINT, semantic_position GEOMETRY(POINTZM, 0))";

    /// COPY statement for atoms staging table
    static constexpr const char* COPY_ATOMS_STAGING = 
        "COPY atoms_staging (hilbert_high, hilbert_low, codepoint, child_count, semantic_position) FROM STDIN";

    /// Create temp staging tables for compositions
    static constexpr const char* CREATE_COMPS_STAGING = 
        "CREATE TEMP TABLE comps_staging (LIKE atom INCLUDING DEFAULTS)";
    
    static constexpr const char* CREATE_STRUCT_STAGING = 
        "CREATE TEMP TABLE struct_staging (LIKE composition_relation INCLUDING DEFAULTS)";

    /// COPY statement for comps staging table
    static constexpr const char* COPY_COMPS_STAGING = 
        "COPY comps_staging (hilbert_high, hilbert_low, codepoint, child_count, semantic_position) FROM STDIN";

    /// COPY statement for struct staging table
    static constexpr const char* COPY_STRUCT_STAGING = 
        "COPY struct_staging (parent_hilbert_high, parent_hilbert_low, child_index, "
        "child_hilbert_high, child_hilbert_low, repetition_count) FROM STDIN";

    /// Insert comps from staging with conflict resolution (alias for consistency)
    static constexpr const char* INSERT_COMPS_FROM_STAGING = INSERT_COMPOSITIONS_FROM_STAGING;

    /// Insert struct from staging (alias for consistency)
    static constexpr const char* INSERT_STRUCT_FROM_STAGING = INSERT_RELATIONS_FROM_STAGING;
};

} // namespace hartonomous::db
