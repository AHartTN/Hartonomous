#!/bin/bash
set -e

echo "Configuring Redis for Hartonomous..."

# Redis is already installed and running
# Additional configuration for production

# Backup original config
if [ ! -f /etc/redis/redis.conf.hartonomous-backup ]; then
    sudo cp /etc/redis/redis.conf /etc/redis/redis.conf.hartonomous-backup
fi

# Apply production settings
echo "Applying production Redis configuration..."

# Set maxmemory policy (evict least recently used keys when memory limit reached)
sudo sed -i 's/^# maxmemory-policy.*/maxmemory-policy allkeys-lru/' /etc/redis/redis.conf

# Set reasonable maxmemory (16GB for 128GB volume)
sudo sed -i 's/^# maxmemory.*/maxmemory 16gb/' /etc/redis/redis.conf

# Enable persistence (both RDB and AOF for durability)
sudo sed -i 's/^appendonly no/appendonly yes/' /etc/redis/redis.conf

# Set append-only file sync to once per second (good balance)
sudo sed -i 's/^# appendfsync.*/appendfsync everysec/' /etc/redis/redis.conf

# Disable protected mode for local network access (if needed)
# Uncomment the next line if you need network access
# sudo sed -i 's/^protected-mode yes/protected-mode no/' /etc/redis/redis.conf

# Restart Redis to apply changes
echo "Restarting Redis..."
sudo systemctl restart redis-server

# Wait for Redis to start
sleep 2

# Test connection
if redis-cli ping | grep -q PONG; then
    echo ""
    echo "✓ Redis configured successfully!"
    echo ""
    echo "Redis configuration:"
    echo "  Host: localhost"
    echo "  Port: 6379"
    echo "  Data directory: /var/lib/redis (128GB volume)"
    echo "  Max memory: 16GB"
    echo "  Persistence: RDB + AOF enabled"
    echo ""
    echo "Connection string:"
    echo "  localhost:6379,abortConnect=false"
    echo ""
    echo "To test Redis:"
    echo "  redis-cli ping"
    echo "  redis-cli info memory"
    echo ""
else
    echo "❌ Redis connection test failed!"
    exit 1
fi
