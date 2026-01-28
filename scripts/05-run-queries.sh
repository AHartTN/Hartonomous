#!/usr/bin/env bash
# Script 5: Run Queries (Test semantic search)

set -e

source "$(dirname "$0")/common.sh"

print_header "Running Semantic Queries"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

# Check PostgreSQL
if ! check_postgres; then
    exit 1
fi

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hypercube}

print_success "Connected to PostgreSQL: $PGDATABASE"

# Query 1: Find composition by text
print_step "Query 1: Find composition 'Captain'"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT text, hash FROM compositions WHERE LOWER(text) = 'captain';
EOF

# Query 2: Find related compositions
print_step "Query 2: Find compositions related to 'Captain'"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
-- Find all relations containing "Captain"
WITH captain_relations AS (
    SELECT DISTINCT rc.relation_hash
    FROM relation_children rc
    JOIN compositions c ON rc.child_hash = c.hash
    WHERE LOWER(c.text) = 'captain'
),
-- Find all compositions in those relations
cooccurring_compositions AS (
    SELECT
        c.text,
        COUNT(DISTINCT cr.relation_hash) AS co_occurrence_count
    FROM captain_relations cr
    JOIN relation_children rc ON rc.relation_hash = cr.relation_hash
    JOIN compositions c ON c.hash = rc.child_hash
    WHERE LOWER(c.text) != 'captain'
    GROUP BY c.text
    ORDER BY co_occurrence_count DESC
    LIMIT 10
)
SELECT * FROM cooccurring_compositions;
EOF

# Query 3: Answer question
print_step "Query 3: What is the captain's name?"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
-- Find compositions related to both "captain" and proper nouns
WITH captain_related AS (
    SELECT DISTINCT rc.relation_hash
    FROM relation_children rc
    JOIN compositions c ON rc.child_hash = c.hash
    WHERE LOWER(c.text) = 'captain'
),
potential_answers AS (
    SELECT
        c.text,
        COUNT(DISTINCT cr.relation_hash) AS relevance,
        -- Heuristic: Capitalized words are likely proper nouns (names)
        CASE WHEN c.text ~ '^[A-Z][a-z]+$' THEN 2 ELSE 1 END AS name_boost
    FROM captain_related cr
    JOIN relation_children rc ON rc.relation_hash = cr.relation_hash
    JOIN compositions c ON c.hash = rc.child_hash
    WHERE LOWER(c.text) != 'captain'
      AND LENGTH(c.text) > 2
    GROUP BY c.text
)
SELECT
    text AS answer,
    relevance * name_boost AS confidence_score
FROM potential_answers
ORDER BY confidence_score DESC
LIMIT 5;
EOF

# Query 4: Full context
print_step "Query 4: Show relations containing 'Captain'"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
-- Show full context of relations containing "Captain"
WITH captain_relations AS (
    SELECT DISTINCT r.hash, r.level, r.length
    FROM relations r
    JOIN relation_children rc ON rc.relation_hash = r.hash
    JOIN compositions c ON c.hash = rc.child_hash
    WHERE LOWER(c.text) = 'captain'
)
SELECT
    cr.hash,
    cr.level,
    cr.length,
    -- Aggregate all compositions in this relation
    string_agg(c.text, ' ' ORDER BY rc.position) AS full_text
FROM captain_relations cr
JOIN relation_children rc ON rc.relation_hash = cr.hash
JOIN compositions c ON c.hash = rc.child_hash
GROUP BY cr.hash, cr.level, cr.length
LIMIT 5;
EOF

# Query 5: Statistics
print_step "Query 5: Database statistics"

psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << 'EOF'
SELECT
    'Atoms' AS table_name,
    COUNT(*) AS row_count,
    pg_size_pretty(pg_total_relation_size('atoms')) AS total_size
FROM atoms
UNION ALL
SELECT
    'Compositions',
    COUNT(*),
    pg_size_pretty(pg_total_relation_size('compositions'))
FROM compositions
UNION ALL
SELECT
    'Relations',
    COUNT(*),
    pg_size_pretty(pg_total_relation_size('relations'))
FROM relations;
EOF

print_complete "Queries complete!"
echo ""
print_info "Key findings:"
print_info "  - Semantic search works via relationship traversal"
print_info "  - Co-occurrence ranking identifies related concepts"
print_info "  - Proper noun detection helps answer 'name' questions"
echo ""
