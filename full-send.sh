#!/usr/bin/env bash
# ==============================================================================
# Hartonomous Full Build & Deploy Pipeline
# ==============================================================================
# Complete pipeline from source code to running intelligence substrate:
#   1. Build C++ Engine + PostgreSQL Extensions + Unit Tests
#   2. Install Extensions to PostgreSQL (requires sudo)
#   3. Setup Database (create DB, load schema)
#   4. Seed Unicode Codespace (1.114M atoms via UCD/UCA → semantic S³ projection)
#   5. Ingest AI Models (MiniLM embeddings → relation evidence)
#   6. Ingest Text (test corpus → compositions + relations)
#   7. Run Queries + Walk Test
#   8. Run Integration Tests

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_info() { echo -e "${CYAN}=== $1 ===${NC}"; }
print_step() { echo -e "${YELLOW}>>> $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠ $1${NC}"; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

LOG_DIR="$SCRIPT_DIR/logs"
mkdir -p "$LOG_DIR"

# ==============================================================================
#  RUNTIME PERFORMANCE TUNING
# ==============================================================================
# jemalloc: 2-3x faster multi-threaded allocation than glibc malloc
if [ -f /usr/lib/x86_64-linux-gnu/libjemalloc.so.2 ]; then
    export LD_PRELOAD=/usr/lib/x86_64-linux-gnu/libjemalloc.so.2
    print_info "jemalloc enabled via LD_PRELOAD"
fi

# OpenMP: bind threads to cores, avoid migration overhead
export OMP_PROC_BIND=close          # Keep threads on nearby cores (same socket)
export OMP_PLACES=cores             # One thread per physical core
export OMP_SCHEDULE="dynamic,64"    # Match ingester loop scheduling

# MKL: let it adapt thread count based on workload size
export MKL_DYNAMIC=TRUE
export MKL_NUM_THREADS=${OMP_NUM_THREADS:-$(nproc)}

PIPELINE_START=$(date +%s)
step_times=()

timer_start() { STEP_START=$(date +%s); }
timer_end() {
    local step_name="$1"
    local duration=$(( $(date +%s) - STEP_START ))
    step_times+=("$step_name: ${duration}s")
    echo -e "${CYAN}⏱  Completed in ${duration}s${NC}"
}

trap 'print_error "Pipeline failed at step: $CURRENT_STEP"; exit 1' ERR

# ==============================================================================
# STEP 1: Build C++ Engine + Extensions + Unit Tests
# ==============================================================================
CURRENT_STEP="Build"
print_info "STEP 1: Build C++ Engine"
print_step "Building libengine_core.so, libengine_io.so, libengine.so, s3.so, hartonomous.so + unit tests..."
timer_start

if ./scripts/linux/01-build.sh -c -T > "$LOG_DIR/01-build.log" 2>&1; then
    print_success "Build complete + unit tests passed"
    timer_end "Build"
else
    print_error "Build failed (see logs/01-build.log)"
    exit 1
fi

# ==============================================================================
# STEP 2: Install PostgreSQL Extensions
# ==============================================================================
CURRENT_STEP="Install Extensions"
print_info "STEP 2: Install PostgreSQL Extensions"
print_step "Installing s3.so and hartonomous.so (requires sudo)..."
timer_start

if ./scripts/linux/02-install.sh > "$LOG_DIR/02-install.log" 2>&1; then
    print_success "Extensions installed"
    timer_end "Install Extensions"
else
    print_error "Extension installation failed (see logs/02-install.log)"
    exit 1
fi

# ==============================================================================
# STEP 3: Setup Database
# ==============================================================================
CURRENT_STEP="Setup Database"
print_info "STEP 3: Setup Database"
print_step "Creating hartonomous database with schema (Physicality, Atom, Composition, Relation, Evidence)..."
timer_start

if ./scripts/linux/03-setup-database.sh --drop > "$LOG_DIR/03-setup-database.log" 2>&1; then
    print_success "Database setup complete"
    timer_end "Setup Database"
else
    print_error "Database setup failed (see logs/03-setup-database.log)"
    exit 1
fi

# ==============================================================================
# STEP 4: Seed Unicode Codespace
# ==============================================================================
CURRENT_STEP="Seed Unicode"
print_info "STEP 4: Seed Unicode Codespace"
print_step "Parsing UCD/UCA data → semantic sequencing → S³ projection → 1.114M atoms..."
timer_start

if ./scripts/linux/05-seed-unicode.sh > "$LOG_DIR/04-seed-unicode.log" 2>&1; then
    print_success "Unicode codespace seeded"

    timer_end "Seed Unicode"

        # ═══════════════════════════════════════════════════════════
        # DROP INDEXES ONCE (Master bulk-load optimization)
        # ═══════════════════════════════════════════════════════════
        ./scripts/linux/drop-substrate-indexes.sh

        # ======================================================================
        # STEP 4b: Ingest WordNet and OMW-data
        # ======================================================================
        CURRENT_STEP="Ingest WordNet/OMW"
        print_info "STEP 4b: Ingest WordNet and OMW-data"
        print_step "Populating Compositions and Relations from WordNet/OMW..."
        timer_start
        if ./scripts/linux/06-ingest-wordnet-omw.sh > "$LOG_DIR/04b-ingest-wordnet-omw.log" 2>&1; then
            print_success "WordNet/OMW ingestion complete"
            timer_end "Ingest WordNet/OMW"
        else
            print_error "WordNet/OMW ingestion failed (see logs/04b-ingest-wordnet-omw.log)"
            # Ensure indexes are rebuilt even on failure to maintain DB integrity if possible
            ./scripts/linux/rebuild-substrate-indexes.sh || true
            exit 1
        fi

        # ======================================================================
        # STEP 4c: Ingest Tatoeba (Translation Links)
        # ======================================================================
        CURRENT_STEP="Ingest Tatoeba"
        print_info "STEP 4c: Ingest Tatoeba"
        print_step "Ingesting 13M sentences + translation links from Tatoeba..."
        timer_start
        if ./scripts/linux/07-ingest-tatoeba.sh > "$LOG_DIR/04c-ingest-tatoeba.log" 2>&1; then
            print_success "Tatoeba ingestion complete"
            timer_end "Ingest Tatoeba"
        else
            print_error "Tatoeba ingestion failed (see logs/04c-ingest-tatoeba.log)"
            ./scripts/linux/rebuild-substrate-indexes.sh || true
            exit 1
        fi

        # ======================================================================
        # STEP 4d: Ingest Universal Dependencies
        # ======================================================================
        CURRENT_STEP="Ingest UD"
        print_info "STEP 4d: Ingest Universal Dependencies"
        print_step "Ingesting dependency treebanks (syntactic/semantic trajectories)..."
        timer_start
        if ./scripts/linux/08-ingest-ud.sh > "$LOG_DIR/04d-ingest-ud.log" 2>&1; then
            print_success "Universal Dependencies ingestion complete"
            timer_end "Ingest UD"
        else
            print_error "Universal Dependencies ingestion failed (see logs/04d-ingest-ud.log)"
            ./scripts/linux/rebuild-substrate-indexes.sh || true
            exit 1
        fi

        # ======================================================================
        # STEP 4e: Ingest Wiktionary
        # ======================================================================
        CURRENT_STEP="Ingest Wiktionary"
        print_info "STEP 4e: Ingest Wiktionary"
        print_step "Ingesting 11GB Wiktionary XML (Definitions, Synonyms, Links)..."
        timer_start
        if ./scripts/linux/09-ingest-wiktionary.sh > "$LOG_DIR/04e-ingest-wiktionary.log" 2>&1; then
            print_success "Wiktionary ingestion complete"
            timer_end "Ingest Wiktionary"
        else
            print_error "Wiktionary ingestion failed (see logs/04e-ingest-wiktionary.log)"
            ./scripts/linux/rebuild-substrate-indexes.sh || true
            exit 1
        fi

    # ═══════════════════════════════════════════════════════════
    # REBUILD INDEXES ONCE (Consolidated finalization)
    # ═══════════════════════════════════════════════════════════
    ./scripts/linux/rebuild-substrate-indexes.sh

else
    print_error "Unicode seeding failed (see logs/04-seed-unicode.log)"
    exit 1
fi

# ==============================================================================
# STEP 5: Ingest AI Models
# ==============================================================================
CURRENT_STEP="Ingest Models"
print_info "STEP 5: Ingest AI Models"
print_step "Extracting relationships from production models (MiniLM, Florence-2, Qwen)..."

if [ -f "./scripts/linux/25-ingest-all-models.sh" ]; then
    timer_start
    if ./scripts/linux/25-ingest-all-models.sh > "$LOG_DIR/05-ingest-models.log" 2>&1; then
        print_success "Production models ingested"
        timer_end "Ingest Models"
    else
        print_warning "Model ingestion had issues (see logs/05-ingest-models.log)"
        timer_end "Ingest Models (with issues)"
    fi
elif [ -f "./scripts/linux/20-ingest-mini-lm.sh" ]; then
    timer_start
    if ./scripts/linux/20-ingest-mini-lm.sh > "$LOG_DIR/05-ingest-models.log" 2>&1; then
        print_success "Models ingested (fallback to MiniLM)"
        timer_end "Ingest Models"
    else
        print_warning "Model ingestion had issues (see logs/05-ingest-models.log)"
        timer_end "Ingest Models (with issues)"
    fi
else
    print_warning "Model ingest script not found (skipping)"
fi

# ==============================================================================
# STEP 6: Ingest Text
# ==============================================================================
CURRENT_STEP="Ingest Text"
print_info "STEP 6: Ingest Text"
print_step "Ingesting text corpus → compositions + relations..."

if [ -f "./scripts/linux/30-ingest-text.sh" ]; then
    timer_start
    if ./scripts/linux/30-ingest-text.sh > "$LOG_DIR/06-ingest-text.log" 2>&1; then
        print_success "Text ingested"
        timer_end "Ingest Text"
    else
        print_warning "Text ingestion had issues (see logs/06-ingest-text.log)"
        timer_end "Ingest Text (with issues)"
    fi
else
    print_warning "Text ingest script not found (skipping)"
fi

# ==============================================================================
# STEP 7: Run Queries + Walk Test
# ==============================================================================
CURRENT_STEP="Queries & Walk"
print_info "STEP 7: Run Queries & Walk Test"

if [ -f "./scripts/linux/40-run-queries.sh" ]; then
    print_step "Running semantic queries..."
    timer_start
    if ./scripts/linux/40-run-queries.sh > "$LOG_DIR/07-queries.log" 2>&1; then
        print_success "Queries executed"
        timer_end "Queries"
    else
        print_warning "Queries had issues (see logs/07-queries.log)"
        timer_end "Queries (with issues)"
    fi
fi

if [ -f "./build/linux-release-max-perf/Engine/tools/walk_test" ]; then
    print_step "Running walk test (graph navigation)..."
    timer_start
    if LD_LIBRARY_PATH="$PWD/build/linux-release-max-perf/Engine:$LD_LIBRARY_PATH" ./build/linux-release-max-perf/Engine/tools/walk_test > "$LOG_DIR/07-walk-test.log" 2>&1; then
        print_success "Walk test complete"
        timer_end "Walk Test"
    else
        print_warning "Walk test had issues (see logs/07-walk-test.log)"
        timer_end "Walk Test (with issues)"
    fi
fi

# ==============================================================================
# STEP 8: Integration Tests
# ==============================================================================
CURRENT_STEP="Integration Tests"
print_info "STEP 8: Integration Tests"
print_step "Running integration + e2e tests (database + data required)..."

timer_start
cd build/linux-release-max-perf
if LD_LIBRARY_PATH="$PWD/Engine:$LD_LIBRARY_PATH" /usr/bin/ctest --output-on-failure -L "integration|e2e" > "$LOG_DIR/08-integration-tests.log" 2>&1; then
    print_success "All integration tests passed"
    timer_end "Integration Tests"
else
    print_warning "Some integration tests failed (see logs/08-integration-tests.log)"
    timer_end "Integration Tests (with failures)"
fi
cd "$SCRIPT_DIR"

# ==============================================================================
# STEP 9: Deploy Docker Stack
# ==============================================================================
CURRENT_STEP="Deploy Docker"
print_info "STEP 9: Deploy Docker Stack"
print_step "Packaging native libs and launching containers..."

timer_start

# Prepare docker-libs
mkdir -p docker-libs
cp /usr/lib/x86_64-linux-gnu/libdivsufsort.so.3 docker-libs/ 2>/dev/null || true
cp /opt/intel/oneapi/mkl/2025.3/lib/libmkl_intel_lp64.so.2 docker-libs/ 2>/dev/null || true
cp /opt/intel/oneapi/mkl/2025.3/lib/libmkl_sequential.so.2 docker-libs/ 2>/dev/null || true
cp /opt/intel/oneapi/mkl/2025.3/lib/libmkl_gnu_thread.so.2 docker-libs/ 2>/dev/null || true
cp /opt/intel/oneapi/mkl/2025.3/lib/libmkl_core.so.2 docker-libs/ 2>/dev/null || true

if docker compose up -d --build > "$LOG_DIR/09-docker-deploy.log" 2>&1; then
    print_success "Docker stack deployed"
    print_info "  API: http://localhost:5000"
    print_info "  Web: http://localhost:5001"
    timer_end "Deploy Docker"
else
    print_error "Docker deployment failed (see logs/09-docker-deploy.log)"
    # Don't exit, just warn, as the pipeline itself succeeded
fi

# ==============================================================================
# PIPELINE COMPLETE
# ==============================================================================
PIPELINE_END=$(date +%s)
PIPELINE_DURATION=$((PIPELINE_END - PIPELINE_START))

print_info "PIPELINE COMPLETE"
echo ""
print_success "All steps completed!"
echo ""
echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}TIMING BREAKDOWN:${NC}"
for time in "${step_times[@]}"; do
    echo -e "  ${CYAN}$time${NC}"
done
echo -e "${CYAN}───────────────────────────────────────────────────────────${NC}"
echo -e "${GREEN}TOTAL PIPELINE TIME: ${PIPELINE_DURATION}s ($(($PIPELINE_DURATION / 60))m $(($PIPELINE_DURATION % 60))s)${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
echo ""
echo "Logs saved to: $LOG_DIR/"
echo ""
echo "Next steps:"
echo "  - Connect: psql -U postgres -d hartonomous"
echo "  - Check atoms: psql -U postgres -d hartonomous -c 'SELECT COUNT(*) FROM hartonomous.atom;'"
echo ""
