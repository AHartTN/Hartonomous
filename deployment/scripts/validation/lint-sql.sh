#!/bin/bash
# SQL Linting Script
# Validates SQL syntax using sqlfluff
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

initialize_logger "${LOG_LEVEL:-INFO}"

write_step "SQL Linting"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."
SCHEMA_PATH="$REPO_ROOT/schema"

if [[ ! -d "$SCHEMA_PATH" ]]; then
    write_failure "Schema directory not found: $SCHEMA_PATH"
fi

cd "$SCHEMA_PATH"

# Check if sqlfluff is installed
if ! command -v sqlfluff &>/dev/null; then
    write_log "sqlfluff not installed, installing..." "INFO"
    pip install --quiet sqlfluff
fi

write_success "sqlfluff ready"

# Find all SQL files
write_step "Finding SQL Files"
SQL_FILES=$(find . -name "*.sql" -type f || true)

if [[ -z "$SQL_FILES" ]]; then
    write_log "No SQL files found" "WARNING"
    exit 0
fi

FILE_COUNT=$(echo "$SQL_FILES" | wc -l)
write_log "Found $FILE_COUNT SQL files" "INFO"

# Run sqlfluff lint
write_step "Linting SQL Files"
if echo "$SQL_FILES" | xargs sqlfluff lint --dialect postgres --exclude-rules L034,L036; then
    write_success "All SQL files passed"
else
    write_log "Some SQL files have style issues" "WARNING"
    write_log "Run 'sqlfluff fix' to automatically fix some issues" "INFO"
fi

# Check for common SQL anti-patterns
write_step "Checking for SQL Anti-patterns"

# Check for SELECT *
if grep -r "SELECT \*" . --include="*.sql" &>/dev/null; then
    write_log "Warning: Found SELECT * in SQL files (use explicit column names)" "WARNING"
fi

# Check for missing semicolons
if grep -L ";" $(find . -name "*.sql") &>/dev/null; then
    write_log "Warning: Some SQL files may be missing semicolons" "WARNING"
fi

write_success "Anti-pattern checks completed"

write_step "SQL Linting Complete"
write_success "All SQL linting checks completed"

exit 0
