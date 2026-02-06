#!/usr/bin/env bash
# ==============================================================================
# Restore Normal Installation (Remove Dev Symlinks)
# ==============================================================================
# Removes development symlinks and installs actual files (copies)
#
# Usage: sudo ./scripts/linux/02-install-dev-restore.sh
# ==============================================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

print_success() { echo -e "${GREEN}✓ $1${NC}"; }
print_error() { echo -e "${RED}✗ $1${NC}"; }
print_info() { echo -e "${CYAN}$1${NC}"; }

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    print_error "This script must be run with sudo"
    exit 1
fi

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../.." && pwd )"

print_info "=== Restoring Normal Installation ==="
echo ""
print_info "This will remove development symlinks and install actual files"
echo ""

# Call the normal install script
if [ -x "$SCRIPT_DIR/02-install.sh" ]; then
    "$SCRIPT_DIR/02-install.sh"
    print_success "Normal installation complete"
else
    print_error "Install script not found or not executable: $SCRIPT_DIR/02-install.sh"
    exit 1
fi

# Remove marker file
if [ -f "$PROJECT_ROOT/.dev-symlinks-active" ]; then
    rm "$PROJECT_ROOT/.dev-symlinks-active"
    print_success "Removed development mode marker"
fi

echo ""
print_success "=== Restored to Normal Installation ==="
echo ""
print_info "Development symlinks removed, actual files installed"
print_info "Future builds will require sudo for installation"
echo ""
