CREATE TABLE IF NOT EXISTS hartonomous.Relation (
    Id UUID PRIMARY KEY,
    PhysicalityId UUID NOT NULL REFERENCES hartonomous.Physicality(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Relation_Physicality ON hartonomous.Relation(PhysicalityId);
