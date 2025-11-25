#!/bin/bash
# Python Linting Script
# Runs Python linting tools (flake8, black, pylint)
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Python Linting"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."
API_PATH="$REPO_ROOT/api"

if [[ ! -d "$API_PATH" ]]; then
    write_failure "API directory not found: $API_PATH"
fi

cd "$API_PATH"

# Check if linting tools are installed
write_step "Checking Linting Tools"

TOOLS_MISSING=false

if ! python -m flake8 --version &>/dev/null; then
    write_log "flake8 not installed" "WARNING"
    TOOLS_MISSING=true
fi

if ! python -m black --version &>/dev/null; then
    write_log "black not installed" "WARNING"
    TOOLS_MISSING=true
fi

if ! python -m pylint --version &>/dev/null; then
    write_log "pylint not installed" "WARNING"
    TOOLS_MISSING=true
fi

if [[ "$TOOLS_MISSING" == "true" ]]; then
    write_log "Installing linting tools..." "INFO"
    pip install --quiet flake8 black pylint
fi

write_success "Linting tools ready"

# Run flake8
write_step "Running flake8"
if python -m flake8 . --count --select=E9,F63,F7,F82 --show-source --statistics; then
    write_success "flake8 passed (syntax errors and undefined names)"
else
    write_failure "flake8 found errors"
fi

# Check for warnings
if python -m flake8 . --count --exit-zero --max-complexity=10 --max-line-length=127 --statistics; then
    write_success "flake8 style check completed"
fi

# Run black (check only, no formatting)
write_step "Running black (check mode)"
if python -m black --check --diff . ; then
    write_success "black formatting check passed"
else
    write_log "black found formatting issues (run 'black .' to fix)" "WARNING"
fi

# Run pylint
write_step "Running pylint"
if python -m pylint --rcfile=../.pylintrc $(find . -name "*.py" | grep -v ".venv" | grep -v "__pycache__") --exit-zero; then
    write_success "pylint check completed"
else
    write_log "pylint found issues" "WARNING"
fi

write_step "Python Linting Complete"
write_success "All Python linting checks completed"

exit 0
