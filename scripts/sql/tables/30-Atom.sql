-- ==============================================================================
-- Atom: Unicode Codepoints
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Atom (
    -- Unique BLAKE3 identifier, stored as a UUID (16 bytes)
    Id UUID PRIMARY KEY,
    
    -- Unicode codepoint represented by this Atom stored as a 16-bit bytea array
    Codepoint UINT32 NOT NULL UNIQUE,

    -- The physicality record containing the 4d geometric data
    PhysicalityId UUID NOT NULL REFERENCES Physicality(Id) ON DELETE CASCADE,

    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Atom_Codepoint ON Atom(Codepoint);

CREATE INDEX IF NOT EXISTS idx_Atom_Physicality ON Atom(PhysicalityId);

COMMENT ON TABLE Atom IS 'Individual Unicode codepoints with 4D geometric positions on S³';
COMMENT ON COLUMN Atom.Id IS 'BLAKE3 hash of codepoint + context (content-addressable key)';
COMMENT ON COLUMN Atom.Codepoint IS 'Unicode codepoint value (U+0000 to U+10FFFF)';
COMMENT ON COLUMN Atom.PhysicalityId IS 'Reference to the Physicality record containing 4D geometric data';
COMMENT ON COLUMN Atom.CreatedAt IS 'Timestamp of first insertion into the Atom table';
COMMENT ON COLUMN Atom.ModifiedAt IS 'Timestamp of last modification to the Atom record';
COMMENT ON COLUMN Atom.ValidatedAt IS 'Timestamp of last validation of the Atom record';