-- ==============================================================================
-- AtomMetadata: Full UCD properties for each codepoint
-- ==============================================================================
-- This table stores ALL Unicode Character Database properties from ucd.all.flat.xml
-- One row per codepoint (up to 1,114,112 codepoints)

CREATE TABLE IF NOT EXISTS AtomMetadata (
    -- Primary key references Atom
    AtomId UUID PRIMARY KEY REFERENCES Atom(Id) ON DELETE CASCADE,
    Codepoint INT NOT NULL,  -- Denormalized for fast lookups

    -- Core properties (from UCD XML)
    Name VARCHAR(255),
    Name1 VARCHAR(255),                  -- Unicode 1.0 name
    GeneralCategory VARCHAR(10),         -- gc: Lu, Ll, Lt, Lm, Lo, Mn, Mc, Me, Nd, etc.
    CombiningClass SMALLINT DEFAULT 0,   -- ccc: 0-254
    Script VARCHAR(50),                  -- sc: Latin, Greek, Cyrillic, Han, etc.
    ScriptExtensions VARCHAR(255),       -- scx: semicolon-separated
    Block VARCHAR(100),                  -- blk: Basic Latin, Latin Extended-A, etc.
    Age VARCHAR(20),                     -- Unicode version: 1.1, 2.0, etc.

    -- Decomposition
    DecompositionType VARCHAR(20),       -- dt: can, com, font, noBreak, etc.
    DecompositionMapping TEXT,           -- dm: hex codepoints space-separated

    -- Case mappings (hex codepoints)
    UppercaseMapping VARCHAR(50),
    LowercaseMapping VARCHAR(50),
    TitlecaseMapping VARCHAR(50),
    SimpleCaseFolding VARCHAR(50),
    CaseFolding TEXT,

    -- Numeric
    NumericType VARCHAR(20),             -- nt: None, De, Di, Nu
    NumericValue VARCHAR(50),            -- nv: actual numeric value or NaN

    -- Bidi
    BidiClass VARCHAR(10),               -- bc: L, R, AL, EN, ES, etc.
    BidiMirrored BOOLEAN DEFAULT FALSE,
    BidiMirroringGlyph VARCHAR(20),
    BidiControl BOOLEAN DEFAULT FALSE,
    BidiPairedBracketType VARCHAR(5),
    BidiPairedBracket VARCHAR(20),

    -- Joining (Arabic, etc.)
    JoiningType VARCHAR(5),              -- jt: U, C, D, L, R, T
    JoiningGroup VARCHAR(50),
    JoinControl BOOLEAN DEFAULT FALSE,

    -- Width and Breaking
    EastAsianWidth VARCHAR(5),           -- ea: N, A, H, W, F, Na
    LineBreak VARCHAR(10),               -- lb: BK, CR, LF, CM, etc.
    WordBreak VARCHAR(10),               -- WB: ALetter, Numeric, etc.
    SentenceBreak VARCHAR(10),           -- SB: ATerm, Close, etc.
    GraphemeClusterBreak VARCHAR(10),    -- GCB: CR, LF, Control, etc.
    IndicSyllabicCategory VARCHAR(30),
    IndicPositionalCategory VARCHAR(30),
    VerticalOrientation VARCHAR(5),

    -- Hangul
    HangulSyllableType VARCHAR(10),      -- hst: L, V, T, LV, LVT, NA
    JamoShortName VARCHAR(10),

    -- Boolean Properties (packed as bitfield for efficiency)
    -- Group 1: Character types
    IsAlphabetic BOOLEAN DEFAULT FALSE,
    IsUppercase BOOLEAN DEFAULT FALSE,
    IsLowercase BOOLEAN DEFAULT FALSE,
    IsCased BOOLEAN DEFAULT FALSE,
    IsMath BOOLEAN DEFAULT FALSE,
    IsHexDigit BOOLEAN DEFAULT FALSE,
    IsAsciiHexDigit BOOLEAN DEFAULT FALSE,
    IsIdeographic BOOLEAN DEFAULT FALSE,
    IsUnifiedIdeograph BOOLEAN DEFAULT FALSE,
    IsRadical BOOLEAN DEFAULT FALSE,

    -- Group 2: Punctuation/Symbol
    IsDash BOOLEAN DEFAULT FALSE,
    IsWhitespace BOOLEAN DEFAULT FALSE,
    IsQuotationMark BOOLEAN DEFAULT FALSE,
    IsTerminalPunctuation BOOLEAN DEFAULT FALSE,
    IsSentenceTerminal BOOLEAN DEFAULT FALSE,
    IsDiacritic BOOLEAN DEFAULT FALSE,
    IsExtender BOOLEAN DEFAULT FALSE,
    IsSoftDotted BOOLEAN DEFAULT FALSE,

    -- Group 3: Special status
    IsDeprecated BOOLEAN DEFAULT FALSE,
    IsDefaultIgnorable BOOLEAN DEFAULT FALSE,
    IsVariationSelector BOOLEAN DEFAULT FALSE,
    IsNoncharacter BOOLEAN DEFAULT FALSE,
    IsPatternWhitespace BOOLEAN DEFAULT FALSE,
    IsPatternSyntax BOOLEAN DEFAULT FALSE,

    -- Group 4: Grapheme properties
    IsGraphemeBase BOOLEAN DEFAULT FALSE,
    IsGraphemeExtend BOOLEAN DEFAULT FALSE,
    IsIdStart BOOLEAN DEFAULT FALSE,
    IsIdContinue BOOLEAN DEFAULT FALSE,
    IsXidStart BOOLEAN DEFAULT FALSE,
    IsXidContinue BOOLEAN DEFAULT FALSE,

    -- Group 5: Normalization
    CompositionExclusion BOOLEAN DEFAULT FALSE,
    FullCompositionExclusion BOOLEAN DEFAULT FALSE,
    NfcQuickCheck VARCHAR(5),
    NfdQuickCheck VARCHAR(5),
    NfkcQuickCheck VARCHAR(5),
    NfkdQuickCheck VARCHAR(5),
    NfkcCasefold TEXT,

    -- Group 6: Case changes
    ChangesWhenLowercased BOOLEAN DEFAULT FALSE,
    ChangesWhenUppercased BOOLEAN DEFAULT FALSE,
    ChangesWhenTitlecased BOOLEAN DEFAULT FALSE,
    ChangesWhenCasefolded BOOLEAN DEFAULT FALSE,
    ChangesWhenCasemapped BOOLEAN DEFAULT FALSE,
    ChangesWhenNfkcCasefolded BOOLEAN DEFAULT FALSE,

    -- Group 7: Emoji
    IsEmoji BOOLEAN DEFAULT FALSE,
    IsEmojiPresentation BOOLEAN DEFAULT FALSE,
    IsEmojiModifier BOOLEAN DEFAULT FALSE,
    IsEmojiModifierBase BOOLEAN DEFAULT FALSE,
    IsEmojiComponent BOOLEAN DEFAULT FALSE,
    IsExtendedPictographic BOOLEAN DEFAULT FALSE,

    -- Group 8: Other
    PrependedConcatenationMark BOOLEAN DEFAULT FALSE,
    RegionalIndicator BOOLEAN DEFAULT FALSE,

    -- Han-specific
    Radical SMALLINT,
    Strokes SMALLINT,

    -- UCA (DUCET) - store as JSONB for flexibility
    UcaWeights JSONB,

    -- Name aliases as JSONB array
    NameAliases JSONB,

    -- Extra properties not modeled above (extensibility)
    ExtraProperties JSONB,

    -- Timestamps
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Indexes for common queries
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Codepoint ON AtomMetadata(Codepoint);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_GeneralCategory ON AtomMetadata(GeneralCategory);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Script ON AtomMetadata(Script);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Block ON AtomMetadata(Block);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Age ON AtomMetadata(Age);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_NumericType ON AtomMetadata(NumericType) WHERE NumericType != 'None';
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Radical ON AtomMetadata(Radical) WHERE Radical IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Emoji ON AtomMetadata(IsEmoji) WHERE IsEmoji = TRUE;
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Alphabetic ON AtomMetadata(IsAlphabetic) WHERE IsAlphabetic = TRUE;

-- Comments
COMMENT ON TABLE AtomMetadata IS 'Full Unicode Character Database (UCD) properties for each atom/codepoint';
COMMENT ON COLUMN AtomMetadata.UcaWeights IS 'DUCET collation weights as JSON array of {primary, secondary, tertiary, quaternary}';
COMMENT ON COLUMN AtomMetadata.NameAliases IS 'Name aliases as JSON array of {alias, type}';
COMMENT ON COLUMN AtomMetadata.ExtraProperties IS 'Additional UCD properties not explicitly modeled (Unihan, etc.)';
