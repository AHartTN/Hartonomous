"""
Production deployment automation script
Handles database initialization, extension installation, and health verification
"""

import subprocess
import sys
import time
from pathlib import Path
import psycopg2

class ProductionDeployer:
    def __init__(self, config: dict):
        self.config = config
        self.db_host = config.get('db_host', 'localhost')
        self.db_port = config.get('db_port', 5432)
        self.db_name = config.get('db_name', 'hartonomous')
        self.db_user = config.get('db_user', 'hartonomous')
        self.postgres_bin = config.get('postgres_bin', 'D:/PostgreSQL/18/bin')
        
    def deploy(self):
        """Execute full deployment sequence"""
        print("=== Hartonomous Production Deployment ===\n")
        
        steps = [
            ("1. Create database", self.create_database),
            ("2. Install PostGIS", self.install_postgis),
            ("3. Create schema", self.create_schema),
            ("4. Install Cortex extension", self.install_cortex),
            ("5. Install monitoring views", self.install_monitoring),
            ("6. Install spatial inference", self.install_spatial_inference),
            ("7. Install streaming reconstruction", self.install_streaming_reconstruction),
            ("8. Install two-stage filtering", self.install_two_stage_filtering),
            ("9. Install sharding functions", self.install_sharding),
            ("10. Install disaster recovery", self.install_disaster_recovery),
            ("11. Install connection pooling", self.install_connection_pooling),
            ("12. Verify deployment", self.verify_deployment),
        ]
        
        for step_name, step_func in steps:
            print(f"\n{step_name}...")
            try:
                step_func()
                print(f"✓ {step_name} complete")
            except Exception as e:
                print(f"✗ {step_name} failed: {e}")
                if not self.config.get('continue_on_error', False):
                    sys.exit(1)
        
        print("\n=== Deployment Complete ===")
        self.print_connection_info()
    
    def create_database(self):
        """Create hartonomous database if not exists"""
        try:
            conn = psycopg2.connect(
                host=self.db_host,
                port=self.db_port,
                user=self.db_user,
                database='postgres'
            )
            conn.autocommit = True
            cursor = conn.cursor()
            
            cursor.execute(f"SELECT 1 FROM pg_database WHERE datname = '{self.db_name}'")
            if not cursor.fetchone():
                cursor.execute(f"CREATE DATABASE {self.db_name}")
                print(f"  Created database: {self.db_name}")
            else:
                print(f"  Database already exists: {self.db_name}")
            
            conn.close()
        except psycopg2.Error as e:
            print(f"  Warning: {e}")
    
    def install_postgis(self):
        """Install PostGIS extension"""
        self._run_sql_command("CREATE EXTENSION IF NOT EXISTS postgis")
    
    def create_schema(self):
        """Install core schema"""
        self._run_sql_file("database/schema.sql")
    
    def install_cortex(self):
        """Build and install Cortex C++ extension"""
        cortex_dir = Path("cortex")
        
        # Build Cortex DLL
        subprocess.run(
            ["cmake", "--build", "build", "--config", "Release"],
            cwd=cortex_dir,
            check=True
        )
        
        # Copy DLL to PostgreSQL lib
        dll_path = cortex_dir / "build" / "Release" / "cortex.dll"
        target_path = Path(self.postgres_bin).parent / "lib" / "cortex.dll"
        
        import shutil
        shutil.copy(dll_path, target_path)
        print(f"  Copied cortex.dll to {target_path}")
        
        # Create extension in database
        self._run_sql_command("CREATE EXTENSION IF NOT EXISTS cortex")
    
    def install_monitoring(self):
        """Install monitoring views"""
        self._run_sql_file("database/monitoring.sql")
    
    def install_spatial_inference(self):
        """Install spatial inference functions"""
        self._run_sql_file("database/functions/spatial_inference.sql")
    
    def install_streaming_reconstruction(self):
        """Install streaming reconstruction"""
        self._run_sql_file("database/functions/streaming_reconstruction.sql")
    
    def install_two_stage_filtering(self):
        """Install two-stage filtering"""
        self._run_sql_file("database/functions/two_stage_filtering.sql")
    
    def install_sharding(self):
        """Install sharding functions"""
        self._run_sql_file("database/sharding.sql")
    
    def install_disaster_recovery(self):
        """Install disaster recovery procedures"""
        self._run_sql_file("database/disaster_recovery.sql")
    
    def install_connection_pooling(self):
        """Install connection pooling config"""
        self._run_sql_file("database/connection_pooling.sql")
    
    def verify_deployment(self):
        """Run health checks"""
        conn = self._get_connection()
        cursor = conn.cursor()
        
        # Check table counts
        cursor.execute("SELECT COUNT(*) FROM atom")
        atom_count = cursor.fetchone()[0]
        print(f"  Atoms: {atom_count}")
        
        # Check Cortex
        cursor.execute("SELECT COUNT(*) FROM cortex_landmarks")
        landmark_count = cursor.fetchone()[0]
        print(f"  Cortex landmarks: {landmark_count}")
        
        # Check functions
        cursor.execute("""
            SELECT COUNT(*) FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public'
              AND p.proname IN ('task_decompose', 'cortex_cycle_once', 'two_stage_trajectory_search')
        """)
        func_count = cursor.fetchone()[0]
        print(f"  Key functions installed: {func_count}/3")
        
        # Test k-NN query
        if atom_count > 0:
            start = time.time()
            cursor.execute("""
                SELECT atom_id FROM atom
                ORDER BY geom <-> (SELECT geom FROM atom LIMIT 1)
                LIMIT 10
            """)
            elapsed = (time.time() - start) * 1000
            print(f"  k-NN query test: {elapsed:.2f}ms")
        
        conn.close()
    
    def _run_sql_file(self, file_path: str):
        """Execute SQL file via psql"""
        subprocess.run([
            str(Path(self.postgres_bin) / "psql"),
            "-h", self.db_host,
            "-U", self.db_user,
            "-d", self.db_name,
            "-f", file_path
        ], check=True, capture_output=True)
    
    def _run_sql_command(self, sql: str):
        """Execute SQL command"""
        conn = self._get_connection()
        cursor = conn.cursor()
        cursor.execute(sql)
        conn.commit()
        conn.close()
    
    def _get_connection(self):
        """Get database connection"""
        return psycopg2.connect(
            host=self.db_host,
            port=self.db_port,
            dbname=self.db_name,
            user=self.db_user
        )
    
    def print_connection_info(self):
        """Print connection details"""
        print(f"\nConnection string:")
        print(f"  host={self.db_host} port={self.db_port} dbname={self.db_name} user={self.db_user}")
        print(f"\nTo connect:")
        print(f"  psql -h {self.db_host} -U {self.db_user} -d {self.db_name}")

if __name__ == '__main__':
    deployer = ProductionDeployer({
        'db_host': 'localhost',
        'db_name': 'hartonomous',
        'db_user': 'hartonomous',
        'postgres_bin': 'D:/PostgreSQL/18/bin',
        'continue_on_error': True
    })
    
    deployer.deploy()
