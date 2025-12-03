#!/bin/bash
set -e

# Hartonomous - Cross-platform idempotent database migration deployment
# Works on Linux, macOS, and Windows (via Git Bash/WSL)

ENVIRONMENT="${1:-localhost}"
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "========================================="
echo "Deploying Migrations to: $ENVIRONMENT"
echo "========================================="

# Validate environment
case $ENVIRONMENT in
    localhost|dev|staging|production)
        ;;
    *)
        echo "Error: Invalid environment '$ENVIRONMENT'"
        echo "Usage: $0 [localhost|dev|staging|production]"
        exit 1
        ;;
esac

# Set environment variable
export ASPNETCORE_ENVIRONMENT=$ENVIRONMENT

# Navigate to project root
cd "$PROJECT_ROOT"

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Please install .NET 10 SDK."
    exit 1
fi

# Verify .NET version
DOTNET_VERSION=$(dotnet --version)
echo "Using .NET version: $DOTNET_VERSION"

# Generate idempotent SQL script (optional - for auditing)
echo ""
echo "Generating idempotent migration script..."
cd Hartonomous.Db
dotnet ef migrations script --idempotent --output "../migrations/migration-${ENVIRONMENT}-$(date +%Y%m%d-%H%M%S).sql" --project Hartonomous.Db.csproj || true

# Apply migrations
echo ""
echo "Applying migrations to $ENVIRONMENT database..."
dotnet ef database update --project Hartonomous.Db.csproj --verbose

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================="
    echo "Migration deployment completed successfully!"
    echo "Environment: $ENVIRONMENT"
    echo "========================================="
else
    echo ""
    echo "Error: Migration deployment failed!"
    exit 1
fi
