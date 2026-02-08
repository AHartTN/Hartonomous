#!/usr/bin/env bash
# rebuild-substrate-indexes.sh
# Finalize substrate reinforcement by rebuilding all indices.

set -e
DB_NAME="hartonomous"
PSQL="psql -v ON_ERROR_STOP=1 -d $DB_NAME"

echo "[OPTIMIZE] Rebuilding substrate indexes (this may take several minutes)..."

$PSQL <<EOF
-- Physicality
CREATE INDEX idx_physicality_hilbert ON hartonomous.physicality(hilbert);
CREATE INDEX idx_physicality_centroid ON hartonomous.physicality USING GIST(centroid gist_geometry_ops_nd);
CREATE INDEX idx_physicality_trajectory ON hartonomous.physicality USING GIST(trajectory gist_geometry_ops_nd);
ALTER TABLE hartonomous.physicality ADD CONSTRAINT physicality_centroid_normalized 
CHECK (ABS(ST_X(centroid)*ST_X(centroid) + ST_Y(centroid)*ST_Y(centroid) + ST_Z(centroid)*ST_Z(centroid) + ST_M(centroid)*ST_M(centroid) - 1.0) < 0.0001) NOT VALID;

-- Composition
CREATE INDEX idx_composition_physicality ON hartonomous.composition(physicalityid);
CREATE INDEX idx_composition_createdat ON hartonomous.composition(createdat);
CREATE INDEX idx_composition_modifiedat ON hartonomous.composition(modifiedat);
CREATE INDEX idx_composition_validatedat ON hartonomous.composition(validatedat);

-- CompositionSequence
CREATE UNIQUE INDEX uq_compositionsequence_compositionid_ordinal ON hartonomous.compositionsequence(compositionid, ordinal);
CREATE INDEX idx_compositionsequence_compositionid ON hartonomous.compositionsequence(compositionid);
CREATE INDEX idx_compositionsequence_atomid ON hartonomous.compositionsequence(atomid);
CREATE INDEX idx_compositionsequence_ordinal ON hartonomous.compositionsequence(ordinal);
CREATE INDEX idx_compositionsequence_occurrences ON hartonomous.compositionsequence(occurrences);
CREATE INDEX idx_compositionsequence_createdat ON hartonomous.compositionsequence(createdat);
CREATE INDEX idx_compositionsequence_modifiedat ON hartonomous.compositionsequence(modifiedat);
CREATE INDEX idx_compositionsequence_validatedat ON hartonomous.compositionsequence(validatedat);

-- Relation
CREATE INDEX idx_relation_physicality ON hartonomous.relation(physicalityid);

-- RelationSequence
CREATE UNIQUE INDEX uq_relationsequence_relationid_ordinal ON hartonomous.relationsequence(relationid, ordinal);
CREATE INDEX idx_relationsequence_relationid ON hartonomous.relationsequence(relationid, ordinal ASC, occurrences);
CREATE INDEX idx_relationsequence_compositionid ON hartonomous.relationsequence(compositionid, relationid);
CREATE INDEX idx_relationsequence_createdat ON hartonomous.relationsequence(createdat);
CREATE INDEX idx_relationsequence_modifiedat ON hartonomous.relationsequence(modifiedat);
CREATE INDEX idx_relationsequence_validatedat ON hartonomous.relationsequence(validatedat);

-- RelationRating
CREATE INDEX idx_relationrating_ratingvalue ON hartonomous.relationrating(ratingvalue);

-- RelationEvidence
CREATE INDEX idx_relationevidence_sourcerating ON hartonomous.relationevidence(sourcerating);

-- Finalize
SET session_replication_role = 'origin';
ANALYZE hartonomous.physicality;
ANALYZE hartonomous.composition;
ANALYZE hartonomous.relation;
EOF

echo "[OPTIMIZE] Substrate indices rebuilt and statistics updated."
