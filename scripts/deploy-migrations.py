#!/usr/bin/env python3
"""
Hartonomous - Python-driven idempotent database migration deployment
Cross-platform: Linux, macOS, Windows
"""

import os
import sys
import subprocess
import argparse
from pathlib import Path
from datetime import datetime


class MigrationDeployer:
    VALID_ENVIRONMENTS = ["localhost", "dev", "staging", "production"]

    def __init__(self, environment: str):
        if environment not in self.VALID_ENVIRONMENTS:
            raise ValueError(f"Invalid environment: {environment}. Must be one of {self.VALID_ENVIRONMENTS}")

        self.environment = environment
        self.script_dir = Path(__file__).parent
        self.project_root = self.script_dir.parent
        self.db_project = self.project_root / "Hartonomous.Db"
        self.migrations_dir = self.project_root / "migrations"

    def check_dotnet_installed(self) -> str:
        """Check if .NET SDK is installed and return version"""
        try:
            result = subprocess.run(
                ["dotnet", "--version"],
                capture_output=True,
                text=True,
                check=True
            )
            return result.stdout.strip()
        except (subprocess.CalledProcessError, FileNotFoundError):
            raise RuntimeError(".NET SDK not found. Please install .NET 10 SDK.")

    def generate_migration_script(self) -> Path:
        """Generate idempotent SQL migration script"""
        self.migrations_dir.mkdir(exist_ok=True)

        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        script_path = self.migrations_dir / f"migration-{self.environment}-{timestamp}.sql"

        print(f"\nGenerating idempotent migration script...")

        try:
            subprocess.run(
                [
                    "dotnet", "ef", "migrations", "script",
                    "--idempotent",
                    "--output", str(script_path),
                    "--project", "Hartonomous.Db.csproj"
                ],
                cwd=self.db_project,
                check=False  # Don't fail if no migrations exist
            )

            if script_path.exists():
                print(f"Migration script saved to: {script_path}")
                return script_path
        except Exception as e:
            print(f"Warning: Could not generate migration script: {e}")

        return None

    def apply_migrations(self) -> bool:
        """Apply EF Core migrations to database"""
        print(f"\nApplying migrations to {self.environment} database...")

        # Set environment variable
        env = os.environ.copy()
        env["ASPNETCORE_ENVIRONMENT"] = self.environment

        try:
            subprocess.run(
                [
                    "dotnet", "ef", "database", "update",
                    "--project", "Hartonomous.Db.csproj",
                    "--verbose"
                ],
                cwd=self.db_project,
                env=env,
                check=True
            )
            return True
        except subprocess.CalledProcessError as e:
            print(f"Error: Migration deployment failed with code {e.returncode}")
            return False

    def deploy(self) -> int:
        """Execute full deployment"""
        print("=" * 50)
        print(f"Deploying Migrations to: {self.environment}")
        print("=" * 50)

        try:
            # Check .NET SDK
            dotnet_version = self.check_dotnet_installed()
            print(f"\nUsing .NET version: {dotnet_version}")

            # Generate migration script (for auditing)
            self.generate_migration_script()

            # Apply migrations
            if not self.apply_migrations():
                return 1

            print("\n" + "=" * 50)
            print("Migration deployment completed successfully!")
            print(f"Environment: {self.environment}")
            print("=" * 50)
            return 0

        except Exception as e:
            print(f"\nError: {e}", file=sys.stderr)
            return 1


def main():
    parser = argparse.ArgumentParser(
        description="Deploy database migrations to specified environment"
    )
    parser.add_argument(
        "environment",
        nargs="?",
        default="localhost",
        choices=MigrationDeployer.VALID_ENVIRONMENTS,
        help="Target environment for deployment"
    )

    args = parser.parse_args()

    deployer = MigrationDeployer(args.environment)
    sys.exit(deployer.deploy())


if __name__ == "__main__":
    main()
