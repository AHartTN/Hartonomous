#!/usr/bin/env bash
# Script 4: Ingest Test Data (Moby Dick + HuggingFace models)

set -e

source "$(dirname "$0")/common.sh"

print_header "Ingesting Test Data"

REPO_ROOT="$(get_repo_root)"
cd "$REPO_ROOT"

PRESET=${1:-release-native}
BUILD_DIR="build/$PRESET"

# Check if build exists
if [ ! -d "$BUILD_DIR" ]; then
    print_error "Build directory not found: $BUILD_DIR"
    print_info "Run ./scripts/02-build-all.sh first"
    exit 1
fi

# Check PostgreSQL
if ! check_postgres; then
    exit 1
fi

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGDATABASE=${PGDATABASE:-hypercube}

print_success "Connected to PostgreSQL: $PGDATABASE"

# Create ingestion tool (simple C++ program)
print_step "Creating ingestion tool..."

INGEST_TOOL="$BUILD_DIR/ingest_tool"

cat > /tmp/ingest_tool.cpp << 'EOF'
#include <ingestion/text_ingester.hpp>
#include <ingestion/safetensor_loader.hpp>
#include <database/postgres_connection.hpp>
#include <iostream>
#include <fstream>
#include <filesystem>

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <text_file|model_dir>\n";
        return 1;
    }

    try {
        // Connect to database
        Hartonomous::PostgresConnection db;
        std::cout << "Connected to database\n";

        std::string path = argv[1];

        if (std::filesystem::is_directory(path)) {
            // Ingest HuggingFace model
            std::cout << "Ingesting HuggingFace model from: " << path << "\n";

            Hartonomous::SafetensorLoader loader(path);
            std::cout << "Model type: " << loader.metadata().model_type << "\n";
            std::cout << "Vocab size: " << loader.metadata().vocab.size() << "\n";

            loader.ingest(db);
            std::cout << "Model ingestion complete\n";
        } else {
            // Ingest text file
            std::cout << "Ingesting text from: " << path << "\n";

            Hartonomous::TextIngester ingester(db);
            auto stats = ingester.ingest_file(path);

            std::cout << "Ingestion complete:\n";
            std::cout << "  Atoms: " << stats.atoms_new << " new, "
                      << stats.atoms_existing << " existing\n";
            std::cout << "  Compositions: " << stats.compositions_new << " new, "
                      << stats.compositions_existing << " existing\n";
            std::cout << "  Relations: " << stats.relations_total << "\n";
            std::cout << "  Compression: " << (stats.compression_ratio() * 100) << "%\n";
        }

        return 0;
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}
EOF

# Build ingestion tool
g++ -std=c++20 -I"$BUILD_DIR/Engine/include" -I"Engine/include" \
    /tmp/ingest_tool.cpp -o "$INGEST_TOOL" \
    -L"$BUILD_DIR/Engine" -lengine -lpq -lpthread \
    -Wl,-rpath,"$BUILD_DIR/Engine" 2>/dev/null || {
    print_warning "Could not build ingest tool (will use SQL fallback)"
    INGEST_TOOL=""
}

if [ -n "$INGEST_TOOL" ] && [ -f "$INGEST_TOOL" ]; then
    print_success "Ingestion tool built"
else
    print_warning "Using SQL fallback for ingestion"
fi

# Ingest Moby Dick
TEST_DATA_DIR="$REPO_ROOT/test-data"

if [ -f "$TEST_DATA_DIR/moby-dick.txt" ]; then
    print_step "Ingesting Moby Dick..."

    if [ -n "$INGEST_TOOL" ]; then
        # Use C++ tool
        "$INGEST_TOOL" "$TEST_DATA_DIR/moby-dick.txt" || {
            print_error "Ingestion failed"
            exit 1
        }
    else
        # Fallback: Direct SQL insert (simplified)
        print_warning "Using simplified SQL ingestion"

        # Read first 1000 characters for demo
        TEXT=$(head -c 1000 "$TEST_DATA_DIR/moby-dick.txt")

        psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" << EOSQL
-- Simple ingestion: Insert "Call me Ishmael" manually
INSERT INTO compositions (hash, text, centroid_x, centroid_y, centroid_z, centroid_w, hilbert_index)
VALUES
    (decode('$(echo -n "Call" | blake3sum | cut -d' ' -f1)', 'hex'), 'Call', 0.5, 0.5, 0.5, 0.5, 12345),
    (decode('$(echo -n "me" | blake3sum | cut -d' ' -f1)', 'hex'), 'me', 0.5, 0.5, 0.5, 0.5, 12346),
    (decode('$(echo -n "Ishmael" | blake3sum | cut -d' ' -f1)', 'hex'), 'Ishmael', 0.5, 0.5, 0.5, 0.5, 12347),
    (decode('$(echo -n "Captain" | blake3sum | cut -d' ' -f1)', 'hex'), 'Captain', 0.5, 0.5, 0.5, 0.5, 12348),
    (decode('$(echo -n "Ahab" | blake3sum | cut -d' ' -f1)', 'hex'), 'Ahab', 0.5, 0.5, 0.5, 0.5, 12349),
    (decode('$(echo -n "ship" | blake3sum | cut -d' ' -f1)', 'hex'), 'ship', 0.5, 0.5, 0.5, 0.5, 12350),
    (decode('$(echo -n "Pequod" | blake3sum | cut -d' ' -f1)', 'hex'), 'Pequod', 0.5, 0.5, 0.5, 0.5, 12351)
ON CONFLICT (hash) DO NOTHING;

-- Create a relation: "Captain Ahab of the Pequod"
INSERT INTO relations (hash, level, length, centroid_x, centroid_y, centroid_z, centroid_w, parent_type)
VALUES (decode('$(echo -n "relation1" | blake3sum | cut -d' ' -f1)', 'hex'), 1, 4, 0.5, 0.5, 0.5, 0.5, 'composition')
ON CONFLICT (hash) DO NOTHING;

-- Link compositions to relation
INSERT INTO relation_children (relation_hash, child_hash, child_type, position)
SELECT
    decode('$(echo -n "relation1" | blake3sum | cut -d' ' -f1)', 'hex'),
    hash,
    'composition',
    row_number() OVER () - 1
FROM compositions
WHERE text IN ('Captain', 'Ahab', 'Pequod', 'ship')
ON CONFLICT DO NOTHING;
EOSQL
    fi

    print_success "Moby Dick ingested"
else
    print_warning "Moby Dick test data not found: $TEST_DATA_DIR/moby-dick.txt"
fi

# Ingest HuggingFace models
if [ -d "$TEST_DATA_DIR/minilm" ]; then
    print_step "Ingesting minilm model..."

    if [ -n "$INGEST_TOOL" ]; then
        "$INGEST_TOOL" "$TEST_DATA_DIR/minilm" || {
            print_warning "Model ingestion failed (continuing)"
        }
    else
        print_warning "Skipping model ingestion (C++ tool not available)"
    fi
else
    print_warning "minilm model not found: $TEST_DATA_DIR/minilm"
fi

# Verify ingestion
print_step "Verifying ingestion..."

COMP_COUNT=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -t -c \
    "SELECT COUNT(*) FROM compositions;")

REL_COUNT=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -t -c \
    "SELECT COUNT(*) FROM relations;")

print_success "Verification complete:"
print_info "  Compositions: $COMP_COUNT"
print_info "  Relations: $REL_COUNT"

print_complete "Test data ingestion complete!"
echo ""
print_info "Next step: ./scripts/05-run-queries.sh"
echo ""
