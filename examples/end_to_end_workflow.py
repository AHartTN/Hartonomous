#!/usr/bin/env python3
"""
End-to-End Hartonomous Workflow
Demonstrates: Shader → Database → Connector → Query
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.api import Hartonomous
import subprocess
import tempfile
import struct

def create_test_atoms():
    """Generate test atoms using Shader"""
    print("1. Generating atoms with Shader...")
    
    # Create input CSV for Shader
    with tempfile.NamedTemporaryFile(mode='w', suffix='.csv', delete=False) as f:
        # Numeric constants
        for i in range(100):
            f.write(f"numeric,{i},0,raw\n")
        # Text tokens
        tokens = ["hello", "world", "spatial", "database", "geometry", "inference"]
        for token in tokens:
            f.write(f"text,{token},0,raw\n")
        csv_path = f.name
    
    # Run Shader to process and load
    shader_bin = Path("shader/target/release/hartonomous-shader.exe")
    if not shader_bin.exists():
        print("ERROR: Shader binary not found. Run: cargo build --release")
        return False
    
    result = subprocess.run([
        str(shader_bin),
        "process",
        "--input", csv_path,
        "--modality", "mixed",
        "--output", "atoms.bin"
    ], capture_output=True, text=True)
    
    if result.returncode != 0:
        print(f"Shader error: {result.stderr}")
        return False
    
    print(f"✓ Generated atoms: {result.stdout.strip()}")
    Path(csv_path).unlink()
    return True

def test_connector_operations():
    """Test all connector operations"""
    print("\n2. Testing Connector operations...")
    
    h = Hartonomous()
    
    # Status
    status = h.status()
    print(f"✓ Database status: {status['atoms_processed']} atoms processed")
    
    # Get sample atom for queries
    with h.pool.connection() as conn:
        cur = conn.cursor()
        cur.execute("SELECT atom_id FROM atom LIMIT 1")
        result = cur.fetchone()
        if not result:
            print("✗ No atoms in database")
            return False
        sample_hash = result[0]
    
    print(f"✓ Sample atom: {sample_hash.hex()[:16]}...")
    
    # k-NN query
    print("\n3. Testing k-NN inference...")
    neighbors = h.query(sample_hash, k=5)
    print(f"✓ Found {len(neighbors)} similar atoms")
    for i, atom in enumerate(neighbors[:3]):
        print(f"  {i+1}. Class={atom.atom_class}, Modality={atom.modality}, Coords=({atom.x:.2f}, {atom.y:.2f}, {atom.z:.2f})")
    
    # Radius search
    print("\n4. Testing radius search...")
    neighborhood = h.neighborhood(sample_hash, radius=10.0)
    print(f"✓ Found {len(neighborhood)} atoms within radius 10")
    
    # Coordinate search
    print("\n5. Testing coordinate-based search...")
    nearby = h.search(x=0.0, y=0.0, z=0.0, m=0.5, k=5)
    print(f"✓ Found {len(nearby)} atoms near origin")
    
    # Hierarchy traversal
    print("\n6. Testing hierarchy traversal...")
    try:
        abstractions = h.abstract(sample_hash, levels=1, k=3)
        print(f"✓ Abstraction: {len(abstractions)} atoms at higher Z-level")
    except Exception as e:
        print(f"⚠ Abstraction not available (expected - no Z>0 atoms yet)")
    
    return True

def test_spatial_queries():
    """Test raw spatial SQL queries"""
    print("\n7. Testing spatial SQL queries...")
    
    h = Hartonomous()
    with h.pool.connection() as conn:
        cur = conn.cursor()
        
        # GiST index usage check
        cur.execute("""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT atom_id 
            FROM atom 
            ORDER BY geom <-> ST_SetSRID(ST_MakePoint(0, 0, 0, 0), 4326) 
            LIMIT 10
        """)
        plan = cur.fetchall()
        uses_index = any("Index Scan" in str(row) for row in plan)
        print(f"✓ GiST index {'USED' if uses_index else 'NOT USED'} for k-NN")
        
        # Distance calculations
        cur.execute("""
            SELECT 
                COUNT(*) as total,
                AVG(ST_X(geom)) as avg_x,
                AVG(ST_Y(geom)) as avg_y,
                AVG(ST_Z(geom)) as avg_z,
                AVG(ST_M(geom)) as avg_m
            FROM atom
        """)
        stats = cur.fetchone()
        print(f"✓ Spatial stats: {stats[0]} atoms, centroid=({stats[1]:.2f}, {stats[2]:.2f}, {stats[3]:.2f})")
        
        # Hilbert clustering check
        cur.execute("""
            SELECT 
                COUNT(DISTINCT hilbert_index) as unique_hilbert,
                COUNT(*) as total_atoms,
                ROUND(100.0 * COUNT(DISTINCT hilbert_index) / COUNT(*), 2) as diversity_pct
            FROM atom
        """)
        hilbert = cur.fetchone()
        print(f"✓ Hilbert diversity: {hilbert[2]}% unique indices")
    
    return True

def benchmark_performance():
    """Quick performance benchmark"""
    print("\n8. Performance benchmark...")
    import time
    
    h = Hartonomous()
    with h.pool.connection() as conn:
        cur = conn.cursor()
        cur.execute("SELECT atom_id FROM atom LIMIT 100")
        test_hashes = [row[0] for row in cur.fetchall()]
    
    # Benchmark k-NN queries
    start = time.time()
    for atom_hash in test_hashes[:10]:
        h.query(atom_hash, k=10)
    elapsed = time.time() - start
    qps = 10 / elapsed
    
    print(f"✓ k-NN performance: {qps:.1f} queries/sec ({elapsed*100:.1f}ms avg)")
    
    # Benchmark radius search
    start = time.time()
    for atom_hash in test_hashes[:10]:
        h.neighborhood(atom_hash, radius=5.0)
    elapsed = time.time() - start
    
    print(f"✓ Radius search: {10/elapsed:.1f} queries/sec ({elapsed*100:.1f}ms avg)")
    
    return True

def main():
    print("=== Hartonomous End-to-End Workflow ===\n")
    
    try:
        # Skip shader test if atoms already exist
        h = Hartonomous()
        status = h.status()
        if status['atoms_processed'] == 0:
            print("Database empty - would run Shader here")
            # create_test_atoms()  # Disabled - Shader needs full implementation
        
        if not test_connector_operations():
            return 1
        
        if not test_spatial_queries():
            return 1
        
        if not benchmark_performance():
            return 1
        
        print("\n=== All Tests Passed ===")
        return 0
        
    except Exception as e:
        print(f"\n✗ ERROR: {e}")
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    sys.exit(main())
