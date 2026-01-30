CREATE OR REPLACE FUNCTION hartonomous.upsert_composition(
    p_hash BYTEA,
    p_text TEXT,
    p_centroid_x DOUBLE PRECISION,
    p_centroid_y DOUBLE PRECISION,
    p_centroid_z DOUBLE PRECISION,
    p_centroid_w DOUBLE PRECISION,
    p_hilbert_index BIGINT
)
RETURNS VOID
LANGUAGE sql
AS $$
    INSERT INTO hartonomous.compositions (
        hash, text,
        centroid_x, centroid_y, centroid_z, centroid_w,
        centroid,
        hilbert_index,
        length
    ) VALUES (
        p_hash, p_text,
        p_centroid_x, p_centroid_y, p_centroid_z, p_centroid_w,
        ST_SetSRID(ST_MakePoint(p_centroid_x, p_centroid_y, p_centroid_z, p_centroid_w), 0),
        p_hilbert_index,
        length(p_text)
    )
    ON CONFLICT (hash) DO UPDATE SET
        access_count = hartonomous.compositions.access_count + 1;
$$;