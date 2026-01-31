-- ==============================================================================
-- CompositionSequence: The sequential arrangement of Atoms within Compositions
-- ==============================================================================

CREATE TABLE IF NOT EXISTS CompositionSequence (
    Id UUID PRIMARY KEY,
    CompositionId UUID NOT NULL REFERENCES Composition(Id) ON DELETE CASCADE,
    AtomId UUID NOT NULL REFERENCES Atom(Id) ON DELETE CASCADE,
    Ordinal UINT32 NOT NULL,
    Occurrences UINT32 DEFAULT '\x00000001'::bytea NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_CompositionSequence_CompositionId ON CompositionSequence(CompositionId);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_AtomId ON CompositionSequence(AtomId);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_Ordinal ON CompositionSequence(Ordinal);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_Occurrences ON CompositionSequence(Occurrences);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_CreatedAt ON CompositionSequence(CreatedAt);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_ModifiedAt ON CompositionSequence(ModifiedAt);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_ValidatedAt ON CompositionSequence(ValidatedAt);

CREATE UNIQUE INDEX IF NOT EXISTS uq_CompositionSequence_CompositionId_Ordinal
    ON CompositionSequence(CompositionId, Ordinal);

COMMENT ON TABLE CompositionSequence IS 'The sequential arrangement of Atoms within Compositions';
COMMENT ON COLUMN CompositionSequence.Id IS 'BLAKE3 hash of composition sequence metadata (content-addressable key)';
COMMENT ON COLUMN CompositionSequence.CompositionId IS 'Reference to the Composition record';
COMMENT ON COLUMN CompositionSequence.AtomId IS 'Reference to the Atom record';
COMMENT ON COLUMN CompositionSequence.Ordinal IS 'Position of the Atom within the Composition (0-indexed)';
COMMENT ON COLUMN CompositionSequence.Occurrences IS 'Number of times the Atom appears in the Composition at this position (for RLE)';
COMMENT ON COLUMN CompositionSequence.CreatedAt IS 'Timestamp of first insertion into the CompositionSequence table';
COMMENT ON COLUMN CompositionSequence.ModifiedAt IS 'Timestamp of last modification to the CompositionSequence record';
COMMENT ON COLUMN CompositionSequence.ValidatedAt IS 'Timestamp of last validation of the CompositionSequence record';


