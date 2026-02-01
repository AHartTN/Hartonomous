CREATE TABLE IF NOT EXISTS hartonomous.Physicality (
    Id UUID PRIMARY KEY,
    Hilbert hartonomous.UINT128 NOT NULL,
    Centroid GEOMETRY(POINTZM, 0) NOT NULL,
    Trajectory GEOMETRY(GEOMETRYZM, 0),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT Physicality_Centroid_Normalized CHECK (ABS(ST_X(Centroid) * ST_X(Centroid) + ST_Y(Centroid) * ST_Y(Centroid) + ST_Z(Centroid) * ST_Z(Centroid) + ST_M(Centroid) * ST_M(Centroid) - 1.0) < 0.0001)
);

CREATE INDEX idx_Physicality_hilbert ON hartonomous.Physicality(Hilbert);
CREATE INDEX IF NOT EXISTS idx_Physicality_Centroid ON hartonomous.Physicality USING GIST(Centroid gist_geometry_ops_nd);
CREATE INDEX IF NOT EXISTS idx_Physicality_Trajectory ON hartonomous.Physicality USING GIST(Trajectory public.gist_geometry_ops_nd);