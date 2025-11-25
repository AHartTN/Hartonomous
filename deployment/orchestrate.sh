#!/usr/bin/env bash
# ============================================================================
# Hartonomous Master Deployment Orchestrator
# Single entry point for all deployment operations
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# ============================================================================
# Helper Functions
# ============================================================================

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

show_usage() {
    cat << EOF
Hartonomous Deployment Orchestrator

Usage:
    $0 <command> [options]

Commands:
    preflight <env>     - Run preflight checks for environment
    deploy <env>        - Full deployment to environment
    db-only <env>       - Deploy database schema only
    api-only <env>      - Deploy API only
    validate <env>      - Run validation tests
    rollback <env>      - Rollback to previous deployment
    status <env>        - Check deployment status
    logs <env>          - View deployment logs

Environments:
    development (dev)
    staging (stage)
    production (prod)

Examples:
    $0 preflight dev
    $0 deploy production
    $0 validate staging
    $0 logs prod

Options:
    --skip-tests        Skip test execution
    --skip-backup       Skip backup creation
    --force             Force deployment without prompts
    --help              Show this help message

Environment Variables:
    DEPLOYMENT_ENVIRONMENT  - Target environment
    AZURE_TENANT_ID        - Azure tenant ID
    AZURE_CLIENT_ID        - Azure client ID
    AZURE_CLIENT_SECRET    - Azure client secret
    LOG_LEVEL              - Logging level (DEBUG, INFO, WARNING, ERROR)

EOF
}

check_prerequisites() {
    log_info "Checking prerequisites..."
    
    local missing=0
    
    # Check required tools
    for tool in az python3 psql git; do
        if ! command -v $tool &> /dev/null; then
            log_error "$tool is not installed"
            missing=$((missing + 1))
        fi
    done
    
    # Check environment variables
    if [[ -z "${AZURE_TENANT_ID:-}" ]]; then
        log_warning "AZURE_TENANT_ID not set"
    fi
    
    if [[ $missing -gt 0 ]]; then
        log_error "$missing required tool(s) missing"
        return 1
    fi
    
    log_success "All prerequisites met"
    return 0
}

normalize_env() {
    local env=$1
    case "$env" in
        dev|development) echo "development" ;;
        stage|staging) echo "staging" ;;
        prod|production) echo "production" ;;
        *) echo "$env" ;;
    esac
}

# ============================================================================
# Commands
# ============================================================================

cmd_preflight() {
    local env=$(normalize_env "$1")
    
    log_info "Running preflight checks for: $env"
    
    export DEPLOYMENT_ENVIRONMENT="$env"
    
    # Run prerequisites check
    "$SCRIPT_DIR/scripts/preflight/check-prerequisites.sh"
    
    log_success "Preflight checks passed"
}

cmd_deploy() {
    local env=$(normalize_env "$1")
    shift
    
    local skip_tests=false
    local skip_backup=false
    local force=false
    
    # Parse options
    while [[ $# -gt 0 ]]; do
        case $1 in
            --skip-tests) skip_tests=true; shift ;;
            --skip-backup) skip_backup=true; shift ;;
            --force) force=true; shift ;;
            *) log_error "Unknown option: $1"; return 1 ;;
        esac
    done
    
    log_info "==================================="
    log_info "Hartonomous Deployment"
    log_info "Environment: $env"
    log_info "==================================="
    
    # Confirmation prompt
    if [[ "$force" != "true" && "$env" == "production" ]]; then
        read -p "Deploy to PRODUCTION? (yes/no): " confirm
        if [[ "$confirm" != "yes" ]]; then
            log_warning "Deployment cancelled"
            return 0
        fi
    fi
    
    export DEPLOYMENT_ENVIRONMENT="$env"
    
    # Step 1: Preflight
    log_info "Step 1/6: Preflight checks"
    "$SCRIPT_DIR/scripts/preflight/check-prerequisites.sh"
    
    # Step 2: Backup
    if [[ "$skip_backup" != "true" ]]; then
        log_info "Step 2/6: Creating backup"
        "$SCRIPT_DIR/scripts/database/backup-database.sh"
        "$SCRIPT_DIR/scripts/application/backup-application.sh"
    else
        log_warning "Step 2/6: Skipping backup"
    fi
    
    # Step 3: Database
    log_info "Step 3/6: Deploying database schema"
    "$SCRIPT_DIR/scripts/database/migrate.sh" -e "$ENVIRONMENT"
    
    # Step 4: Application
    log_info "Step 4/6: Deploying API"
    "$SCRIPT_DIR/scripts/application/deploy-api.sh"
    
    # Step 5: Neo4j Worker
    log_info "Step 5/6: Deploying Neo4j worker"
    if [[ -f "$SCRIPT_DIR/scripts/neo4j/deploy-neo4j-worker.sh" ]]; then
        "$SCRIPT_DIR/scripts/neo4j/deploy-neo4j-worker.sh"
    else
        log_warning "Neo4j worker deployment script not found (skipping)"
    fi
    
    # Step 6: Validation
    log_info "Step 6/6: Running validation"
    "$SCRIPT_DIR/scripts/validation/health-check.sh"
    
    if [[ "$skip_tests" != "true" ]]; then
        if [[ -f "$SCRIPT_DIR/scripts/validation/smoke-test.sh" ]]; then
            "$SCRIPT_DIR/scripts/validation/smoke-test.sh"
        else
            log_warning "Smoke test script not found (skipping)"
        fi
    fi
    
    log_success "==================================="
    log_success "Deployment completed successfully!"
    log_success "==================================="
}

