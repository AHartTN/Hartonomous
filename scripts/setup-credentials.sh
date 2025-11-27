#!/bin/bash
# ============================================================================
# Setup credentials for HuggingFace and Ollama
# Sources credentials from .env.local
# ============================================================================

set -e

# Detect machine-specific env file
if [ -f ".env.hart-server" ]; then
    ENV_FILE=".env.hart-server"
elif [ -f ".env.hart-desktop" ]; then
    ENV_FILE=".env.hart-desktop"
elif [ -f ".env.local" ]; then
    ENV_FILE=".env.local"
else
    echo "❌ Error: No environment file found"
    echo "Please create one of: .env.hart-server, .env.hart-desktop, or .env.local"
    echo "Use .env.example as a template"
    exit 1
fi

echo "=== Setting up credentials from $ENV_FILE ==="

# Load environment variables
set -a  # Export all variables
source "$ENV_FILE"
set +a

# Setup HuggingFace CLI credentials
echo "Configuring HuggingFace..."
mkdir -p ~/.huggingface
echo "$HUGGINGFACE_TOKEN" > ~/.huggingface/token
chmod 600 ~/.huggingface/token
echo "✅ HuggingFace token configured"

# Update bashrc with environment variables
echo "Updating shell configuration..."
if ! grep -q "# Hartonomous AI Credentials" ~/.bashrc; then
    cat >> ~/.bashrc << 'EOF'

# Hartonomous AI Credentials
# Auto-detect machine-specific environment file
HARTONOMOUS_DIR=""
if [ -d "$HOME/Repositories/Github/AHartTN/Hartonomous" ]; then
    HARTONOMOUS_DIR="$HOME/Repositories/Github/AHartTN/Hartonomous"
elif [ -d "/var/workload/Repositories/Github/AHartTN/Hartonomous" ]; then
    HARTONOMOUS_DIR="/var/workload/Repositories/Github/AHartTN/Hartonomous"
fi

if [ -n "$HARTONOMOUS_DIR" ]; then
    if [ -f "$HARTONOMOUS_DIR/.env.hart-server" ]; then
        set -a
        source "$HARTONOMOUS_DIR/.env.hart-server"
        set +a
    elif [ -f "$HARTONOMOUS_DIR/.env.hart-desktop" ]; then
        set -a
        source "$HARTONOMOUS_DIR/.env.hart-desktop"
        set +a
    elif [ -f "$HARTONOMOUS_DIR/.env.local" ]; then
        set -a
        source "$HARTONOMOUS_DIR/.env.local"
        set +a
    fi
fi
EOF
    echo "✅ Added credential loading to ~/.bashrc"
else
    echo "✅ Credential loading already configured in ~/.bashrc"
fi

# Create Ollama config directory
mkdir -p ~/.ollama
cat > ~/.ollama/config.json << EOF
{
  "api_key": "$OLLAMA_API_KEY",
  "host": "$OLLAMA_HOST"
}
EOF
chmod 600 ~/.ollama/config.json
echo "✅ Ollama configuration saved"

echo ""
echo "=== Credentials configured successfully! ==="
echo ""
echo "To use in current session:"
echo "  source ~/.bashrc"
echo ""
echo "Configured:"
echo "  ✅ HuggingFace token (~/.huggingface/token)"
echo "  ✅ Ollama API key (~/.ollama/config.json)"
echo "  ✅ Environment variables (~/.bashrc)"
echo ""
echo "Test with:"
echo "  echo \$HUGGINGFACE_TOKEN"
echo "  echo \$OLLAMA_API_KEY"
