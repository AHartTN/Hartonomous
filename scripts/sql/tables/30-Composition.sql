-- ==============================================================================
-- Composition: n-grams of Atoms representing higher-level structures
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Composition (
    Id UUID PRIMARY KEY,

    -- The physicality record containing the 4d geometric data
    PhysicalityId UUID NOT NULL REFERENCES Physicality(Id) ON DELETE CASCADE,

    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Composition_Physicality ON Composition(PhysicalityId);
CREATE INDEX IF NOT EXISTS idx_Composition_CreatedAt ON Composition(CreatedAt);
CREATE INDEX IF NOT EXISTS idx_Composition_ModifiedAt ON Composition(ModifiedAt);
CREATE INDEX IF NOT EXISTS idx_Composition_ValidatedAt ON Composition(ValidatedAt);

COMMENT ON TABLE Composition IS 'n-grams of Atoms forming higher-level structures';
COMMENT ON COLUMN Composition.Id IS 'BLAKE3 hash of Composition content + context (content-addressable key)';
COMMENT ON COLUMN Composition.PhysicalityId IS 'Reference to the Physicality record containing 4D geometric data';
COMMENT ON COLUMN Composition.CreatedAt IS 'Timestamp of first insertion into the Composition table';
COMMENT ON COLUMN Composition.ModifiedAt IS 'Timestamp of last modification to the Composition record';
COMMENT ON COLUMN Composition.ValidatedAt IS 'Timestamp of last validation of the Composition record';