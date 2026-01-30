-- ==============================================================================
-- RelationSequence: The sequential arrangement of compositions within Relations (n-grams of n-grams)
-- ==============================================================================

CREATE TABLE IF NOT EXISTS RelationSequence (
    Id UUID PRIMARY KEY,
    RelationId UUID NOT NULL REFERENCES Relation(Id) ON DELETE CASCADE,
    CompositionId UUID NOT NULL REFERENCES Composition(Id) ON DELETE CASCADE,
    Ordinal UINT32 NOT NULL,
    Occurrences UINT32 DEFAULT 1 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_RelationSequence_RelationId_Ordinal ON RelationSequence(RelationId, Ordinal);

CREATE INDEX IF NOT EXISTS idx_RelationSequence_RelationId ON RelationSequence(RelationId, Ordinal ASC, Occurrences);
CREATE INDEX IF NOT EXISTS idx_RelationSequence_CompositionId ON RelationSequence(CompositionId, RelationId);

CREATE INDEX IF NOT EXISTS idx_RelationSequence_CreatedAt ON RelationSequence(CreatedAt);
CREATE INDEX IF NOT EXISTS idx_RelationSequence_ModifiedAt ON RelationSequence(ModifiedAt);
CREATE INDEX IF NOT EXISTS idx_RelationSequence_ValidatedAt ON RelationSequence(ValidatedAt);

COMMENT ON TABLE RelationSequence IS 'The sequential arrangement of Compositions within Relations (n-grams of n-grams)';
COMMENT ON COLUMN RelationSequence.Id IS 'BLAKE3 hash of Relation sequence metadata (content-addressable key)';
COMMENT ON COLUMN RelationSequence.RelationId IS 'Reference to the Relation record';
COMMENT ON COLUMN RelationSequence.CompositionId IS 'Reference to the Composition record';
COMMENT ON COLUMN RelationSequence.Ordinal IS 'Position of the Composition within the Relation (0-indexed)';
COMMENT ON COLUMN RelationSequence.Occurrences IS 'Number of times the Composition appears in the Relation at this position (for RLE)';
COMMENT ON COLUMN RelationSequence.CreatedAt IS 'Timestamp of first insertion into the RelationSequence table';
COMMENT ON COLUMN RelationSequence.ModifiedAt IS 'Timestamp of last modification to the RelationSequence record';
COMMENT ON COLUMN RelationSequence.ValidatedAt IS 'Timestamp of last validation of the RelationSequence record';
