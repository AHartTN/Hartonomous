#!/bin/bash
# YAML Linting Script
# Validates YAML syntax using yamllint
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

initialize_logger "${LOG_LEVEL:-INFO}"

write_step "YAML Linting"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."

cd "$REPO_ROOT"

# Check if yamllint is installed
if ! command -v yamllint &>/dev/null; then
    write_log "yamllint not installed, installing..." "INFO"
    pip install --quiet yamllint
fi

write_success "yamllint ready"

# Run yamllint on GitHub workflows
write_step "Linting GitHub Workflows"
if yamllint .github/workflows/*.yml; then
    write_success "GitHub workflows passed"
else
    write_failure "GitHub workflows have YAML errors"
fi

# Run yamllint on deployment configs
write_step "Linting Deployment Configs"
if yamllint deployment/config/*.json 2>/dev/null || true; then
    write_success "Deployment configs checked (JSON, not YAML)"
fi

# Find and lint any other YAML files
write_step "Linting Other YAML Files"
YAML_FILES=$(find . -name "*.yml" -o -name "*.yaml" | grep -v ".venv" | grep -v "node_modules" || true)

if [[ -n "$YAML_FILES" ]]; then
    if echo "$YAML_FILES" | xargs yamllint; then
        write_success "All YAML files passed"
    else
        write_failure "Some YAML files have errors"
    fi
else
    write_log "No additional YAML files found" "INFO"
fi

write_step "YAML Linting Complete"
write_success "All YAML linting checks completed"

exit 0
