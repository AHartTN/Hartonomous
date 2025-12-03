#!/bin/bash
# Verify all prerequisites are installed for Hartonomous deployment

echo "Hartonomous Environment Verification"
echo "======================================"

MISSING=0

# Check .NET SDK
echo -n "Checking .NET SDK... "
if command -v dotnet &> /dev/null; then
    VERSION=$(dotnet --version)
    echo "OK ($VERSION)"
    if [[ ! $VERSION =~ ^10\. ]]; then
        echo "  WARNING: .NET 10 recommended, found $VERSION"
    fi
else
    echo "MISSING"
    MISSING=1
fi

# Check Docker
echo -n "Checking Docker... "
if command -v docker &> /dev/null; then
    VERSION=$(docker --version | cut -d' ' -f3 | tr -d ',')
    echo "OK ($VERSION)"
else
    echo "MISSING"
    MISSING=1
fi

# Check Python 3
echo -n "Checking Python 3... "
if command -v python3 &> /dev/null; then
    VERSION=$(python3 --version | cut -d' ' -f2)
    echo "OK ($VERSION)"
else
    echo "MISSING (optional)"
fi

# Check EF Core tools
echo -n "Checking EF Core tools... "
if dotnet tool list --global | grep -q "dotnet-ef"; then
    VERSION=$(dotnet ef --version | head -n1)
    echo "OK ($VERSION)"
else
    echo "MISSING"
    echo "  Install with: dotnet tool install --global dotnet-ef"
    MISSING=1
fi

# Check psql (optional)
echo -n "Checking PostgreSQL client... "
if command -v psql &> /dev/null; then
    VERSION=$(psql --version | cut -d' ' -f3)
    echo "OK ($VERSION)"
else
    echo "NOT INSTALLED (optional)"
fi

echo ""
if [ $MISSING -eq 0 ]; then
    echo "All required tools are installed!"
    exit 0
else
    echo "Some required tools are missing. Please install them."
    exit 1
fi