cmd_db_only() {
    local env=$(normalize_env "$1")
    
    log_info "Deploying database schema to: $env"
    
    export DEPLOYMENT_ENVIRONMENT="$env"
    
    "$SCRIPT_DIR/scripts/database/backup-database.sh"
    "$SCRIPT_DIR/scripts/database/deploy-schema.sh"
    
    log_success "Database deployment complete"
}

cmd_api_only() {
    local env=$(normalize_env "$1")
    
    log_info "Deploying API to: $env"
    
    export DEPLOYMENT_ENVIRONMENT="$env"
    
    "$SCRIPT_DIR/scripts/application/backup-application.sh"
    "$SCRIPT_DIR/scripts/application/deploy-api.sh"
    
    log_success "API deployment complete"
}

cmd_validate() {
    local env=$(normalize_env "$1")
    
    log_info "Running validation for: $env"
    
    export DEPLOYMENT_ENVIRONMENT="$env"
    
    "$SCRIPT_DIR/scripts/validation/health-check.sh"
    
    log_success "Validation passed"
}

cmd_status() {
    local env=$(normalize_env "$1")
    
    log_info "Checking deployment status for: $env"
    
    # Check API health
    local config_file="$REPO_ROOT/deployment/config/${env}.json"
    if [[ -f "$config_file" ]]; then
        local api_host=$(jq -r '.api.host' "$config_file")
        local api_port=$(jq -r '.api.port' "$config_file")
        
        log_info "API endpoint: http://${api_host}:${api_port}"
        
        if curl -sf "http://${api_host}:${api_port}/v1/health" > /dev/null 2>&1; then
            log_success "API is healthy"
        else
            log_error "API health check failed"
        fi
    else
        log_error "Config file not found: $config_file"
    fi
}

cmd_logs() {
    local env=$(normalize_env "$1")
    
    log_info "Viewing logs for: $env"
    
    local log_dir="/var/log/hartonomous"
    
    if [[ -d "$log_dir" ]]; then
        tail -f "$log_dir"/*.log
    else
        log_error "Log directory not found: $log_dir"
    fi
}

# ============================================================================
# Main
# ============================================================================

main() {
    if [[ $# -eq 0 ]]; then
        show_usage
        exit 0
    fi
    
    local command=$1
    shift
    
    case "$command" in
        preflight)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_preflight "$@"
            ;;
        deploy)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_deploy "$@"
            ;;
        db-only)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_db_only "$@"
            ;;
        api-only)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_api_only "$@"
            ;;
        validate)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_validate "$@"
            ;;
        status)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_status "$@"
            ;;
        logs)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            cmd_logs "$@"
            ;;
        rollback)
            if [[ $# -eq 0 ]]; then
                log_error "Environment required"
                show_usage
                exit 1
            fi
            
            ENV=$(normalize_env "$1")
            shift
            
            # Parse rollback options
            FORCE=false
            while [[ $# -gt 0 ]]; do
                case $1 in
                    --force) FORCE=true; shift ;;
                    *) log_error "Unknown option: $1"; exit 1 ;;
                esac
            done
            
            log_warning "========================================="
            log_warning "ROLLBACK DEPLOYMENT"
            log_warning "Environment: $ENV"
            log_warning "========================================="
            
            if [[ "$FORCE" != "true" && "$ENV" == "production" ]]; then
                read -p "Rollback PRODUCTION deployment? This will restore backups. Type 'rollback production' to confirm: " confirm
                if [[ "$confirm" != "rollback production" ]]; then
                    log_warning "Rollback cancelled"
                    exit 0
                fi
            fi
            
            export DEPLOYMENT_ENVIRONMENT="$ENV"
            
            if [[ -f "$SCRIPT_DIR/scripts/rollback/rollback-deployment.sh" ]]; then
                "$SCRIPT_DIR/scripts/rollback/rollback-deployment.sh"
            else
                log_error "Rollback script not found: $SCRIPT_DIR/scripts/rollback/rollback-deployment.sh"
                exit 1
            fi
            ;;
        --help|-h|help)
            show_usage
            exit 0
            ;;
        *)
            log_error "Unknown command: $command"
            show_usage
            exit 1
            ;;
    esac
}

# Check prerequisites before running
if ! check_prerequisites; then
    exit 1
fi

main "$@"
