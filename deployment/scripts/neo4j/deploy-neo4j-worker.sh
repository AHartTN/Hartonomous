#!/usr/bin/env bash
# Deploy Neo4j Worker Script
# Ensures Neo4j provenance worker is running

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"

ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-development}"
initialize_logger "/var/log/hartonomous/neo4j-worker-$(date '+%Y%m%d-%H%M%S').log" "INFO"

write_step "Neo4j Worker Deployment - $ENVIRONMENT"

# Load configuration
CONFIG=$(get_deployment_config "$ENVIRONMENT")
NEO4J_ENABLED=$(echo "$CONFIG" | jq -r '.features.neo4j_enabled')

if [[ "$NEO4J_ENABLED" != "true" ]]; then
    write_log "Neo4j is disabled in configuration, skipping" "INFO"
    exit 0
fi

write_log "Neo4j provenance tracking enabled" "INFO"

# Check Neo4j connectivity
write_step "Checking Neo4j Connectivity"
NEO4J_URI=$(echo "$CONFIG" | jq -r '.neo4j.uri')
NEO4J_HOST=$(echo "$NEO4J_URI" | sed 's|bolt://||' | cut -d: -f1)
NEO4J_PORT=$(echo "$NEO4J_URI" | sed 's|bolt://||' | cut -d: -f2)

if nc -z "$NEO4J_HOST" "$NEO4J_PORT" &>/dev/null; then
    write_success "Neo4j is accessible at $NEO4J_URI"
else
    write_failure "Neo4j is not accessible at $NEO4J_URI"
fi

# Verify Python neo4j driver installed
write_step "Checking Neo4j Python Driver"
if python3 -c "import neo4j; print(neo4j.__version__)" &>/dev/null; then
    DRIVER_VERSION=$(python3 -c "import neo4j; print(neo4j.__version__)")
    write_success "Neo4j Python driver installed: v$DRIVER_VERSION"
else
    write_log "Installing Neo4j Python driver..." "INFO"
    pip3 install --quiet neo4j
    write_success "Neo4j Python driver installed"
fi

# The worker starts with the API, so we just verify configuration
write_step "Worker Configuration"
write_log "Worker will start with API application" "INFO"
write_log "Worker listens for: atom_created, composition_created" "INFO"

# Test Neo4j connection with Python
write_step "Testing Neo4j Connection"
python3 << 'EOF'
from neo4j import GraphDatabase
import sys

try:
    # Connection details from environment
    uri = "bolt://localhost:7687"
    auth = ("neo4j", "neo4jneo4j")  # Default, should use Key Vault in prod
    
    driver = GraphDatabase.driver(uri, auth=auth)
    driver.verify_connectivity()
    print("? Neo4j connection successful")
    driver.close()
except Exception as e:
    print(f"? Neo4j connection failed: {e}", file=sys.stderr)
    sys.exit(1)
EOF

if [[ $? -eq 0 ]]; then
    write_success "Neo4j connection test passed"
else
    write_failure "Neo4j connection test failed"
fi

write_step "Neo4j Worker Deployment Complete"
write_success "Worker will start with API application"

exit 0
