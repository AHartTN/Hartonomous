#!/bin/bash
set -e

echo "Installing Hartonomous systemd services..."

# Copy service files
sudo cp hartonomous-api.service /etc/systemd/system/
sudo cp hartonomous-worker.service /etc/systemd/system/

# Reload systemd
sudo systemctl daemon-reload

# Enable services (start on boot)
sudo systemctl enable hartonomous-api.service
sudo systemctl enable hartonomous-worker.service

echo "✓ Services installed and enabled"
echo ""
echo "To start services:"
echo "  sudo systemctl start hartonomous-api"
echo "  sudo systemctl start hartonomous-worker"
echo ""
echo "To check status:"
echo "  sudo systemctl status hartonomous-api"
echo "  sudo systemctl status hartonomous-worker"
echo ""
echo "To view logs:"
echo "  sudo journalctl -u hartonomous-api -f"
echo "  sudo journalctl -u hartonomous-worker -f"
