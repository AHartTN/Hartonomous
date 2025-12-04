#!/bin/bash
# Deploy PL/Python GPU functions to PostgreSQL server

set -e

echo "==========================================="
echo "Deploying PL/Python GPU Functions"
echo "==========================================="

# Configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-hartonomous}"
DB_USER="${DB_USER:-postgres}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_FUNCTIONS_DIR="$SCRIPT_DIR/../Hartonomous.Data/Functions/PlPython"
SQL_FUNCTIONS_FILE="$SCRIPT_DIR/../Hartonomous.Data/Migrations/20250101000002_AddGpuFunctions.sql"

# Target deployment directory on server
DEPLOY_DIR="${DEPLOY_DIR:-/var/lib/hartonomous/functions/plpython}"

echo ""
echo "Configuration:"
echo "  Database: $DB_USER@$DB_HOST:$DB_PORT/$DB_NAME"
echo "  Python Functions: $PYTHON_FUNCTIONS_DIR"
echo "  Deploy Directory: $DEPLOY_DIR"
echo ""

# Step 1: Check if PostgreSQL is accessible
echo "[1/6] Checking PostgreSQL connectivity..."
if ! psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT version();" > /dev/null 2>&1; then
    echo "ERROR: Cannot connect to PostgreSQL"
    exit 1
fi
echo "✓ PostgreSQL is accessible"

# Step 2: Check if plpython3u extension is available
echo ""
echo "[2/6] Checking plpython3u extension..."
PLPYTHON_AVAILABLE=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM pg_available_extensions WHERE name = 'plpython3u';")
if [ "$PLPYTHON_AVAILABLE" -eq 0 ]; then
    echo "ERROR: plpython3u extension not available. PostgreSQL must be compiled with Python support."
    exit 1
fi

# Check if extension is installed
PLPYTHON_INSTALLED=$(psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM pg_extension WHERE extname = 'plpython3u';")
if [ "$PLPYTHON_INSTALLED" -eq 0 ]; then
    echo "Installing plpython3u extension (requires superuser)..."
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS plpython3u;"
fi
echo "✓ plpython3u extension is installed"

# Step 3: Create deployment directory
echo ""
echo "[3/6] Creating deployment directory..."
if [ ! -d "$DEPLOY_DIR" ]; then
    sudo mkdir -p "$DEPLOY_DIR"
    sudo chmod 755 "$DEPLOY_DIR"
fi
echo "✓ Deployment directory ready: $DEPLOY_DIR"

# Step 4: Copy Python function files
echo ""
echo "[4/6] Copying Python function files..."
sudo cp -v "$PYTHON_FUNCTIONS_DIR"/*.py "$DEPLOY_DIR/"
sudo chmod 644 "$DEPLOY_DIR"/*.py
echo "✓ Python files deployed"

# Step 5: Update function path in migration
echo ""
echo "[5/6] Updating function path configuration..."
TEMP_SQL_FILE=$(mktemp)
sed "s|/var/lib/hartonomous/functions/plpython|$DEPLOY_DIR|g" "$SQL_FUNCTIONS_FILE" > "$TEMP_SQL_FILE"
echo "✓ Function path configured"

# Step 6: Execute SQL migration
echo ""
echo "[6/6] Creating PostgreSQL functions..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$TEMP_SQL_FILE"
rm "$TEMP_SQL_FILE"
echo "✓ PostgreSQL functions created"

# Step 7: Test GPU availability
echo ""
echo "==========================================="
echo "Testing GPU Availability"
echo "==========================================="
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT * FROM gpu_check_availability();"

# Step 8: List installed functions
echo ""
echo "==========================================="
echo "Installed GPU Functions"
echo "==========================================="
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "
SELECT 
    routine_name,
    routine_type,
    data_type as return_type
FROM information_schema.routines
WHERE routine_schema = 'public'
  AND routine_name LIKE 'gpu_%'
ORDER BY routine_name;
"

echo ""
echo "==========================================="
echo "Deployment Complete!"
echo "==========================================="
echo ""
echo "GPU-accelerated functions are now available:"
echo "  • gpu_spatial_knn           - K-nearest neighbors search"
echo "  • gpu_spatial_clustering    - DBSCAN clustering"
echo "  • gpu_similarity_search     - Cosine similarity"
echo "  • gpu_bpe_learn             - BPE vocabulary learning"
echo "  • gpu_hilbert_index_batch   - Hilbert curve indexing"
echo "  • gpu_check_availability    - GPU status check"
echo "  • update_hilbert_indices_gpu       - Batch index update helper"
echo "  • detect_landmarks_from_clustering - Landmark detection helper"
echo ""
echo "Next steps:"
echo "  1. Install Python GPU libraries: pip install cupy-cuda12x cuml-cu12"
echo "  2. Verify GPU availability: SELECT * FROM gpu_check_availability();"
echo "  3. Test with sample query: SELECT * FROM gpu_spatial_knn(0, 0, 0, 10);"
echo ""
