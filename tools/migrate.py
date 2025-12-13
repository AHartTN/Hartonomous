#!/usr/bin/env python3
"""Database migration runner"""

import sys
import os
from pathlib import Path
import psycopg2


class MigrationRunner:
    """Manage database schema migrations"""
    
    def __init__(self, connection_string: str):
        self.conn_string = connection_string
        self.migrations_dir = Path(__file__).parent.parent / 'database' / 'migrations'
    
    def init_migration_table(self):
        """Create migrations tracking table"""
        with psycopg2.connect(self.conn_string) as conn:
            with conn.cursor() as cur:
                cur.execute("""
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        id SERIAL PRIMARY KEY,
                        migration_name VARCHAR(255) NOT NULL UNIQUE,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                    );
                """)
                conn.commit()
    
    def get_applied_migrations(self) -> set:
        """Get list of already applied migrations"""
        with psycopg2.connect(self.conn_string) as conn:
            with conn.cursor() as cur:
                cur.execute("SELECT migration_name FROM schema_migrations;")
                return {row[0] for row in cur.fetchall()}
    
    def get_pending_migrations(self) -> list:
        """Get list of migrations to apply"""
        if not self.migrations_dir.exists():
            return []
        
        applied = self.get_applied_migrations()
        all_migrations = sorted(
            f for f in self.migrations_dir.glob('*.sql')
        )
        
        return [m for m in all_migrations if m.name not in applied]
    
    def apply_migration(self, migration_file: Path):
        """Apply single migration"""
        print(f"Applying {migration_file.name}...", end=' ')
        
        with open(migration_file, 'r') as f:
            sql = f.read()
        
        with psycopg2.connect(self.conn_string) as conn:
            with conn.cursor() as cur:
                try:
                    cur.execute(sql)
                    cur.execute(
                        "INSERT INTO schema_migrations (migration_name) VALUES (%s);",
                        (migration_file.name,)
                    )
                    conn.commit()
                    print("✓")
                except Exception as e:
                    conn.rollback()
                    print(f"✗\nError: {e}")
                    raise
    
    def run(self):
        """Run all pending migrations"""
        self.init_migration_table()
        
        pending = self.get_pending_migrations()
        
        if not pending:
            print("No pending migrations.")
            return
        
        print(f"Found {len(pending)} pending migrations:")
        for m in pending:
            print(f"  - {m.name}")
        
        print("\nApplying migrations...")
        for migration in pending:
            self.apply_migration(migration)
        
        print(f"\n✓ Applied {len(pending)} migrations successfully.")


def main():
    # Build connection string from environment
    conn_string = " ".join([
        f"host={os.getenv('PGHOST', 'localhost')}",
        f"port={os.getenv('PGPORT', '5432')}",
        f"dbname={os.getenv('PGDATABASE', 'hartonomous')}",
        f"user={os.getenv('PGUSER', 'postgres')}",
        f"password={os.getenv('PGPASSWORD', '')}",
    ])
    
    runner = MigrationRunner(conn_string)
    
    try:
        runner.run()
    except Exception as e:
        print(f"\nMigration failed: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
