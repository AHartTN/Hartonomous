#!/usr/bin/env bash
set -euo pipefail

# --- Configuration ---
BUILD_PRESET="linux-release-max-perf" # Assuming this is the default or passed as arg
EXT_NAME="hartonomous"

# --- Directories ---
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." >/dev/null 2>&1 && pwd)"
BUILD_DIR="${REPO_ROOT}/build/${BUILD_PRESET}"

# --- Functions ---
print_info() { echo -e "\033[0;36m$1\033[0m"; }
print_success() { echo -e "\033[0;32m$1\033[0m"; }
print_error() { echo -e "\033[0;31m$1\033[0m"; }
print_warning() { echo -e "\033[1;33m$1\033[0m"; }

# --- Main Logic ---

print_info "Starting Hartonomous PostgreSQL Extension Installation (CMake Install Method)..."

# 1. Verify build directory exists
if [[ ! -d "${BUILD_DIR}" ]]; then
    print_error "Error: Build directory not found at ${BUILD_DIR}"
    print_error "Please build the project first: ${REPO_ROOT}/build.sh -c"
    exit 1
fi

# 2. Call cmake --install
print_info "Executing 'sudo cmake --install ${BUILD_DIR}' to install the extension."
print_warning "This step requires sudo privileges to install files into PostgreSQL system directories."
print_warning "Please enter your sudo password when prompted."

sudo cmake --install "${BUILD_DIR}"

print_success "Hartonomous PostgreSQL Extension installation complete!"
print_info "To activate the extension in your PostgreSQL database, connect via psql and run:"
print_info "  CREATE EXTENSION ${EXT_NAME};"
echo ""
