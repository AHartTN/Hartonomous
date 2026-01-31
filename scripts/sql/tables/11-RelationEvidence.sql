-- ==============================================================================
-- RelationEvidence: The ELO Evidence of a Relation based on ingestion, user feedback, and system evaluations
-- ==============================================================================

CREATE TABLE IF NOT EXISTS RelationEvidence (
    Id UUID PRIMARY KEY,
    
    -- Foreign keys
    ContentId UUID NOT NULL REFERENCES Content(Id) ON DELETE CASCADE,
    RelationId UUID NOT NULL REFERENCES Relation(Id) ON DELETE CASCADE,

    --- ELO Evidence fields
    IsValid BOOLEAN DEFAULT TRUE NOT NULL,
    
    -- 1. Source Credibility (The "Opponent Rating")
    -- Derived from the Content source (e.g., trusted paper = 2000, random tweet = 800)
    -- This replaces "EvidenceValue"
    SourceRating DOUBLE PRECISION NOT NULL DEFAULT 1000,

    -- 2. Signal Strength (The "Match Result" / S-Value)
    -- The normalized 0-1 score (Sigmoid/Attention result).
    -- 1.0 = Explicit Definition, 0.5 = Ambiguous/Draw, 0.0 = Contradiction
    SignalStrength DOUBLE PRECISION NOT NULL DEFAULT 1.0 CHECK (SignalStrength BETWEEN 0 AND 1),
    
    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationEvidence_SourceRating ON RelationEvidence(SourceRating);

COMMENT ON TABLE RelationEvidence IS 'ELO Evidence of a Relation based on ingestion, user feedback, and system evaluations';
COMMENT ON COLUMN RelationEvidence.Id IS 'BLAKE3 hash of Evidence metadata (content-addressable key)';
COMMENT ON COLUMN RelationEvidence.ContentId IS 'Reference to the Content record';
COMMENT ON COLUMN RelationEvidence.RelationId IS 'Reference to the Relation record';
COMMENT ON COLUMN RelationEvidence.IsValid IS 'Indicates if the evidence is considered valid';
COMMENT ON COLUMN RelationEvidence.SourceRating IS 'Credibility rating of the source providing the evidence';
COMMENT ON COLUMN RelationEvidence.SignalStrength IS 'Normalized strength of the signal (0 to 1)';
COMMENT ON COLUMN RelationEvidence.CreatedAt IS 'Timestamp when the evidence was created';
COMMENT ON COLUMN RelationEvidence.ModifiedAt IS 'Timestamp when the evidence was last modified';
COMMENT ON COLUMN RelationEvidence.ValidatedAt IS 'Timestamp when the evidence was validated';