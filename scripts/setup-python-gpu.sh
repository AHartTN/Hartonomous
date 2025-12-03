#!/bin/bash
# Setup Python 3.13 environment with CUDA support for Hartonomous

set -e

echo "========================================="
echo "Setting up Python 3.13 + CUDA Environment"
echo "========================================="

# Check Python version
PYTHON_CMD="python3.13"
if ! command -v $PYTHON_CMD &> /dev/null; then
    PYTHON_CMD="python3"
    if ! command -v $PYTHON_CMD &> /dev/null; then
        echo "Error: Python 3.13 not found. Please install Python 3.13"
        exit 1
    fi
fi

PYTHON_VERSION=$($PYTHON_CMD --version | cut -d' ' -f2)
echo "Using Python: $PYTHON_VERSION"

# Check for CUDA
if command -v nvidia-smi &> /dev/null; then
    echo "CUDA detected:"
    nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader
else
    echo "WARNING: NVIDIA GPU not detected. CuPy will fall back to CPU."
fi

# Create virtual environment
VENV_DIR="../.venv"
if [ ! -d "$VENV_DIR" ]; then
    echo ""
    echo "Creating virtual environment..."
    $PYTHON_CMD -m venv $VENV_DIR
fi

# Activate virtual environment
source $VENV_DIR/bin/activate

# Upgrade pip
echo ""
echo "Upgrading pip..."
pip install --upgrade pip setuptools wheel

# Install dependencies
echo ""
echo "Installing Python dependencies..."
pip install -r ../requirements.txt

echo ""
echo "========================================="
echo "Python environment setup complete!"
echo "========================================="
echo ""
echo "To activate the environment, run:"
echo "  source .venv/bin/activate"
echo ""
echo "To test GPU availability:"
echo "  python -c 'import cupy; print(cupy.cuda.runtime.getDeviceCount())'"
