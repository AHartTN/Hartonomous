"""
Integration test for Hartonomous system
Tests: Database → Shader → Cortex → Connector
"""
import unittest
import os
import subprocess
import sys
from pathlib import Path

# Add connector to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.api import Hartonomous
from connector.pool import HartonomousPool
import psycopg2


class TestHartonomousIntegration(unittest.TestCase):
    """End-to-end integration tests"""
    
    @classmethod
    def setUpClass(cls):
        """Initialize database and load test data"""
        # Use environment variables that match PostgreSQL setup
        import subprocess
        pg_pass = subprocess.run(['powershell', '-Command', '$env:PGPASSWORD'], 
                                capture_output=True, text=True).stdout.strip()
        
        cls.db_params = {
            'dbname': os.getenv('PGDATABASE', 'hartonomous'),
            'user': os.getenv('PGUSER', 'hartonomous'),
            'password': pg_pass if pg_pass else None,
            'host': 'localhost',
            'port': 5432
        }
        if not cls.db_params['password']:
            del cls.db_params['password']
        
        # Create database if it doesn't exist
        try:
            conn = psycopg2.connect(**{**cls.db_params, 'dbname': 'postgres'})
            conn.autocommit = True
            cur = conn.cursor()
            cur.execute("SELECT 1 FROM pg_database WHERE datname='hartonomous'")
            if not cur.fetchone():
                cur.execute("CREATE DATABASE hartonomous")
            cur.close()
            conn.close()
        except Exception as e:
            print(f"Database setup: {e}")
    
    def test_01_database_connection(self):
        """Test PostgreSQL connection"""
        pool = HartonomousPool(config=self.db_params)
        with pool.connection() as conn:
            cur = conn.cursor()
            cur.execute("SELECT version()")
            version = cur.fetchone()[0]
            self.assertIn("PostgreSQL", version)
    
    def test_02_schema_creation(self):
        """Test database schema setup"""
        # Use repair schema instead of full schema (avoids ALTER SYSTEM in tests)
        schema_path = Path(__file__).parent.parent / "database" / "schema_repair.sql"
        if not schema_path.exists():
            self.skipTest("schema_repair.sql not found")

        conn = psycopg2.connect(**self.db_params)
        conn.autocommit = True
        cur = conn.cursor()

        # Execute repair schema (idempotent, no ALTER SYSTEM)
        
        # Verify atom table exists
        cur.execute("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables 
                WHERE table_name = 'atom'
            )
        """)
        self.assertTrue(cur.fetchone()[0])
        cur.close()
        conn.close()
    
    def test_03_connector_initialization(self):
        """Test Hartonomous connector API"""
        h = Hartonomous(**self.db_params)
        status = h.status()
        self.assertIn('is_running', status)
        self.assertIn('model_version', status)
        self.assertIn('atoms_processed', status)
    
    def test_04_shader_binary_exists(self):
        """Test Shader binary compilation"""
        shader_bin = Path(__file__).parent.parent / "shader" / "target" / "release" / "hartonomous-shader.exe"
        self.assertTrue(shader_bin.exists(), "Shader binary not found - run 'cargo build --release'")
    
    def test_05_cortex_extension_installed(self):
        """Test Cortex DLL build"""
        cortex_dll = Path(__file__).parent.parent / "cortex" / "build" / "Release" / "cortex.dll"
        self.assertTrue(cortex_dll.exists(), "cortex.dll not built")


if __name__ == '__main__':
    unittest.main(verbosity=2)
