#!/usr/bin/env bash
# drop-substrate-indexes.sh
# Nuclear optimization for massive bulk ingestion.

set -e
DB_NAME="hartonomous"
PSQL="psql -v ON_ERROR_STOP=1 -d $DB_NAME"

echo "[OPTIMIZE] Disabling constraints and dropping indexes for substrate reinforcement..."

$PSQL <<EOF
SET session_replication_role = 'replica';
ALTER TABLE hartonomous.physicality DROP CONSTRAINT IF EXISTS physicality_centroid_normalized;

-- Physicality
DROP INDEX IF EXISTS hartonomous.idx_physicality_hilbert;
DROP INDEX IF EXISTS hartonomous.idx_physicality_centroid;
DROP INDEX IF EXISTS hartonomous.idx_physicality_trajectory;

-- Composition
DROP INDEX IF EXISTS hartonomous.idx_composition_physicality;
DROP INDEX IF EXISTS hartonomous.idx_composition_createdat;
DROP INDEX IF EXISTS hartonomous.idx_composition_modifiedat;
DROP INDEX IF EXISTS hartonomous.idx_composition_validatedat;

-- CompositionSequence
DROP INDEX IF EXISTS hartonomous.uq_compositionsequence_compositionid_ordinal;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_compositionid;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_atomid;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_ordinal;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_occurrences;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_createdat;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_modifiedat;
DROP INDEX IF EXISTS hartonomous.idx_compositionsequence_validatedat;

-- Relation
DROP INDEX IF EXISTS hartonomous.idx_relation_physicality;

-- RelationSequence
DROP INDEX IF EXISTS hartonomous.uq_relationsequence_relationid_ordinal;
DROP INDEX IF EXISTS hartonomous.idx_relationsequence_relationid;
DROP INDEX IF EXISTS hartonomous.idx_relationsequence_compositionid;
DROP INDEX IF EXISTS hartonomous.idx_relationsequence_createdat;
DROP INDEX IF EXISTS hartonomous.idx_relationsequence_modifiedat;
DROP INDEX IF EXISTS hartonomous.idx_relationsequence_validatedat;

-- RelationRating
DROP INDEX IF EXISTS hartonomous.idx_relationrating_ratingvalue;

-- RelationEvidence
DROP INDEX IF EXISTS hartonomous.idx_relationevidence_sourcerating;
EOF

echo "[OPTIMIZE] Substrate is now 'soft' and ready for high-speed COPY."
