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

-- Including tables/relation_rating.sql
CREATE TABLE IF NOT EXISTS hartonomous.RelationRating (
    RelationId UUID PRIMARY KEY REFERENCES hartonomous.Relation(Id) ON DELETE CASCADE,
    Observations UINT64 DEFAULT 1 NOT NULL,
    RatingValue DOUBLE PRECISION NOT NULL DEFAULT 1000,
    KFactor DOUBLE PRECISION DEFAULT 1.0 NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ModifiedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ValidatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_RelationRating_RatingValue ON hartonomous.RelationRating(RatingValue);

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
