-- =============================================================================
-- Model Projection: "Firefly Jar" Multi-Model Embedding Store
-- =============================================================================
-- Each model that gets ingested projects its vocabulary into S³ via Laplacian
-- eigenmaps + Gram-Schmidt orthonormalization. A single composition ("king")
-- can have multiple projected positions — one per model. This enables:
--   - Multi-perspective spatial queries (MiniLM vs Qwen neighborhoods)
--   - Concept spread analysis (models agree → tight cluster; disagree → scattered)
--   - A* heuristic selection (which model's lens to reason through)
--   - Voronoi cell comparison across model perspectives
-- =============================================================================

CREATE TABLE IF NOT EXISTS hartonomous.modelprojection (
    Id              UUID PRIMARY KEY,
    CompositionId   UUID NOT NULL REFERENCES hartonomous.composition(Id),
    ContentId       UUID NOT NULL REFERENCES hartonomous.content(Id),
    Position        geometry(POINTZM, 0) NOT NULL,
    CreatedAt       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Unique: one projection per composition per model source
CREATE UNIQUE INDEX IF NOT EXISTS idx_modelprojection_comp_content
    ON hartonomous.modelprojection (CompositionId, ContentId);

-- Spatial: find nearby projections from any model
CREATE INDEX IF NOT EXISTS idx_modelprojection_position_gist
    ON hartonomous.modelprojection USING GIST (Position);

-- Lookup: all projections for a composition (the "firefly cloud")
CREATE INDEX IF NOT EXISTS idx_modelprojection_comp
    ON hartonomous.modelprojection (CompositionId);

-- Lookup: all projections from a model source
CREATE INDEX IF NOT EXISTS idx_modelprojection_content
    ON hartonomous.modelprojection (ContentId);

COMMENT ON TABLE hartonomous.modelprojection IS
    'Multi-model embedding projections onto S³. Each row is one model''s '
    'perspective on a composition''s position in semantic space. Multiple '
    'rows per composition enable stereoscopic reasoning.';
