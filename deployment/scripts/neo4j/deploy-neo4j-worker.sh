#!/bin/bash
# Neo4j Worker Deployment Script (Bash)
# Deploys Neo4j provenance worker
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Parse arguments
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"

while [[ $# -gt 0 ]]; do
    case $1 in
        -e|--environment)
            ENVIRONMENT="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Neo4j Worker Deployment"

# Validate environment
if [[ -z "$ENVIRONMENT" ]]; then
    write_failure "DEPLOYMENT_ENVIRONMENT not set. Use -e parameter or set environment variable."
fi

case $ENVIRONMENT in
    development|staging|production)
        ;;
    *)
        write_failure "Invalid environment: $ENVIRONMENT"
        ;;
esac

# Load configuration
load_deployment_config "$ENVIRONMENT"
write_log "Loaded configuration for: $ENVIRONMENT" "INFO"

# Check if Neo4j is enabled
NEO4J_ENABLED=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.features.neo4j_enabled')
if [[ "$NEO4J_ENABLED" != "true" ]]; then
    write_log "Neo4j is disabled for environment: $ENVIRONMENT" "WARNING"
    write_success "Neo4j worker deployment skipped (disabled in config)"
    exit 0
fi

# Configure Neo4j connection
write_step "Configuring Neo4j Connection"
"$SCRIPT_DIR/configure-neo4j.sh" -e "$ENVIRONMENT"

# Test Neo4j connectivity
write_step "Testing Neo4j Connectivity"

NEO4J_URI=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.uri')
NEO4J_USER=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.user')
NEO4J_DATABASE=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.neo4j.database')

write_log "Neo4j URI: $NEO4J_URI" "INFO"
write_log "Neo4j Database: $NEO4J_DATABASE" "INFO"

# Get Neo4j password
if [[ "$ENVIRONMENT" != "development" ]]; then
    write_log "Retrieving Neo4j credentials from Azure Key Vault..." "INFO"

    azure_login

    KV_URL=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.azure.key_vault_url')
    KV_NAME=$(echo "$KV_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')
    MACHINE=$(echo "$DEPLOYMENT_CONFIG" | jq -r '.target.machine')
    NEO4J_PASSWORD=$(get_keyvault_secret "$KV_NAME" "Neo4j-$MACHINE-Password")
else
    # Development: Use environment variable or default
    NEO4J_PASSWORD="${NEO4J_PASSWORD:-neo4jneo4j}"
fi

# Test connection using cypher-shell (if available)
write_log "Testing Neo4j connection..." "DEBUG"

if command -v cypher-shell &> /dev/null; then
    export NEO4J_PASSWORD
    if cypher-shell -a "$NEO4J_URI" -u "$NEO4J_USER" -d "$NEO4J_DATABASE" "RETURN 'connected' as status" &> /dev/null; then
        write_success "Neo4j connection successful"
    else
        write_log "Neo4j connection test failed" "WARNING"
        write_log "Worker will attempt connection at runtime" "INFO"
    fi
else
    write_log "cypher-shell not found in PATH, skipping connectivity test" "INFO"
    write_log "Worker will test connection at startup" "INFO"
fi

# Verify Neo4j worker exists in API
API_PATH="$SCRIPT_DIR/../../../api"
WORKER_PATH="$API_PATH/workers/neo4j_sync.py"

if [[ ! -f "$WORKER_PATH" ]]; then
    write_failure "Neo4j worker not found: $WORKER_PATH"
fi

write_success "Neo4j worker found: workers/neo4j_sync.py"

# Update API configuration to ensure Neo4j worker is enabled
write_step "Verifying API Configuration"

API_CONFIG_PATH="$API_PATH/config.py"
if [[ -f "$API_CONFIG_PATH" ]]; then
    if grep -q "NEO4J_ENABLED.*=.*True\|neo4j_enabled.*=.*True" "$API_CONFIG_PATH"; then
        write_success "Neo4j worker enabled in API configuration"
    else
        write_log "Warning: NEO4J_ENABLED may not be set in config.py" "WARNING"
        write_log "Ensure .env has NEO4J_ENABLED=true" "INFO"
    fi
fi

# Check if worker will start with API or separately
write_step "Worker Startup Mode"

if [[ "$ENVIRONMENT" == "development" ]]; then
    write_success "Development mode: Worker starts with API automatically"
    write_log "The Neo4j worker will start when you run: uvicorn main:app --reload" "INFO"
else
    write_success "Production mode: Worker starts with API service"
    write_log "The Neo4j worker is part of the API systemd service" "INFO"
fi

# Create Neo4j constraints and indexes
write_step "Creating Neo4j Schema (Constraints & Indexes)"

# Python script to create constraints
SCHEMA_SCRIPT=$(cat <<'EOF'
import os
from neo4j import GraphDatabase

uri = os.getenv('NEO4J_URI')
user = os.getenv('NEO4J_USER')
password = os.getenv('NEO4J_PASSWORD')
database = os.getenv('NEO4J_DATABASE', 'neo4j')

driver = GraphDatabase.driver(uri, auth=(user, password))

try:
    with driver.session(database=database) as session:
        # Create constraint on Atom.atom_id (unique)
        print("Creating constraint on Atom.atom_id...")
        session.run("CREATE CONSTRAINT atom_id_unique IF NOT EXISTS FOR (a:Atom) REQUIRE a.atom_id IS UNIQUE")

        # Create index on Atom.content_hash
        print("Creating index on Atom.content_hash...")
        session.run("CREATE INDEX atom_content_hash IF NOT EXISTS FOR (a:Atom) ON (a.content_hash)")

        # Create index on DERIVED_FROM.created_at
        print("Creating index on DERIVED_FROM.created_at...")
        session.run("CREATE INDEX derived_from_created_at IF NOT EXISTS FOR ()-[r:DERIVED_FROM]-() ON (r.created_at)")

        print("Neo4j schema created successfully")

finally:
    driver.close()
EOF
)

TEMP_SCRIPT="/tmp/neo4j-schema-setup-$$.py"
echo "$SCHEMA_SCRIPT" > "$TEMP_SCRIPT"

# Set environment variables for Python script
export NEO4J_URI
export NEO4J_USER
export NEO4J_PASSWORD
export NEO4J_DATABASE

# Run schema setup
cd "$API_PATH"
if python3 "$TEMP_SCRIPT" 2>&1; then
    write_success "Neo4j schema created"
else
    write_log "Schema creation failed (may already exist or Neo4j unreachable)" "WARNING"
    write_log "You may need to create schema manually" "INFO"
fi
cd - > /dev/null

rm -f "$TEMP_SCRIPT"

# Summary
write_step "Deployment Summary"
write_success "Neo4j worker deployment completed"
write_log "Neo4j URI: $NEO4J_URI" "INFO"
write_log "Neo4j Database: $NEO4J_DATABASE" "INFO"
write_log "Worker file: api/workers/neo4j_sync.py" "INFO"

if [[ "$ENVIRONMENT" == "development" ]]; then
    echo ""
    echo "To start the worker:"
    echo "1. cd api"
    echo "2. source .venv/bin/activate"
    echo "3. python -m uvicorn main:app --reload"
    echo "4. Worker starts automatically and listens for PostgreSQL events"
fi

write_log "Neo4j worker deployment completed" "INFO"
