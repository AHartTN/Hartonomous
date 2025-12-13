"""
Data migration tool for Hartonomous schema evolution.
Safely migrates atoms between schema versions.
"""

import psycopg2
from psycopg2.extras import RealDictCursor
import json
import sys
from typing import Dict, List, Callable

class SchemaMigrator:
    """
    Manages schema version migrations with data preservation.
    """
    
    def __init__(self, conn):
        """
        Initialize migrator.
        
        Args:
            conn: psycopg2 connection to target database
        """
        self.conn = conn
        self._ensure_version_table()
    
    def _ensure_version_table(self):
        """Create schema version tracking table."""
        cur = self.conn.cursor()
        cur.execute("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                description TEXT
            )
        """)
        self.conn.commit()
        cur.close()
    
    def get_current_version(self) -> int:
        """Get currently applied schema version."""
        cur = self.conn.cursor()
        cur.execute("SELECT COALESCE(MAX(version), 0) FROM schema_version")
        version = cur.fetchone()[0]
        cur.close()
        return version
    
    def apply_migration(self, version: int, description: str, 
                       migrate_func: Callable):
        """
        Apply a migration function.
        
        Args:
            version: Target version number
            description: Migration description
            migrate_func: Function(conn) that performs migration
        """
        current = self.get_current_version()
        
        if version <= current:
            print(f"⏭️  Migration {version} already applied (current: {current})")
            return
        
        print(f"🔄 Applying migration {version}: {description}")
        
        try:
            # Execute migration in transaction
            migrate_func(self.conn)
            
            # Record version
            cur = self.conn.cursor()
            cur.execute("""
                INSERT INTO schema_version (version, description)
                VALUES (%s, %s)
            """, (version, description))
            
            self.conn.commit()
            cur.close()
            
            print(f"✅ Migration {version} completed")
            
        except Exception as e:
            self.conn.rollback()
            print(f"❌ Migration {version} failed: {e}")
            raise
    
    def rollback_migration(self, version: int):
        """
        Rollback to previous version (requires manual data restoration).
        
        Args:
            version: Version to rollback to
        """
        current = self.get_current_version()
        
        if version >= current:
            print(f"Cannot rollback to {version} (current: {current})")
            return
        
        print(f"⚠️  WARNING: Rollback requires manual data restoration from backup")
        print(f"   Rolling back from v{current} to v{version}")
        
        confirm = input("Type 'ROLLBACK' to confirm: ")
        if confirm != "ROLLBACK":
            print("Rollback cancelled")
            return
        
        cur = self.conn.cursor()
        cur.execute("DELETE FROM schema_version WHERE version > %s", (version,))
        self.conn.commit()
        cur.close()
        
        print(f"✅ Version rolled back to {version}")
        print(f"   Restore data from backup before applying new migrations")

# Example migrations
def migration_001_add_metadata_jsonb(conn):
    """Add JSONB metadata column if missing."""
    cur = conn.cursor()
    
    # Check if column exists
    cur.execute("""
        SELECT column_name 
        FROM information_schema.columns 
        WHERE table_name = 'atom' AND column_name = 'metadata'
    """)
    
    if cur.fetchone() is None:
        cur.execute("ALTER TABLE atom ADD COLUMN metadata JSONB")
        print("   Added metadata column")
    
    cur.close()

def migration_002_add_constituents_array(conn):
    """Add constituents array for composition tracking."""
    cur = conn.cursor()
    
    cur.execute("""
        SELECT column_name 
        FROM information_schema.columns 
        WHERE table_name = 'atom' AND column_name = 'constituents'
    """)
    
    if cur.fetchone() is None:
        cur.execute("ALTER TABLE atom ADD COLUMN constituents TEXT[]")
        print("   Added constituents column")
    
    cur.close()

def migration_003_create_performance_views(conn):
    """Create materialized views for performance monitoring."""
    cur = conn.cursor()
    
    cur.execute("""
        CREATE MATERIALIZED VIEW IF NOT EXISTS atom_stats AS
        SELECT 
            ST_Z(geom)::INTEGER as z_level,
            modality,
            COUNT(*) as atom_count,
            AVG(ST_M(geom)) as avg_salience,
            ST_Centroid(ST_Collect(geom)) as centroid
        FROM atom
        GROUP BY ST_Z(geom)::INTEGER, modality
    """)
    
    cur.execute("""
        CREATE INDEX IF NOT EXISTS idx_atom_stats_z_mod 
        ON atom_stats(z_level, modality)
    """)
    
    print("   Created atom_stats materialized view")
    cur.close()

def migration_004_add_audit_triggers(conn):
    """Add audit logging for atom modifications."""
    cur = conn.cursor()
    
    # Create audit table
    cur.execute("""
        CREATE TABLE IF NOT EXISTS atom_audit (
            audit_id SERIAL PRIMARY KEY,
            atom_hash TEXT NOT NULL,
            operation TEXT NOT NULL,
            changed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            changed_by TEXT DEFAULT CURRENT_USER,
            old_geom GEOMETRY(PointZM, 4326),
            new_geom GEOMETRY(PointZM, 4326)
        )
    """)
    
    # Create trigger function
    cur.execute("""
        CREATE OR REPLACE FUNCTION audit_atom_changes()
        RETURNS TRIGGER AS $$
        BEGIN
            IF TG_OP = 'UPDATE' THEN
                INSERT INTO atom_audit (atom_hash, operation, old_geom, new_geom)
                VALUES (NEW.atom_hash, 'UPDATE', OLD.geom, NEW.geom);
            ELSIF TG_OP = 'DELETE' THEN
                INSERT INTO atom_audit (atom_hash, operation, old_geom)
                VALUES (OLD.atom_hash, 'DELETE', OLD.geom);
            END IF;
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;
    """)
    
    # Attach trigger
    cur.execute("""
        DROP TRIGGER IF EXISTS trigger_audit_atoms ON atom;
        CREATE TRIGGER trigger_audit_atoms
        AFTER UPDATE OR DELETE ON atom
        FOR EACH ROW EXECUTE FUNCTION audit_atom_changes();
    """)
    
    print("   Created audit trail triggers")
    cur.close()

def main():
    """Run all pending migrations."""
    import os
    
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    migrator = SchemaMigrator(conn)
    
    print("=" * 60)
    print("HARTONOMOUS SCHEMA MIGRATION")
    print("=" * 60)
    
    current = migrator.get_current_version()
    print(f"\nCurrent schema version: {current}")
    print(f"\nApplying migrations...\n")
    
    # Apply each migration in sequence
    migrator.apply_migration(1, "Add metadata JSONB column", 
                            migration_001_add_metadata_jsonb)
    
    migrator.apply_migration(2, "Add constituents array", 
                            migration_002_add_constituents_array)
    
    migrator.apply_migration(3, "Create performance views", 
                            migration_003_create_performance_views)
    
    migrator.apply_migration(4, "Add audit triggers", 
                            migration_004_add_audit_triggers)
    
    new_version = migrator.get_current_version()
    print(f"\n✅ All migrations applied (v{current} → v{new_version})")
    
    conn.close()

if __name__ == "__main__":
    main()
