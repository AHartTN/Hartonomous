#!/usr/bin/env bash
# Script: Run Queries (Test semantic search on current schema)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_step() { echo -e "${YELLOW}>>> $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hartonomous}

print_info "=== Running Semantic Queries ==="

# Query 1: Database statistics
print_step "Query 1: Database statistics"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT
    'Atom' AS table_name,
    COUNT(*) AS row_count,
    pg_size_pretty(pg_total_relation_size('hartonomous.atom')) AS total_size
FROM hartonomous.atom
UNION ALL
SELECT
    'Composition',
    COUNT(*),
    pg_size_pretty(pg_total_relation_size('hartonomous.composition'))
FROM hartonomous.composition
UNION ALL
SELECT
    'Relation',
    COUNT(*),
    pg_size_pretty(pg_total_relation_size('hartonomous.relation'))
FROM hartonomous.relation
UNION ALL
SELECT
    'RelationRating',
    COUNT(*),
    pg_size_pretty(pg_total_relation_size('hartonomous.relationrating'))
FROM hartonomous.relationrating;
EOF

# Query 2: Find a composition by text
print_step "Query 2: Find composition 'whale'"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT composition_id, reconstructed_text
FROM hartonomous.v_composition_text
WHERE LOWER(reconstructed_text) = 'whale'
LIMIT 5;
EOF

# Query 3: Find related compositions via co-occurrence
print_step "Query 3: Compositions co-occurring with 'whale'"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
WITH whale AS (
    SELECT composition_id FROM hartonomous.v_composition_text
    WHERE LOWER(reconstructed_text) = 'whale' LIMIT 1
),
whale_relations AS (
    SELECT DISTINCT rs.relationid
    FROM hartonomous.relationsequence rs, whale w
    WHERE rs.compositionid = w.composition_id
),
cooccurring AS (
    SELECT
        rs.compositionid,
        COUNT(DISTINCT wr.relationid) AS co_occurrence_count
    FROM whale_relations wr
    JOIN hartonomous.relationsequence rs ON rs.relationid = wr.relationid
    WHERE rs.compositionid != (SELECT composition_id FROM whale)
    GROUP BY rs.compositionid
    ORDER BY co_occurrence_count DESC
    LIMIT 10
)
SELECT v.reconstructed_text, co.co_occurrence_count
FROM cooccurring co
JOIN hartonomous.v_composition_text v ON v.composition_id = co.compositionid;
EOF

# Query 4: Top-rated relations
print_step "Query 4: Top-rated relations"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT
    rr.relationid,
    rr.ratingvalue,
    uint64_to_double(rr.observations) AS observations
FROM hartonomous.relationrating rr
ORDER BY rr.ratingvalue DESC
LIMIT 10;
EOF

print_success "Queries complete!"
