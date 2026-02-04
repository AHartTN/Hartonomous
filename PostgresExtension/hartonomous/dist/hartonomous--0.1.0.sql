-- Generated from sql/hartonomous--0.1.0.sql
-- hartonomous--0.1.0.sql
-- This file should include only project schema SQL that belongs to your semantic substrate.

-- Use the schema for includes
-- Including domains/uint16.sql
CREATE DOMAIN hartonomous.uint16 AS integer
    CHECK (VALUE >= 0 AND VALUE <= 65535);

-- Including domains/uint32.sql
CREATE DOMAIN hartonomous.uint32 AS integer
    CHECK (VALUE >= 0 AND VALUE <= 4294967295);

-- Including domains/uint64.sql
CREATE DOMAIN hartonomous.uint64 AS numeric(20,0)
    CHECK (VALUE >= 0 AND VALUE < 18446744073709551616);

-- Including domains/uint128.sql
CREATE DOMAIN hartonomous.uint128 AS numeric(39,0)
    CHECK (VALUE >= 0 AND VALUE < 340282366920938463463374607431768211456);

-- Including domains/hilbert128.sql
CREATE DOMAIN hartonomous.hilbert128 AS numeric(39,0);

-- Including uint64_ops.sql
-- ==============================================================================
-- UINT64 Native Operators
-- ==============================================================================

CREATE OR REPLACE FUNCTION uint64_add(hartonomous.uint64, hartonomous.uint64)
RETURNS hartonomous.uint64
AS 'MODULE_PATHNAME', 'uint64_add'
LANGUAGE C IMMUTABLE STRICT;

CREATE OR REPLACE FUNCTION uint64_to_double(hartonomous.uint64)
RETURNS DOUBLE PRECISION
AS 'MODULE_PATHNAME', 'uint64_to_double'
LANGUAGE C IMMUTABLE STRICT;

CREATE OPERATOR + (
    LEFTARG = hartonomous.uint64,
    RIGHTARG = hartonomous.uint64,
    PROCEDURE = uint64_add
);

-- Weighted average using native types
CREATE OR REPLACE FUNCTION hartonomous.weighted_elo_update(
    old_elo DOUBLE PRECISION, 
    old_obs hartonomous.uint64, 
    new_elo DOUBLE PRECISION, 
    new_obs hartonomous.uint64
)
RETURNS DOUBLE PRECISION
LANGUAGE SQL IMMUTABLE AS $$
    SELECT (old_elo * uint64_to_double(old_obs) + new_elo * uint64_to_double(new_obs)) / 
           (uint64_to_double(old_obs) + uint64_to_double(new_obs));
$$;


