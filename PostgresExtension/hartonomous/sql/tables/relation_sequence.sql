CREATE TABLE IF NOT EXISTS hartonomous.RelationSequence (
    Id UUID PRIMARY KEY,
    RelationId UUID NOT NULL REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,
    CompositionId UUID NOT NULL REFERENCES hartonomous.Composition(Id) ON DELETE CASCADE,
    Ordinal UINT32 NOT NULL,
    Occurrences UINT32 DEFAULT 1 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_RelationSequence_RelationId_Ordinal ON hartonomous.RelationSequence(RelationId, Ordinal);
CREATE INDEX IF NOT EXISTS idx_RelationSequence_RelationId ON hartonomous.RelationSequence(RelationId, Ordinal ASC, Occurrences);

-- CRITICAL: Index for finding all relations a composition participates in (microsecond walks)
CREATE INDEX IF NOT EXISTS idx_RelationSequence_CompositionId ON hartonomous.RelationSequence(CompositionId);
