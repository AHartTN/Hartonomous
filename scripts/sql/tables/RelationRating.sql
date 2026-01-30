-- ==============================================================================
-- RelationRating: The ELO rating of a Relation based on ingestion, user feedback, and system evaluations
-- ==============================================================================

CREATE TABLE IF NOT EXISTS RelationRating (
    RelationId UUID PRIMARY KEY REFERENCES Relation(Id) ON DELETE CASCADE,
    Observations UINT64 DEFAULT 1 NOT NULL,
    RatingValue DOUBLE PRECISION NOT NULL DEFAULT 1000,
    KFactor DOUBLE PRECISION DEFAULT 1.0 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationRating_RatingValue ON RelationRating(RatingValue);

COMMENT ON TABLE RelationRating IS 'Stores the ELO rating of a Relation based on various factors including ingestion quality, user feedback, and system evaluations.';
COMMENT ON COLUMN RelationRating.RelationId IS 'The unique identifier of the Relation being rated.';
COMMENT ON COLUMN RelationRating.RatingValue IS 'The ELO rating value representing the quality of the Relation.';
COMMENT ON COLUMN RelationRating.Observations IS 'The number of observations or evaluations that have contributed to the current rating.';
COMMENT ON COLUMN RelationRating.KFactor IS 'The K-factor used in the ELO rating calculation, influencing the sensitivity of rating changes.';
COMMENT ON COLUMN RelationRating.CreatedAt IS 'Timestamp when the rating record was created.';
COMMENT ON COLUMN RelationRating.ModifiedAt IS 'Timestamp when the rating record was last modified.';
COMMENT ON COLUMN RelationRating.ValidatedAt IS 'Timestamp when the rating was last validated.';