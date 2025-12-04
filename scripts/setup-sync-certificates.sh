#!/bin/bash
#
# setup-sync-certificates.sh - Idempotent SSL Certificate Sync from Router
#
# This script syncs Let's Encrypt certificates from a central ACME router to deployed servers.
# The router manages certificate renewals via ACME protocol (acme.sh client).
# This script is idempotent and safe to run multiple times.
#
# Router Configuration:
#   - Host: 192.168.1.1
#   - SSH Port: 22122
#   - Certificate Location: /etc/ssl/acme/hartonomous.com.*
#   - ACME Directory: /etc/acme/hartonomous.com_ecc/
#
# Certificate Files:
#   - fullchain.cer: Complete certificate chain (cert + intermediates)
#   - hartonomous.com.key: Private key
#   - ca.cer: CA certificate chain
#   - hartonomous.com.cer: End-entity certificate only
#
# Security:
#   - Uses SSH key authentication (passwordless)
#   - Preserves file permissions (private key 600, certificates 644)
#   - Validates certificate before nginx reload
#
# Prerequisites:
#   - SSH key configured for root@192.168.1.1:22122
#   - nginx installed and configured
#   - openssl installed for certificate validation
#

set -euo pipefail

# Configuration
ROUTER_HOST="192.168.1.1"
ROUTER_PORT="22122"
ROUTER_USER="root"
ROUTER_CERT_DIR="/etc/ssl/acme"
ROUTER_DOMAIN="hartonomous.com"

LOCAL_CERT_DIR="/etc/ssl/certs"
LOCAL_KEY_DIR="/etc/ssl/private"
LOCAL_DOMAIN="hartonomous.com"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to ensure SSH key authentication is configured
ensure_ssh_key() {
    log_info "Checking SSH key authentication to router..."
    
    if [ ! -f ~/.ssh/id_rsa ] && [ ! -f ~/.ssh/id_ed25519 ]; then
        log_warn "No SSH key found. Generating ED25519 key..."
        ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519 -N "" -C "certificate-sync@$(hostname)"
    fi
    
    # Test SSH connection
    if ! ssh -p "${ROUTER_PORT}" -o ConnectTimeout=5 -o StrictHostKeyChecking=accept-new "${ROUTER_USER}@${ROUTER_HOST}" "echo 'SSH connection successful'" 2>/dev/null; then
        log_error "Cannot connect to router via SSH. Please configure SSH key authentication:"
        log_error "1. Copy public key: ssh-copy-id -p ${ROUTER_PORT} ${ROUTER_USER}@${ROUTER_HOST}"
        log_error "2. Or manually add this key to /root/.ssh/authorized_keys on router:"
        if [ -f ~/.ssh/id_ed25519.pub ]; then
            cat ~/.ssh/id_ed25519.pub
        elif [ -f ~/.ssh/id_rsa.pub ]; then
            cat ~/.ssh/id_rsa.pub
        fi
        return 1
    fi
    
    log_info "SSH key authentication to router is working"
    return 0
}

