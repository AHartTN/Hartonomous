#!/bin/bash
# ============================================================================
# Setup Docker Permissions (Non-root access)
# ============================================================================

set -e

echo "🔧 Setting up Docker permissions for user: $(whoami)"

# Check if docker group exists
if ! getent group docker > /dev/null 2>&1; then
    echo "⚠️  Docker group doesn't exist. Creating it..."
    sudo groupadd docker
fi

# Add user to docker group
if ! groups $(whoami) | grep -q docker; then
    echo "➕ Adding $(whoami) to docker group..."
    sudo usermod -aG docker $(whoami)
    echo "✅ User added to docker group"
    echo "⚠️  You need to log out and back in, or run: newgrp docker"
else
    echo "✅ User already in docker group"
fi

# Ensure docker socket has correct permissions
if [ -S /var/run/docker.sock ]; then
    echo "🔧 Setting docker socket permissions..."
    sudo chown root:docker /var/run/docker.sock
    sudo chmod 660 /var/run/docker.sock
    echo "✅ Docker socket permissions set"
fi

# Test docker access
if docker ps > /dev/null 2>&1; then
    echo "✅ Docker access confirmed"
else
    echo "⚠️  Docker access test failed. You may need to:"
    echo "   1. Log out and back in"
    echo "   2. Or run: newgrp docker"
    echo "   3. Or restart the docker service: sudo systemctl restart docker"
fi

echo ""
echo "✅ Docker permissions setup complete"
echo "   If still having issues, run: newgrp docker"
