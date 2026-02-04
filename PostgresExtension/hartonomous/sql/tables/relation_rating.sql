-- ==============================================================================
-- RelationRating: Dual-ELO System for Gravitational Truth
-- ==============================================================================

CREATE TABLE IF NOT EXISTS hartonomous.RelationRating (
    RelationId UUID PRIMARY KEY REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,

    -- Consensus ELO (Quantity/Frequency): Increments logarithmically with every duplicate insertion.
    ConsensusElo DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    
    -- Base ELO (Quality): Assigned at ingestion based on source authority/reputation.
    BaseElo DOUBLE PRECISION NOT NULL DEFAULT 1500.0,

    -- Total observations count
    Observations UINT64 DEFAULT '\x0000000000000001'::bytea NOT NULL,
    
    -- Sensitivity factor for updates
    KFactor DOUBLE PRECISION DEFAULT 32.0 NOT NULL,

    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationRating_Consensus ON hartonomous.RelationRating(ConsensusElo);
CREATE INDEX IF NOT EXISTS idx_RelationRating_Base ON hartonomous.RelationRating(BaseElo);

COMMENT ON TABLE hartonomous.RelationRating IS 'Stores the Dual-ELO rating representing the quality (Base) and consensus (Consensus) of a Relation.';