-- ==============================================================================
-- Relation: Compositions of Compositions essentially forming sequences
-- ==============================================================================

CREATE TABLE IF NOT EXISTS Relation (
    Id UUID PRIMARY KEY,

    -- The physicality record containing the 4d geometric data
    PhysicalityId UUID NOT NULL REFERENCES Physicality(Id) ON DELETE CASCADE,

    -- Metadata
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for fast queries
CREATE INDEX IF NOT EXISTS idx_Relation_Physicality ON Relation(PhysicalityId);

-- Comment
COMMENT ON TABLE Relation IS 'Hierarchical sequences forming a Merkle DAG/Abstract Syntax Tree (n-grams of n-grams)';
COMMENT ON COLUMN Relation.Id IS 'BLAKE3 hash of Relation content + context (content-addressable key)';
COMMENT ON COLUMN Relation.PhysicalityId IS 'Reference to the Physicality record containing 4D geometric data';
COMMENT ON COLUMN Relation.CreatedAt IS 'Timestamp of first insertion into the Relation table';