#!/bin/bash
# ============================================================================
# Install Ollama on /var/workload
# Models will be stored on workload drive, then ingested into postgres
# ============================================================================

set -e  # Exit on error

OLLAMA_BASE="/var/workload/ollama"
OLLAMA_BIN="$OLLAMA_BASE/bin"
OLLAMA_MODELS="$OLLAMA_BASE/models"

echo "=== Installing Ollama to $OLLAMA_BASE ==="

# Create directories
echo "Creating directories..."
sudo mkdir -p "$OLLAMA_BIN" "$OLLAMA_MODELS" 2>/dev/null || true

# Download Ollama binary from GitHub releases
echo "Downloading Ollama binary from GitHub..."
OLLAMA_VERSION=$(curl -s https://api.github.com/repos/ollama/ollama/releases/latest | grep '"tag_name"' | cut -d'"' -f4)
echo "Latest version: $OLLAMA_VERSION"

# Download the tarball
cd /tmp
sudo curl -L "https://github.com/ollama/ollama/releases/download/${OLLAMA_VERSION}/ollama-linux-amd64.tgz" -o ollama.tgz

# Extract just the binary
echo "Extracting binary..."
sudo tar -xzf ollama.tgz -C "$OLLAMA_BIN" --strip-components=1 bin/ollama
sudo chmod +x "$OLLAMA_BIN/ollama"

# Cleanup
sudo rm -f ollama.tgz
cd - > /dev/null

# Set ownership to current user
echo "Setting ownership..."
sudo chown -R $USER:$USER "$OLLAMA_BASE" 2>/dev/null || true

# Create symlink to PATH
echo "Creating symlink to ~/.local/bin..."
mkdir -p ~/.local/bin
ln -sf "$OLLAMA_BIN/ollama" ~/.local/bin/ollama 2>/dev/null || true

# Set environment variable for model storage
echo "Configuring model storage location..."
if ! grep -q "OLLAMA_MODELS" ~/.bashrc; then
    echo "" >> ~/.bashrc
    echo "# Ollama configuration" >> ~/.bashrc
    echo "export OLLAMA_MODELS=\"$OLLAMA_MODELS\"" >> ~/.bashrc
    echo "export PATH=\"\$HOME/.local/bin:\$PATH\"" >> ~/.bashrc
fi

# Export for current session
export OLLAMA_MODELS="$OLLAMA_MODELS"
export PATH="$HOME/.local/bin:$PATH"

echo ""
echo "=== Ollama installed successfully! ==="
echo ""
echo "Location: $OLLAMA_BIN/ollama"
echo "Models:   $OLLAMA_MODELS"
echo ""
echo "To use in current session, run:"
echo "  source ~/.bashrc"
echo ""
echo "To start the Ollama service:"
echo "  ollama serve &"
echo ""
echo "To download a model (e.g., llama3):"
echo "  ollama pull llama3"
echo ""
echo "To test:"
echo "  ollama --version"
echo "  ollama list"
