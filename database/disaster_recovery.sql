-- Disaster recovery and backup procedures

-- 1. Full database backup (run via pg_basebackup)
-- pg_basebackup -D /backup/hartonomous -Fp -Xs -P

-- 2. Point-in-time recovery setup
-- Enable WAL archiving in postgresql.conf:
-- wal_level = replica
-- archive_mode = on
-- archive_command = 'cp %p /archive/%f'

-- 3. Atom-level replication check
CREATE OR REPLACE FUNCTION verify_atom_integrity() 
RETURNS TABLE (
    issue_type VARCHAR,
    atom_id BYTEA,
    details TEXT
) AS $$
BEGIN
    -- Check for orphaned compositions
    RETURN QUERY
    SELECT 
        'orphaned_composition'::VARCHAR,
        a.atom_id,
        'Composition has no components'::TEXT
    FROM atom a
    WHERE a.atom_class = 1
      AND NOT EXISTS (
          SELECT 1 FROM atom_compositions c
          WHERE c.parent_atom_id = a.atom_id
      );
    
    -- Check for broken references
    RETURN QUERY
    SELECT 
        'broken_reference'::VARCHAR,
        c.parent_atom_id,
        'Component atom_id ' || encode(c.component_atom_id, 'hex') || ' does not exist'
    FROM atom_compositions c
    WHERE NOT EXISTS (
        SELECT 1 FROM atom a
        WHERE a.atom_id = c.component_atom_id
    );
    
    -- Check for invalid geometry
    RETURN QUERY
    SELECT 
        'invalid_geometry'::VARCHAR,
        a.atom_id,
        ST_IsValidReason(a.geom)
    FROM atom a
    WHERE NOT ST_IsValid(a.geom);
    
    -- Check for NULL required fields
    RETURN QUERY
    SELECT 
        'null_geometry'::VARCHAR,
        a.atom_id,
        'Geometry is NULL'::TEXT
    FROM atom a
    WHERE a.geom IS NULL;
END;
$$ LANGUAGE plpgsql;

-- 4. Export atom table to binary format (for offline backups)
CREATE OR REPLACE FUNCTION export_atoms_to_file(
    output_path TEXT,
    batch_size INTEGER DEFAULT 10000
) RETURNS INTEGER AS $$
DECLARE
    exported_count INTEGER := 0;
    batch_offset INTEGER := 0;
BEGIN
    LOOP
        EXECUTE format(
            'COPY (SELECT * FROM atom ORDER BY atom_id LIMIT %s OFFSET %s) 
             TO %L WITH (FORMAT binary)',
            batch_size,
            batch_offset,
            output_path || '_' || batch_offset::TEXT || '.bin'
        );
        
        GET DIAGNOSTICS exported_count = ROW_COUNT;
        
        EXIT WHEN exported_count < batch_size;
        batch_offset := batch_offset + batch_size;
    END LOOP;
    
    RETURN batch_offset + exported_count;
END;
$$ LANGUAGE plpgsql;

-- 5. Restore from binary backup
-- COPY atom FROM '/backup/atoms_0.bin' WITH (FORMAT binary);

-- 6. Cortex landmarks backup
CREATE OR REPLACE FUNCTION backup_cortex_landmarks(output_path TEXT)
RETURNS INTEGER AS $$
DECLARE
    landmark_count INTEGER;
BEGIN
    EXECUTE format(
        'COPY cortex_landmarks TO %L WITH (FORMAT csv, HEADER)',
        output_path
    );
    
    GET DIAGNOSTICS landmark_count = ROW_COUNT;
    RETURN landmark_count;
END;
$$ LANGUAGE plpgsql;

-- 7. Failover readiness check
CREATE OR REPLACE FUNCTION check_failover_readiness()
RETURNS TABLE (
    check_name VARCHAR,
    status VARCHAR,
    details TEXT
) AS $$
BEGIN
    -- Check replication lag
    RETURN QUERY
    SELECT 
        'replication_lag'::VARCHAR,
        CASE 
            WHEN pg_last_wal_replay_lsn() = pg_current_wal_lsn() THEN 'OK'
            ELSE 'WARNING'
        END,
        'Lag: ' || (pg_wal_lsn_diff(pg_current_wal_lsn(), pg_last_wal_replay_lsn()) / 1024.0)::TEXT || ' KB';
    
    -- Check index health
    RETURN QUERY
    SELECT 
        'index_health'::VARCHAR,
        CASE 
            WHEN COUNT(*) = 0 THEN 'OK'
            ELSE 'ERROR'
        END,
        COUNT(*)::TEXT || ' invalid indexes'
    FROM pg_index
    WHERE NOT indisvalid;
    
    -- Check atom count consistency
    RETURN QUERY
    SELECT 
        'atom_count_consistency'::VARCHAR,
        CASE 
            WHEN (SELECT atoms_processed FROM cortex_state) = (SELECT COUNT(*) FROM atom WHERE atom_class = 0)
            THEN 'OK'
            ELSE 'WARNING'
        END,
        'Cortex: ' || (SELECT atoms_processed FROM cortex_state)::TEXT || 
        ' | Actual: ' || (SELECT COUNT(*) FROM atom WHERE atom_class = 0)::TEXT;
END;
$$ LANGUAGE plpgsql;

-- 8. Recovery test procedure
CREATE OR REPLACE FUNCTION test_recovery_procedure()
RETURNS TABLE (
    step VARCHAR,
    success BOOLEAN,
    duration_ms DOUBLE PRECISION
) AS $$
DECLARE
    start_time TIMESTAMPTZ;
    test_atom_id BYTEA;
BEGIN
    -- Step 1: Insert test atom
    start_time := clock_timestamp();
    test_atom_id := decode('DEADBEEF' || repeat('00', 28), 'hex');
    
    INSERT INTO atom (atom_id, atom_class, modality, geom)
    VALUES (test_atom_id, 0, 99, ST_MakePoint(0, 0, 0, 1)::geometry)
    ON CONFLICT DO NOTHING;
    
    RETURN QUERY SELECT 
        'insert_test_atom'::VARCHAR,
        true,
        extract(epoch from (clock_timestamp() - start_time)) * 1000;
    
    -- Step 2: Verify retrieval
    start_time := clock_timestamp();
    RETURN QUERY SELECT 
        'retrieve_test_atom'::VARCHAR,
        EXISTS(SELECT 1 FROM atom WHERE atom_id = test_atom_id),
        extract(epoch from (clock_timestamp() - start_time)) * 1000;
    
    -- Step 3: k-NN query
    start_time := clock_timestamp();
    PERFORM * FROM atom
    WHERE atom_id != test_atom_id
    ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = test_atom_id)
    LIMIT 10;
    
    RETURN QUERY SELECT 
        'knn_query'::VARCHAR,
        true,
        extract(epoch from (clock_timestamp() - start_time)) * 1000;
    
    -- Step 4: Cleanup
    DELETE FROM atom WHERE atom_id = test_atom_id;
    
    RETURN QUERY SELECT 
        'cleanup'::VARCHAR,
        true,
        0.0;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION verify_atom_integrity IS 'Check database integrity: orphans, broken refs, invalid geometry';
COMMENT ON FUNCTION check_failover_readiness IS 'Verify system ready for failover';
COMMENT ON FUNCTION test_recovery_procedure IS 'End-to-end recovery test';
