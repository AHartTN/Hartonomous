#!/bin/bash
# Markdown Linting Script
# Validates Markdown syntax using markdownlint
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"

initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Markdown Linting"

# Get repository root
REPO_ROOT="$SCRIPT_DIR/../../.."

cd "$REPO_ROOT"

# Check if markdownlint-cli is installed
if ! command -v markdownlint &>/dev/null; then
    write_log "markdownlint-cli not installed" "WARNING"
    write_log "Install with: npm install -g markdownlint-cli" "INFO"
    write_log "Skipping Markdown linting" "WARNING"
    exit 0
fi

write_success "markdownlint ready"

# Run markdownlint
write_step "Linting Markdown Files"

# Find all markdown files
MD_FILES=$(find . -name "*.md" | grep -v ".venv" | grep -v "node_modules" || true)

if [[ -z "$MD_FILES" ]]; then
    write_log "No Markdown files found" "WARNING"
    exit 0
fi

write_log "Found $(echo "$MD_FILES" | wc -l) Markdown files" "INFO"

# Run markdownlint with common rules
if echo "$MD_FILES" | xargs markdownlint --config .markdownlint.json 2>/dev/null || \
   echo "$MD_FILES" | xargs markdownlint; then
    write_success "All Markdown files passed"
else
    write_log "Some Markdown files have style issues" "WARNING"
    write_log "These are recommendations, not blocking errors" "INFO"
fi

write_step "Markdown Linting Complete"
write_success "Markdown linting checks completed"

exit 0