-- Core Schema
-- Including tables/tenant.sql
CREATE TABLE IF NOT EXISTS hartonomous.Tenant (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Name VARCHAR(255) NOT NULL UNIQUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    IsActive BOOLEAN DEFAULT TRUE,
    MaxStorageGB INTEGER DEFAULT 100,
    MaxCompositions INTEGER DEFAULT 10000000,
    MaxRelations INTEGER DEFAULT 1000000,
    SubscriptionTier VARCHAR(50) DEFAULT 'free',
    BillingEmail VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS idx_tenants_active ON hartonomous.Tenant(IsActive);

-- Including tables/user.sql
CREATE TABLE IF NOT EXISTS hartonomous."User" (
    Id UUID PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES hartonomous.Tenant(Id) ON DELETE CASCADE,
    Username VARCHAR(50) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Role VARCHAR(20) NOT NULL DEFAULT 'user',
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    LastLoginAt TIMESTAMP WITH TIME ZONE,
    UNIQUE(TenantId, Username)
);

CREATE INDEX IF NOT EXISTS idx_users_tenant ON hartonomous."User"(TenantId);
CREATE INDEX IF NOT EXISTS idx_users_active ON hartonomous."User"(IsActive);

-- Including tables/content.sql
CREATE TABLE IF NOT EXISTS hartonomous.Content (
    Id UUID PRIMARY KEY,
    TenantId UUID NOT NULL,
    UserId UUID NOT NULL,
    ContentType UINT16 NOT NULL,
    ContentHash BYTEA NOT NULL UNIQUE,
    ContentSize UINT64 NOT NULL,
    ContentMimeType VARCHAR(100),
    ContentLanguage VARCHAR(20),
    ContentSource VARCHAR(255),
    ContentEncoding VARCHAR(50),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Content_ContentType ON hartonomous.Content(ContentType);
CREATE INDEX IF NOT EXISTS idx_Content_TenantId ON hartonomous.Content(TenantId, UserId);
CREATE INDEX IF NOT EXISTS idx_Content_UserId ON hartonomous.Content(UserId);
CREATE INDEX IF NOT EXISTS idx_Content_ContentHash ON hartonomous.Content(ContentHash);

-- Including tables/physicality.sql
CREATE TABLE IF NOT EXISTS hartonomous.Physicality (
    Id UUID PRIMARY KEY,
    Hilbert hartonomous.UINT128 NOT NULL,
    Centroid GEOMETRY(POINTZM, 0) NOT NULL,
    Trajectory GEOMETRY(GEOMETRYZM, 0),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT Physicality_Centroid_Normalized CHECK (ABS(ST_X(Centroid) * ST_X(Centroid) + ST_Y(Centroid) * ST_Y(Centroid) + ST_Z(Centroid) * ST_Z(Centroid) + ST_M(Centroid) * ST_M(Centroid) - 1.0) < 0.0001)
);

CREATE INDEX idx_Physicality_hilbert ON hartonomous.Physicality(Hilbert);
CREATE INDEX IF NOT EXISTS idx_Physicality_Centroid ON hartonomous.Physicality USING GIST(Centroid gist_geometry_ops_nd);
CREATE INDEX IF NOT EXISTS idx_Physicality_Trajectory ON hartonomous.Physicality USING GIST(Trajectory public.gist_geometry_ops_nd);
-- Including tables/atom.sql
CREATE TABLE IF NOT EXISTS hartonomous.Atom (
    Id UUID PRIMARY KEY,
    Codepoint INT NOT NULL UNIQUE,
    PhysicalityId UUID NOT NULL REFERENCES hartonomous.Physicality(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Atom_Codepoint ON hartonomous.Atom(Codepoint);
CREATE INDEX IF NOT EXISTS idx_Atom_Physicality ON hartonomous.Atom(PhysicalityId);

-- Including tables/atom_metadata.sql
-- ==============================================================================
-- AtomMetadata: Full UCD properties for each codepoint
-- ==============================================================================
-- This table stores ALL Unicode Character Database properties from ucd.all.flat.xml
-- One row per codepoint (up to 1,114,112 codepoints)

CREATE TABLE IF NOT EXISTS hartonomous.AtomMetadata (
    -- Primary key references Atom
    AtomId UUID PRIMARY KEY REFERENCES hartonomous.Atom(Id) ON DELETE CASCADE,
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
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Codepoint ON hartonomous.AtomMetadata(Codepoint);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_GeneralCategory ON hartonomous.AtomMetadata(GeneralCategory);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Script ON hartonomous.AtomMetadata(Script);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Block ON hartonomous.AtomMetadata(Block);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Age ON hartonomous.AtomMetadata(Age);
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_NumericType ON hartonomous.AtomMetadata(NumericType) WHERE NumericType != 'None';
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Radical ON hartonomous.AtomMetadata(Radical) WHERE Radical IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Emoji ON hartonomous.AtomMetadata(IsEmoji) WHERE IsEmoji = TRUE;
CREATE INDEX IF NOT EXISTS idx_AtomMetadata_Alphabetic ON hartonomous.AtomMetadata(IsAlphabetic) WHERE IsAlphabetic = TRUE;

-- Comments
COMMENT ON TABLE hartonomous.AtomMetadata IS 'Full Unicode Character Database (UCD) properties for each atom/codepoint';
COMMENT ON COLUMN hartonomous.AtomMetadata.UcaWeights IS 'DUCET collation weights as JSON array of {primary, secondary, tertiary, quaternary}';
COMMENT ON COLUMN hartonomous.AtomMetadata.NameAliases IS 'Name aliases as JSON array of {alias, type}';
COMMENT ON COLUMN hartonomous.AtomMetadata.ExtraProperties IS 'Additional UCD properties not explicitly modeled (Unihan, etc.)';

-- Including tables/composition.sql
CREATE TABLE IF NOT EXISTS hartonomous.Composition (
    Id UUID PRIMARY KEY,
    PhysicalityId UUID NOT NULL REFERENCES hartonomous.Physicality(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Composition_Physicality ON hartonomous.Composition(PhysicalityId);

-- Including tables/composition_sequence.sql
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

-- Including tables/relation.sql
CREATE TABLE IF NOT EXISTS hartonomous.Relation (
    Id UUID PRIMARY KEY,
    PhysicalityId UUID NOT NULL REFERENCES hartonomous.Physicality(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_Relation_Physicality ON hartonomous.Relation(PhysicalityId);

-- Including tables/relation_sequence.sql
CREATE TABLE IF NOT EXISTS hartonomous.RelationSequence (
    Id UUID PRIMARY KEY,
    RelationId UUID NOT NULL REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,
    CompositionId UUID NOT NULL REFERENCES hartonomous.Composition(Id) ON DELETE CASCADE,
    Ordinal UINT32 NOT NULL,
    Occurrences UINT32 DEFAULT 1 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_RelationSequence_RelationId_Ordinal ON hartonomous.RelationSequence(RelationId, Ordinal);
CREATE INDEX IF NOT EXISTS idx_RelationSequence_RelationId ON hartonomous.RelationSequence(RelationId, Ordinal ASC, Occurrences);

-- CRITICAL: Index for finding all relations a composition participates in (microsecond walks)
CREATE INDEX IF NOT EXISTS idx_RelationSequence_CompositionId ON hartonomous.RelationSequence(CompositionId);

-- Including tables/relation_rating.sql
-- ==============================================================================
-- RelationRating: Dual-ELO System for Gravitational Truth
-- ==============================================================================

CREATE TABLE IF NOT EXISTS hartonomous.RelationRating (
    RelationId UUID PRIMARY KEY REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,

    -- Consensus ELO (Quantity/Frequency): Increments logarithmically with every duplicate insertion.
    ConsensusElo DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    
    -- Base ELO (Quality): Assigned at ingestion based on source authority/reputation.
    BaseElo DOUBLE PRECISION NOT NULL DEFAULT 1500.0,

    -- Total observations count
    Observations UINT64 DEFAULT '\x0000000000000001'::bytea NOT NULL,
    
    -- Sensitivity factor for updates
    KFactor DOUBLE PRECISION DEFAULT 32.0 NOT NULL,

    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationRating_Consensus ON hartonomous.RelationRating(ConsensusElo);
CREATE INDEX IF NOT EXISTS idx_RelationRating_Base ON hartonomous.RelationRating(BaseElo);

COMMENT ON TABLE hartonomous.RelationRating IS 'Stores the Dual-ELO rating representing the quality (Base) and consensus (Consensus) of a Relation.';
-- Including tables/relation_evidence.sql
CREATE TABLE IF NOT EXISTS hartonomous.RelationEvidence (
    Id UUID PRIMARY KEY,
    ContentId UUID NOT NULL REFERENCES hartonomous.Content(Id) ON DELETE CASCADE,
    RelationId UUID NOT NULL REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,
    IsValid BOOLEAN DEFAULT TRUE NOT NULL,
    SourceRating DOUBLE PRECISION NOT NULL DEFAULT 1000,
    SignalStrength DOUBLE PRECISION NOT NULL DEFAULT 1.0 CHECK (SignalStrength BETWEEN 0 AND 1),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Including tables/audit_log.sql
CREATE TABLE IF NOT EXISTS hartonomous.AuditLog (
    Id BIGSERIAL PRIMARY KEY,
    TenantId UUID NOT NULL REFERENCES hartonomous.Tenant(Id),
    UserId UUID REFERENCES hartonomous."User"(Id),
    ActionType VARCHAR(50) NOT NULL,
    ContentHash BYTEA,
    ContentType VARCHAR(20),
    ActionDetails JSONB DEFAULT '{}'::JSONB,
    IPAddress INET,
    UserAgent TEXT,
    ActionResult VARCHAR(20) DEFAULT 'success',
    ErrorMessage TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_AuditLog_Tenant ON hartonomous.AuditLog (TenantId, CreatedAt DESC);
CREATE INDEX idx_AuditLog_User ON hartonomous.AuditLog (UserId, CreatedAt DESC);

-- Diagnostic Views
-- Including views/v_promoted_units.sql
CREATE OR REPLACE VIEW hartonomous.v_promoted_units AS
SELECT 
    c.Id,
    p.Hilbert,
    am.GeneralCategory,
    am.Block,
    am.Script
FROM hartonomous.Composition c
JOIN hartonomous.Physicality p ON c.PhysicalityId = p.Id
LEFT JOIN hartonomous.CompositionSequence cs ON c.Id = cs.CompositionId AND cs.Ordinal = 0
LEFT JOIN hartonomous.Atom a ON cs.AtomId = a.Id
LEFT JOIN hartonomous.AtomMetadata am ON a.Id = am.AtomId
ORDER BY c.CreatedAt DESC;
-- Including views/v_semantic_neighbors.sql
CREATE OR REPLACE VIEW hartonomous.v_semantic_neighbors AS
SELECT 
    r.Id as Relation_Id,
    rr.RatingValue as ELO,
    rr.Observations,
    p1.Hilbert as Source_Hilbert,
    p2.Hilbert as Neighbor_Hilbert
FROM hartonomous.Relation r
JOIN hartonomous.RelationSequence rs1 ON r.Id = rs1.RelationId AND rs1.Ordinal = 0
JOIN hartonomous.RelationSequence rs2 ON r.Id = rs2.RelationId AND rs2.Ordinal = 1
JOIN hartonomous.Composition c1 ON rs1.CompositionId = c1.Id
JOIN hartonomous.Physicality p1 ON c1.PhysicalityId = p1.Id
JOIN hartonomous.Composition c2 ON rs2.CompositionId = c2.Id
JOIN hartonomous.Physicality p2 ON c2.PhysicalityId = p2.Id
JOIN hartonomous.RelationRating rr ON r.Id = rr.RelationId
ORDER BY rr.RatingValue DESC;

-- Including views/v_orphan_atoms.sql
CREATE OR REPLACE VIEW hartonomous.v_orphan_atoms AS
SELECT 
    a.Codepoint,
    am.Name,
    am.Block,
    am.Script
FROM hartonomous.Atom a
JOIN hartonomous.AtomMetadata am ON a.Id = am.AtomId
LEFT JOIN hartonomous.CompositionSequence cs ON a.Id = cs.AtomId
WHERE cs.Id IS NULL;

