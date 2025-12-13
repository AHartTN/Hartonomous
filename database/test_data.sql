-- Test Data Population Script
-- Creates sample atoms for validation

-- Enable pgcrypto for gen_random_bytes
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Insert numeric constants (Z=0, raw data)
INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom, hilbert_index)
SELECT
    gen_random_bytes(32) as atom_id,
    0 as atom_class,
    1 as modality,
    int4send(i::int) as atomic_value,
    ST_SetSRID(ST_MakePoint(
        (random() - 0.5) * 100,  -- X
        (random() - 0.5) * 100,  -- Y
        0.0,                      -- Z (raw)
        random()                  -- M (salience)
    ), 0) as geom,
    (random() * 9223372036854775807)::bigint as hilbert_index
FROM generate_series(1, 1000) i
ON CONFLICT DO NOTHING;

-- Insert text token constants
WITH tokens AS (
    SELECT unnest(ARRAY[
        'the', 'be', 'to', 'of', 'and', 'a', 'in', 'that', 'have', 'i',
        'it', 'for', 'not', 'on', 'with', 'he', 'as', 'you', 'do', 'at'
    ]) as token
)
INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom, hilbert_index)
SELECT
    gen_random_bytes(32) as atom_id,
    0 as atom_class,
    2 as modality,
    token::bytea as atomic_value,
    ST_SetSRID(ST_MakePoint(
        (random() - 0.5) * 100,
        (random() - 0.5) * 100,
        0.0,
        random()
    ), 0) as geom,
    (random() * 9223372036854775807)::bigint as hilbert_index
FROM tokens
ON CONFLICT DO NOTHING;

-- Verify insertion
SELECT
    atom_class,
    modality,
    COUNT(*) as count,
    AVG(ST_X(geom)) as avg_x,
    AVG(ST_Y(geom)) as avg_y,
    AVG(ST_Z(geom)) as avg_z,
    AVG(ST_M(geom)) as avg_m
FROM atom
GROUP BY atom_class, modality;

-- Test k-NN query performance
EXPLAIN ANALYZE
WITH target AS (
    SELECT geom FROM atom LIMIT 1
)
SELECT
    a.atom_id,
    ST_X(a.geom) as x,
    ST_Y(a.geom) as y,
    ST_3DDistance(a.geom, t.geom) as distance
FROM atom a, target t
ORDER BY a.geom <-> t.geom
LIMIT 10;
