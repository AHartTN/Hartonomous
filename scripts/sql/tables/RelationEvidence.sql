-- ==============================================================================
-- RelationEvidence: The ELO Evidence of a Relation based on ingestion, user feedback, and system evaluations
-- ==============================================================================

CREATE TABLE IF NOT EXISTS RelationEvidence (
    Id UUID PRIMARY KEY,
    ContentId UUID NOT NULL REFERENCES Content(Id) ON DELETE CASCADE,
    RelationId UUID NOT NULL REFERENCES Relation(Id) ON DELETE CASCADE,
    Observations UINT64 DEFAULT 1 NOT NULL,
    EvidenceValue DOUBLE PRECISION NOT NULL DEFAULT 1000,
    KFactor DOUBLE PRECISION DEFAULT 1.0 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationEvidence_EvidenceValue ON RelationEvidence(EvidenceValue);

COMMENT ON TABLE RelationEvidence IS 'Stores the ELO Evidence of a Relation based on various factors including ingestion quality, user feedback, and system evaluations.';
COMMENT ON COLUMN RelationEvidence.RelationId IS 'The unique identifier of the Relation being rated.';
COMMENT ON COLUMN RelationEvidence.EvidenceValue IS 'The ELO Evidence value representing the quality of the Relation.';
COMMENT ON COLUMN RelationEvidence.Observations IS 'The number of observations or evaluations that have contributed to the current Evidence.';
COMMENT ON COLUMN RelationEvidence.KFactor IS 'The K-factor used in the ELO Evidence calculation, influencing the sensitivity of Evidence changes.';
COMMENT ON COLUMN RelationEvidence.CreatedAt IS 'Timestamp when the Evidence record was created.';
COMMENT ON COLUMN RelationEvidence.ModifiedAt IS 'Timestamp when the Evidence record was last modified.';
COMMENT ON COLUMN RelationEvidence.ValidatedAt IS 'Timestamp when the Evidence was last validated.';