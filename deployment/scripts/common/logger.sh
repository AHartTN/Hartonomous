#!/usr/bin/env bash
# Common Logger Module (Bash)
# Provides consistent logging across all deployment scripts
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Log levels
declare -r LOG_LEVEL_DEBUG=0
declare -r LOG_LEVEL_INFO=1
declare -r LOG_LEVEL_WARNING=2
declare -r LOG_LEVEL_ERROR=3

# Current log level (default: INFO)
CURRENT_LOG_LEVEL=${CURRENT_LOG_LEVEL:-$LOG_LEVEL_INFO}

# Log file path
LOG_FILE=${LOG_FILE:-""}

# Colors
declare -r COLOR_RESET='\033[0m'
declare -r COLOR_RED='\033[0;31m'
declare -r COLOR_GREEN='\033[0;32m'
declare -r COLOR_YELLOW='\033[0;33m'
declare -r COLOR_CYAN='\033[0;36m'
declare -r COLOR_GRAY='\033[0;90m'
declare -r COLOR_WHITE='\033[0;97m'

function initialize_logger() {
    # Initialize the logging system
    # Usage: initialize_logger [log_file_path] [level]

    local log_file_path="${1:-}"
    local level="${2:-INFO}"

    case "$level" in
        DEBUG)   CURRENT_LOG_LEVEL=$LOG_LEVEL_DEBUG ;;
        INFO)    CURRENT_LOG_LEVEL=$LOG_LEVEL_INFO ;;
        WARNING) CURRENT_LOG_LEVEL=$LOG_LEVEL_WARNING ;;
        ERROR)   CURRENT_LOG_LEVEL=$LOG_LEVEL_ERROR ;;
    esac

    if [[ -n "$log_file_path" ]]; then
        LOG_FILE="$log_file_path"
        local log_dir
        log_dir=$(dirname "$LOG_FILE")
        mkdir -p "$log_dir"
    fi

    write_log "Logger initialized (Level: $level)" "DEBUG"
}

function write_log() {
    # Write a log message
    # Usage: write_log "message" [level] [no_console]

    local message="$1"
    local level="${2:-INFO}"
    local no_console="${3:-false}"

    # Get numeric log level
    local numeric_level
    case "$level" in
        DEBUG)   numeric_level=$LOG_LEVEL_DEBUG ;;
        INFO)    numeric_level=$LOG_LEVEL_INFO ;;
        WARNING) numeric_level=$LOG_LEVEL_WARNING ;;
        ERROR)   numeric_level=$LOG_LEVEL_ERROR ;;
        *) numeric_level=$LOG_LEVEL_INFO ;;
    esac

    # Check if we should log this level
    if [[ $numeric_level -lt $CURRENT_LOG_LEVEL ]]; then
        return
    fi

    # Format timestamp
    local timestamp
    timestamp=$(date '+%Y-%m-%d %H:%M:%S')

    # Format log entry
    local log_entry="[$timestamp] [$level] $message"

    # Console output with colors
    if [[ "$no_console" != "true" ]]; then
        local color
        case "$level" in
            DEBUG)   color=$COLOR_GRAY ;;
            INFO)    color=$COLOR_WHITE ;;
            WARNING) color=$COLOR_YELLOW ;;
            ERROR)   color=$COLOR_RED ;;
            *) color=$COLOR_WHITE ;;
        esac

        echo -e "${color}${log_entry}${COLOR_RESET}"
    fi

    # File output
    if [[ -n "$LOG_FILE" ]]; then
        echo "$log_entry" >> "$LOG_FILE"
    fi

    # GitHub Actions annotation
    if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
        case "$level" in
            WARNING) echo "::warning::$message" ;;
            ERROR)   echo "::error::$message" ;;
        esac
    fi
}

function write_success() {
    # Write a success message
    # Usage: write_success "message"

    local message="$1"
    echo -e "${COLOR_GREEN}✅ $message${COLOR_RESET}"
    write_log "SUCCESS: $message" "INFO"
}

function write_failure() {
    # Write a failure message and exit
    # Usage: write_failure "message"

    local message="$1"
    echo -e "${COLOR_RED}❌ $message${COLOR_RESET}" >&2
    write_log "FAILURE: $message" "ERROR"
    exit 1
}

function write_step() {
    # Write a step header
    # Usage: write_step "message"

    local message="$1"
    echo ""
    echo -e "${COLOR_CYAN}═══════════════════════════════════════════════════════${COLOR_RESET}"
    echo -e "${COLOR_CYAN}  $message${COLOR_RESET}"
    echo -e "${COLOR_CYAN}═══════════════════════════════════════════════════════${COLOR_RESET}"
    write_log "STEP: $message" "INFO"
}

# Export functions (bash doesn't have formal exports, but we'll document them)
# Available functions:
# - initialize_logger
# - write_log
# - write_success
# - write_failure
# - write_step
