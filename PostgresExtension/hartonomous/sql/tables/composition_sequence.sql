CREATE TABLE IF NOT EXISTS hartonomous.CompositionSequence (
    Id UUID PRIMARY KEY,
    CompositionId UUID NOT NULL REFERENCES hartonomous.Composition(Id) ON DELETE CASCADE,
    AtomId UUID NOT NULL REFERENCES hartonomous.Atom(Id) ON DELETE CASCADE,
    Ordinal UINT32 NOT NULL,
    Occurrences UINT32 DEFAULT 1 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_CompositionSequence_CompositionId ON hartonomous.CompositionSequence(CompositionId);
CREATE INDEX IF NOT EXISTS idx_CompositionSequence_AtomId ON hartonomous.CompositionSequence(AtomId);
CREATE UNIQUE INDEX IF NOT EXISTS uq_CompositionSequence_CompositionId_Ordinal ON hartonomous.CompositionSequence(CompositionId, Ordinal);
