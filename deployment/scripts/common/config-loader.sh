#!/usr/bin/env bash
# Configuration Loader Module (Bash)
# Loads environment-specific configuration
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import logger
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./logger.sh
source "$SCRIPT_DIR/logger.sh"

function get_deployment_config() {
    # Load environment-specific configuration
    # Usage: get_deployment_config <environment>

    local environment="$1"

    write_step "Loading Configuration for $environment"

    # Config file path
    local config_path="$SCRIPT_DIR/../../config/${environment}.json"

    if [[ ! -f "$config_path" ]]; then
        write_failure "Configuration file not found: $config_path"
    fi

    # Load and parse JSON
    if ! cat "$config_path"; then
        write_failure "Failed to load configuration"
    fi

    write_success "Configuration loaded: $environment"
}

function get_deployment_target() {
    # Get deployment target machine info

    local hostname
    hostname=$(hostname)

    local os_type
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        os_type="linux"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        os_type="macos"
    elif [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" ]]; then
        os_type="windows"
    else
        os_type="unknown"
    fi

    echo "{\"hostname\":\"$hostname\",\"os_type\":\"$os_type\"}"
}

function test_environment_variables() {
    # Validate required environment variables are set
    # Usage: test_environment_variables VAR1 VAR2 VAR3...

    write_log "Validating environment variables" "DEBUG"

    local missing=()

    for var in "$@"; do
        if [[ -z "${!var:-}" ]]; then
            missing+=("$var")
            write_log "Missing required environment variable: $var" "ERROR"
        else
            write_log "Found environment variable: $var" "DEBUG"
        fi
    done

    if [[ ${#missing[@]} -gt 0 ]]; then
        write_failure "Missing required environment variables: ${missing[*]}"
    fi

    write_success "All required environment variables are set"
}

# Export functions (documented for bash)
# Available functions:
# - get_deployment_config
# - get_deployment_target
# - test_environment_variables
