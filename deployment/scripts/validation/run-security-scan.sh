#!/usr/bin/env bash
# Security Scanning Script (Bandit + Safety)
# Runs SAST analysis on Python code

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

echo "=== Hartonomous Security Scan ==="
echo "Repo: $REPO_ROOT"
echo ""

cd "$REPO_ROOT"

# Install security tools if not present
if ! command -v bandit &> /dev/null; then
    echo "Installing bandit..."
    pip install --quiet bandit safety
fi

# Run Bandit (SAST)
echo "Running Bandit security scan..."
bandit -r api/ -c .bandit -f json -o bandit-report.json || true
bandit -r api/ -c .bandit -f txt

# Run Safety (dependency vulnerability check)
echo ""
echo "Running Safety dependency check..."
cd api
safety check --file requirements.txt --json || true
safety check --file requirements.txt

cd "$REPO_ROOT"

echo ""
echo "Security scan complete"
echo "Report saved: bandit-report.json"
