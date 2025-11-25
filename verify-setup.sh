#!/bin/bash
# ============================================================================
# Hartonomous Setup Verification Script
# Validates installation and runs basic tests
# ============================================================================

set -e

echo "============================================"
echo "Hartonomous Setup Verification"
echo "============================================"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Test counter
TESTS_PASSED=0
TESTS_FAILED=0

# Test function
test_assertion() {
    local description=$1
    local command=$2
    local expected=$3
    
    echo -n "Testing: $description ... "
    
    result=$(eval "$command" 2>&1 || echo "FAILED")
    
    if [[ "$result" == "$expected" ]]; then
        echo -e "${GREEN}? PASS${NC}"
        ((TESTS_PASSED++))
    else
        echo -e "${RED}? FAIL${NC}"
        echo "  Expected: $expected"
        echo "  Got: $result"
        ((TESTS_FAILED++))
    fi
}

# Test database query
test_query() {
    local description=$1
    local query=$2
    local expected=$3
    
    echo -n "Testing: $description ... "
    
    result=$(psql -h localhost -U postgres -d hartonomous -tAc "$query" 2>&1 || echo "FAILED")
    
    if [[ "$result" == "$expected" ]]; then
        echo -e "${GREEN}? PASS${NC}"
        ((TESTS_PASSED++))
    else
        echo -e "${RED}? FAIL${NC}"
        echo "  Expected: $expected"
        echo "  Got: $result"
        ((TESTS_FAILED++))
    fi
}

echo "1. Checking Docker containers..."
echo "============================================"

if ! docker ps | grep -q hartonomous; then
    echo -e "${RED}? Hartonomous container not running${NC}"
    echo "Please run: cd docker && docker-compose up -d"
    exit 1
else
    echo -e "${GREEN}? Hartonomous container running${NC}"
fi

echo ""
echo "2. Checking database connectivity..."
echo "============================================"

if ! pg_isready -h localhost -U postgres -d hartonomous > /dev/null 2>&1; then
    echo -e "${RED}? Cannot connect to database${NC}"
    exit 1
else
    echo -e "${GREEN}? Database connection successful${NC}"
fi

echo ""
echo "3. Checking extensions..."
echo "============================================"

test_query "PostGIS extension" \
    "SELECT COUNT(*) FROM pg_extension WHERE extname = 'postgis'" \
    "1"

test_query "PL/Python extension" \
    "SELECT COUNT(*) FROM pg_extension WHERE extname = 'plpython3u'" \
    "1"

test_query "pg_trgm extension" \
    "SELECT COUNT(*) FROM pg_extension WHERE extname = 'pg_trgm'" \
    "1"

echo ""
echo "4. Checking tables..."
echo "============================================"

test_query "atom table exists" \
    "SELECT COUNT(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = 'atom'" \
    "1"

test_query "atom_composition table exists" \
    "SELECT COUNT(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = 'atom_composition'" \
    "1"

test_query "atom_relation table exists" \
    "SELECT COUNT(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = 'atom_relation'" \
    "1"

echo ""
echo "5. Checking indexes..."
echo "============================================"

test_query "Spatial index exists" \
    "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'idx_atom_spatial'" \
    "1"

test_query "Hash index exists" \
    "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'idx_atom_hash'" \
    "1"

echo ""
echo "6. Checking functions..."
echo "============================================"

test_query "atomize_value function exists" \
    "SELECT COUNT(*) FROM pg_proc WHERE proname = 'atomize_value'" \
    "1"

test_query "atomize_text function exists" \
    "SELECT COUNT(*) FROM pg_proc WHERE proname = 'atomize_text'" \
    "1"

test_query "compute_spatial_position function exists" \
    "SELECT COUNT(*) FROM pg_proc WHERE proname = 'compute_spatial_position'" \
    "1"

test_query "reinforce_synapse function exists" \
    "SELECT COUNT(*) FROM pg_proc WHERE proname = 'reinforce_synapse'" \
    "1"

test_query "run_ooda_cycle function exists" \
    "SELECT COUNT(*) FROM pg_proc WHERE proname = 'run_ooda_cycle'" \
    "1"

echo ""
echo "7. Testing core functionality..."
echo "============================================"

echo "Testing atomization..."
psql -h localhost -U postgres -d hartonomous -c "SELECT atomize_text('TEST');" > /dev/null 2>&1

test_query "Atoms created" \
    "SELECT COUNT(*) FROM atom WHERE metadata->>'modality' = 'character' AND canonical_text IN ('T', 'E', 'S')" \
    "3"

echo ""
echo "Testing spatial positioning..."
psql -h localhost -U postgres -d hartonomous -c "
    DO \$\$
    DECLARE
        v_atom_id BIGINT;
    BEGIN
        v_atom_id := atomize_value(convert_to('spatial_test', 'UTF8'), 'spatial_test', '{\"modality\": \"test\"}'::jsonb);
        UPDATE atom SET spatial_key = ST_MakePoint(1, 2, 3) WHERE atom_id = v_atom_id;
    END \$\$;
" > /dev/null 2>&1

test_query "Spatial position set" \
    "SELECT COUNT(*) FROM atom WHERE canonical_text = 'spatial_test' AND spatial_key IS NOT NULL" \
    "1"

echo ""
echo "Testing relations..."
psql -h localhost -U postgres -d hartonomous -c "
    DO \$\$
    DECLARE
        v_source_id BIGINT;
        v_target_id BIGINT;
    BEGIN
        v_source_id := atomize_value(convert_to('source', 'UTF8'), 'source', '{}'::jsonb);
        v_target_id := atomize_value(convert_to('target', 'UTF8'), 'target', '{}'::jsonb);
        PERFORM create_relation(v_source_id, v_target_id, 'test_relation', 0.8);
    END \$\$;
" > /dev/null 2>&1

test_query "Relation created" \
    "SELECT COUNT(*) FROM atom_relation ar JOIN atom s ON s.atom_id = ar.source_atom_id JOIN atom t ON t.atom_id = ar.target_atom_id WHERE s.canonical_text = 'source' AND t.canonical_text = 'target'" \
    "1"

echo ""
echo "============================================"
echo "Test Results"
echo "============================================"
echo -e "Tests Passed: ${GREEN}$TESTS_PASSED${NC}"
echo -e "Tests Failed: ${RED}$TESTS_FAILED${NC}"
echo ""

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "${GREEN}? All tests passed!${NC}"
    echo ""
    echo "Hartonomous is ready to use. Try:"
    echo "  psql -h localhost -U postgres -d hartonomous"
    echo "  hartonomous=# \\i /docker-entrypoint-initdb.d/999_examples.sql"
    exit 0
else
    echo -e "${RED}? Some tests failed${NC}"
    echo "Please check the errors above and consult docs/03-GETTING-STARTED.md"
    exit 1
fi
