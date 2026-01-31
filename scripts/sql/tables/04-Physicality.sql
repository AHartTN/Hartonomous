-- ==============================================================================
-- Physicality: The Shared Physicality of objects
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Physicality (
    Id UUID PRIMARY KEY,

    -- Hilbert curve index (128-bit)
    Hilbert UINT128 NOT NULL,

    -- PostGIS 4D geometry (SRID 0 for abstract space)
    Centroid GEOMETRY(POINTZM, 0) NOT NULL,

    -- 4D path through space - POINTZM for static objects, LINESTRINGZM for dynamic objects
    Trajectory GEOMETRY(GEOMETRYZM, 0),

    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

    -- Ensure centroid is on S³ (Normalized magnitude = 1.0)
    CONSTRAINT Physicality_Centroid_Normalized CHECK (ABS(ST_X(Centroid) * ST_X(Centroid) + ST_Y(Centroid) * ST_Y(Centroid) + ST_Z(Centroid) * ST_Z(Centroid) + ST_M(Centroid) * ST_M(Centroid) - 1.0) < 0.0001)
);

-- Indexes for fast spatial queries
CREATE INDEX idx_Physicality_hilbert ON Physicality(Hilbert);

-- Spatial indicies using GIST with N-Dimensional support
CREATE INDEX IF NOT EXISTS idx_Physicality_Centroid ON Physicality USING GIST(Centroid gist_geometry_ops_nd);
CREATE INDEX IF NOT EXISTS idx_Physicality_Trajectory ON Physicality USING GIST(Trajectory gist_geometry_ops_nd);

-- Comment
COMMENT ON TABLE Physicality IS 'Shared physicality of objects in 4D space';
COMMENT ON COLUMN Physicality.Id IS 'BLAKE3 hash of physicality metadata (content-addressable key)';
COMMENT ON COLUMN Physicality.Hilbert IS 'Hilbert space-filling curve index for spatial queries';
COMMENT ON COLUMN Physicality.Centroid IS '4D POINTZM representing the Physicality''s position on the 3-sphere (S³)';
COMMENT ON COLUMN Physicality.Trajectory IS '4D GEOMETRYZM representing the Physicality''s trajectory through S³';
COMMENT ON COLUMN Physicality.CreatedAt IS 'Timestamp of first insertion into the Physicality table';
COMMENT ON CONSTRAINT Physicality_Centroid_Normalized ON Physicality IS 'Ensures that the Centroid lies on the surface of the 3-sphere (S³)';