# Function to sync certificates from router
sync_certificates() {
    log_info "Syncing certificates from router ${ROUTER_HOST}:${ROUTER_PORT}..."
    
    # Create local directories if they don't exist
    mkdir -p "${LOCAL_CERT_DIR}"
    mkdir -p "${LOCAL_KEY_DIR}"
    
    # Create temporary directory for downloads
    TEMP_DIR=$(mktemp -d)
    trap "rm -rf ${TEMP_DIR}" EXIT
    
    # Download certificate files from router via SCP
    log_info "Downloading certificate files..."
    
    # Download fullchain certificate (cert + intermediates)
    if ! scp -P "${ROUTER_PORT}" \
        "${ROUTER_USER}@${ROUTER_HOST}:${ROUTER_CERT_DIR}/${ROUTER_DOMAIN}.fullchain.crt" \
        "${TEMP_DIR}/fullchain.crt" 2>/dev/null; then
        log_error "Failed to download fullchain certificate from router"
        return 1
    fi
    
    # Download private key
    if ! scp -P "${ROUTER_PORT}" \
        "${ROUTER_USER}@${ROUTER_HOST}:${ROUTER_CERT_DIR}/${ROUTER_DOMAIN}.key" \
        "${TEMP_DIR}/privkey.pem" 2>/dev/null; then
        log_error "Failed to download private key from router"
        return 1
    fi
    
    # Download CA certificate chain (optional, for verification)
    if ! scp -P "${ROUTER_PORT}" \
        "${ROUTER_USER}@${ROUTER_HOST}:${ROUTER_CERT_DIR}/${ROUTER_DOMAIN}.chain.crt" \
        "${TEMP_DIR}/chain.crt" 2>/dev/null; then
        log_warn "CA chain certificate not available, using fullchain only"
    fi
    
    log_info "Certificate files downloaded successfully"
    
    # Validate certificate before installing
    log_info "Validating certificate..."
    
    # Check certificate is valid X.509
    if ! openssl x509 -in "${TEMP_DIR}/fullchain.crt" -noout -text >/dev/null 2>&1; then
        log_error "Invalid certificate format"
        return 1
    fi
    
    # Check private key matches certificate
    CERT_MODULUS=$(openssl x509 -in "${TEMP_DIR}/fullchain.crt" -noout -modulus 2>/dev/null | openssl md5)
    KEY_MODULUS=$(openssl rsa -in "${TEMP_DIR}/privkey.pem" -noout -modulus 2>/dev/null | openssl md5 2>/dev/null) || \
        KEY_MODULUS=$(openssl ec -in "${TEMP_DIR}/privkey.pem" -noout -text 2>/dev/null | grep -A 3 "pub:" | openssl md5)
    
    if [ -n "${CERT_MODULUS}" ] && [ "${CERT_MODULUS}" != "${KEY_MODULUS}" ]; then
        log_error "Certificate and private key do not match"
        return 1
    fi
    
    # Check certificate expiration
    EXPIRY_DATE=$(openssl x509 -in "${TEMP_DIR}/fullchain.crt" -noout -enddate | cut -d= -f2)
    EXPIRY_EPOCH=$(date -d "${EXPIRY_DATE}" +%s 2>/dev/null || date -j -f "%b %d %T %Y %Z" "${EXPIRY_DATE}" +%s 2>/dev/null)
    CURRENT_EPOCH=$(date +%s)
    DAYS_UNTIL_EXPIRY=$(( (EXPIRY_EPOCH - CURRENT_EPOCH) / 86400 ))
    
    log_info "Certificate expires in ${DAYS_UNTIL_EXPIRY} days (${EXPIRY_DATE})"
    
    if [ ${DAYS_UNTIL_EXPIRY} -lt 0 ]; then
        log_error "Certificate has already expired!"
        return 1
    elif [ ${DAYS_UNTIL_EXPIRY} -lt 7 ]; then
        log_warn "Certificate expires in less than 7 days!"
    fi
    
    # Check if certificate has changed
    CERT_CHANGED=0
    if [ -f "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt" ]; then
        if ! cmp -s "${TEMP_DIR}/fullchain.crt" "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt"; then
            CERT_CHANGED=1
            log_info "Certificate has changed, will update"
        else
            log_info "Certificate is already up to date"
        fi
    else
        CERT_CHANGED=1
        log_info "Installing certificate for the first time"
    fi
    
    # Install certificates if changed
    if [ ${CERT_CHANGED} -eq 1 ]; then
        # Install fullchain certificate
        cp "${TEMP_DIR}/fullchain.crt" "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt"
        chmod 644 "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt"
        log_info "Installed fullchain certificate to ${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt"
        
        # Install private key
        cp "${TEMP_DIR}/privkey.pem" "${LOCAL_KEY_DIR}/${LOCAL_DOMAIN}.key"
        chmod 600 "${LOCAL_KEY_DIR}/${LOCAL_DOMAIN}.key"
        log_info "Installed private key to ${LOCAL_KEY_DIR}/${LOCAL_DOMAIN}.key"
        
        # Install CA chain if available
        if [ -f "${TEMP_DIR}/chain.crt" ]; then
            cp "${TEMP_DIR}/chain.crt" "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.chain.crt"
            chmod 644 "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.chain.crt"
            log_info "Installed CA chain certificate to ${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.chain.crt"
        fi
        
        # Create symlinks for compatibility with different naming conventions
        ln -sf "${LOCAL_DOMAIN}.fullchain.crt" "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.crt"
        ln -sf "${LOCAL_DOMAIN}.fullchain.crt" "${LOCAL_CERT_DIR}/fullchain.pem"
        ln -sf "${LOCAL_DOMAIN}.key" "${LOCAL_KEY_DIR}/privkey.pem"
        
        log_info "Certificates installed successfully"
        return 0
    else
        log_info "No certificate update needed"
        return 2
    fi
}

# Function to update nginx configuration to use synced certificates
update_nginx_config() {
    log_info "Checking nginx configuration..."
    
    # Check if nginx is installed
    if ! command -v nginx >/dev/null 2>&1; then
        log_warn "nginx is not installed, skipping configuration"
        return 0
    fi
    
    # Find nginx configuration files
    NGINX_CONF_DIR="/etc/nginx"
    if [ ! -d "${NGINX_CONF_DIR}" ]; then
        log_warn "nginx configuration directory not found, skipping"
        return 0
    fi
    
    # Update SSL certificate paths in nginx configs (if not already set)
    find "${NGINX_CONF_DIR}" -type f -name "*.conf" | while read -r conf_file; do
        if grep -q "ssl_certificate" "${conf_file}" 2>/dev/null; then
            # Check if already pointing to our synced certificates
            if ! grep -q "${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt" "${conf_file}" 2>/dev/null; then
                log_info "Nginx config ${conf_file} may need manual update to use:"
                log_info "  ssl_certificate ${LOCAL_CERT_DIR}/${LOCAL_DOMAIN}.fullchain.crt;"
                log_info "  ssl_certificate_key ${LOCAL_KEY_DIR}/${LOCAL_DOMAIN}.key;"
            fi
        fi
    done
    
    return 0
}

