-- Horizontal sharding strategy for scaling beyond single PostgreSQL instance

-- Shard configuration table
CREATE TABLE IF NOT EXISTS shard_config (
    shard_id INTEGER PRIMARY KEY,
    shard_name VARCHAR(100) NOT NULL,
    host VARCHAR(255) NOT NULL,
    port INTEGER NOT NULL DEFAULT 5432,
    dbname VARCHAR(100) NOT NULL,
    min_hash BYTEA NOT NULL,  -- Minimum atom_id hash for this shard
    max_hash BYTEA NOT NULL,  -- Maximum atom_id hash for this shard
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Shard assignment function: atom_id -> shard_id
CREATE OR REPLACE FUNCTION get_shard_id(p_atom_id BYTEA) 
RETURNS INTEGER AS $$
DECLARE
    shard INTEGER;
BEGIN
    -- Use first byte of atom_id for simple hash-based sharding
    -- Assumes 256 shards max, but can be adjusted
    shard := get_byte(p_atom_id, 0) % (SELECT COUNT(*) FROM shard_config WHERE is_active);
    RETURN shard;
END;
$$ LANGUAGE plpgsql;

-- Foreign data wrapper setup for cross-shard queries
-- Requires postgres_fdw extension
CREATE EXTENSION IF NOT EXISTS postgres_fdw;

-- Example: Create foreign server for shard 1
-- CREATE SERVER shard_1_server
--     FOREIGN DATA WRAPPER postgres_fdw
--     OPTIONS (host 'shard1.example.com', port '5432', dbname 'hartonomous_shard1');

-- Create user mapping
-- CREATE USER MAPPING FOR hartonomous
--     SERVER shard_1_server
--     OPTIONS (user 'hartonomous', password 'secure_password');

-- Foreign table for remote atom access
-- CREATE FOREIGN TABLE shard_1_atoms (
--     atom_id BYTEA,
--     atom_class SMALLINT,
--     modality SMALLINT,
--     subtype VARCHAR(50),
--     atomic_value BYTEA,
--     geom GEOMETRY(GeometryZM, 4326),
--     hilbert_index BIGINT,
--     metadata JSONB,
--     created_at TIMESTAMPTZ,
--     updated_at TIMESTAMPTZ
-- ) SERVER shard_1_server
-- OPTIONS (schema_name 'public', table_name 'atom');

-- Distributed k-NN query across shards
CREATE OR REPLACE FUNCTION distributed_knn(
    target_atom_id BYTEA,
    k INTEGER DEFAULT 10
) RETURNS TABLE (
    atom_id BYTEA,
    distance DOUBLE PRECISION,
    shard_id INTEGER
) AS $$
DECLARE
    shard_record RECORD;
    query_text TEXT;
BEGIN
    -- Query each shard and aggregate results
    FOR shard_record IN 
        SELECT sc.shard_id, sc.shard_name
        FROM shard_config sc
        WHERE sc.is_active
    LOOP
        -- Build dynamic query for this shard
        query_text := format(
            'SELECT atom_id, 
                    ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %L)) as distance,
                    %s as shard_id
             FROM %I
             WHERE atom_id != %L
             ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %L)
             LIMIT %s',
            target_atom_id,
            shard_record.shard_id,
            shard_record.shard_name || '_atoms',
            target_atom_id,
            target_atom_id,
            k
        );
        
        -- Execute and return results from this shard
        RETURN QUERY EXECUTE query_text;
    END LOOP;
    
    -- Return top k across all shards
    RETURN QUERY
    SELECT t.atom_id, t.distance, t.shard_id
    FROM (
        SELECT * FROM distributed_knn
        ORDER BY distance
        LIMIT k
    ) t;
END;
$$ LANGUAGE plpgsql;

-- Shard rebalancing: Migrate atoms between shards
CREATE OR REPLACE FUNCTION rebalance_shard(
    source_shard_id INTEGER,
    target_shard_id INTEGER,
    batch_size INTEGER DEFAULT 1000
) RETURNS INTEGER AS $$
DECLARE
    migrated_count INTEGER := 0;
    batch RECORD;
BEGIN
    -- Move atoms from source to target shard
    FOR batch IN
        SELECT atom_id
        FROM atom
        WHERE get_shard_id(atom_id) = source_shard_id
        LIMIT batch_size
    LOOP
        -- Copy to target shard (via foreign table)
        EXECUTE format(
            'INSERT INTO shard_%s_atoms SELECT * FROM atom WHERE atom_id = %L',
            target_shard_id,
            batch.atom_id
        );
        
        -- Delete from source
        DELETE FROM atom WHERE atom_id = batch.atom_id;
        
        migrated_count := migrated_count + 1;
    END LOOP;
    
    RETURN migrated_count;
END;
$$ LANGUAGE plpgsql;

-- Monitoring: Shard distribution
CREATE OR REPLACE VIEW v_shard_distribution AS
SELECT
    get_shard_id(atom_id) as shard_id,
    atom_class,
    COUNT(*) as atom_count,
    pg_size_pretty(SUM(octet_length(atomic_value))) as total_size
FROM atom
GROUP BY get_shard_id(atom_id), atom_class
ORDER BY shard_id, atom_class;

COMMENT ON TABLE shard_config IS 'Horizontal sharding configuration for distributed Hartonomous';
COMMENT ON FUNCTION get_shard_id IS 'Determine which shard an atom belongs to based on hash';
COMMENT ON FUNCTION distributed_knn IS 'k-NN query across multiple shards';
COMMENT ON FUNCTION rebalance_shard IS 'Migrate atoms between shards for load balancing';
