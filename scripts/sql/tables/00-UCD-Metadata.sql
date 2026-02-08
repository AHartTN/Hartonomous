-- ==============================================================================
-- Unicode Gene Pool (UCD/UCA Metadata)
-- ==============================================================================

CREATE SCHEMA IF NOT EXISTS ucd;

CREATE TABLE IF NOT EXISTS ucd.code_points (
    Codepoint INT PRIMARY KEY,
    Name TEXT,
    GeneralCategory CHAR(2),
    Block TEXT,
    Age TEXT,
    Script TEXT,
    Properties JSONB,
    BaseCodepoint INT,
    Radical INT,
    Strokes INT
);

CREATE TABLE IF NOT EXISTS ucd.collation_weights (
    SourceCodepoints INT[],
    PrimaryWeight INT,
    SecondaryWeight INT,
    TertiaryWeight INT,
    PRIMARY KEY (SourceCodepoints)
);

CREATE INDEX IF NOT EXISTS idx_ucd_gc ON ucd.code_points(GeneralCategory);
CREATE INDEX IF NOT EXISTS idx_ucd_script ON ucd.code_points(Script);

COMMENT ON SCHEMA ucd IS 'Unicode Character Database (Gene Pool) for semantic substrate grounding';