# Function to reload nginx if certificates changed
reload_nginx() {
    local cert_changed=$1
    
    if [ ${cert_changed} -ne 1 ]; then
        log_info "Certificates unchanged, skipping nginx reload"
        return 0
    fi
    
    log_info "Reloading nginx to use updated certificates..."
    
    # Check if nginx is installed and running
    if ! command -v nginx >/dev/null 2>&1; then
        log_warn "nginx is not installed, skipping reload"
        return 0
    fi
    
    # Test nginx configuration
    if ! nginx -t 2>/dev/null; then
        log_error "nginx configuration test failed, not reloading"
        return 1
    fi
    
    # Reload nginx
    if systemctl is-active --quiet nginx; then
        systemctl reload nginx
        log_info "nginx reloaded successfully"
    elif [ -f /var/run/nginx.pid ]; then
        nginx -s reload
        log_info "nginx reloaded successfully"
    else
        log_warn "nginx is not running, skipping reload"
    fi
    
    return 0
}

# Function to setup automated certificate sync
setup_automation() {
    log_info "Setting up automated certificate sync..."
    
    # Create systemd service for certificate sync
    cat > /etc/systemd/system/cert-sync.service <<'EOF'
[Unit]
Description=Sync SSL Certificates from Router
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/setup-sync-certificates.sh --auto
StandardOutput=journal
StandardError=journal
SyslogIdentifier=cert-sync

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/etc/ssl/certs /etc/ssl/private
EOF
    
    # Create systemd timer for daily certificate sync
    cat > /etc/systemd/system/cert-sync.timer <<EOF
[Unit]
Description=Daily SSL Certificate Sync from Router
Requires=cert-sync.service

[Timer]
# Run daily at 3 AM
OnCalendar=daily
OnCalendar=*-*-* 03:00:00
# Also run 5 minutes after boot
OnBootSec=5min
# Randomize execution time by up to 1 hour to avoid thundering herd
RandomizedDelaySec=3600
Persistent=true

[Install]
WantedBy=timers.target
EOF
    
    # Copy this script to /usr/local/bin for systemd service
    cp "$0" /usr/local/bin/setup-sync-certificates.sh
    chmod +x /usr/local/bin/setup-sync-certificates.sh
    
    # Reload systemd and enable timer
    systemctl daemon-reload
    systemctl enable cert-sync.timer
    systemctl start cert-sync.timer
    
    log_info "Automated certificate sync timer enabled"
    log_info "Certificates will be synced daily at 3 AM and 5 minutes after boot"
    log_info "Check timer status: systemctl status cert-sync.timer"
    log_info "Check last sync: journalctl -u cert-sync.service"
    
    return 0
}

# Main execution
main() {
    log_info "=== SSL Certificate Sync from Router ==="
    log_info "Router: ${ROUTER_USER}@${ROUTER_HOST}:${ROUTER_PORT}"
    log_info "Domain: ${ROUTER_DOMAIN}"
    
    # Check if running in automated mode
    AUTO_MODE=0
    if [ "${1:-}" = "--auto" ]; then
        AUTO_MODE=1
        log_info "Running in automated mode"
    fi
    
    # Ensure SSH key authentication
    if ! ensure_ssh_key; then
        log_error "SSH key authentication setup failed"
        exit 1
    fi
    
    # Sync certificates from router
    sync_certificates
    SYNC_RESULT=$?
    
    if [ ${SYNC_RESULT} -eq 1 ]; then
        log_error "Certificate sync failed"
        exit 1
    fi
    
    # Update nginx configuration
    update_nginx_config
    
    # Reload nginx if certificates changed
    if [ ${SYNC_RESULT} -eq 0 ]; then
        reload_nginx 1
    else
        reload_nginx 0
    fi
    
    # Setup automation if not in auto mode and not already set up
    if [ ${AUTO_MODE} -eq 0 ]; then
        if [ ! -f /etc/systemd/system/cert-sync.timer ]; then
            setup_automation
        else
            log_info "Automated sync already configured"
            log_info "Timer status: $(systemctl is-active cert-sync.timer)"
            log_info "Next run: $(systemctl status cert-sync.timer | grep 'Trigger:' | awk '{print $2, $3, $4}')"
        fi
    fi
    
    log_info "=== Certificate sync completed successfully ==="
    
    return 0
}

# Run main function
main "$@"
