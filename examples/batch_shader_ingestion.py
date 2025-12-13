"""
Batch data ingestion using Hartonomous Shader pipeline.
Demonstrates high-throughput atom loading with Rust preprocessor.
"""

import os
import subprocess
import time
import tempfile
from pathlib import Path

def generate_sample_data(output_path: str, num_atoms: int = 10000):
    """Generate sample text data for shader ingestion."""
    print(f"📝 Generating {num_atoms} sample atoms...")
    
    with open(output_path, 'w', encoding='utf-8') as f:
        # Mix of numeric and text atoms
        for i in range(num_atoms):
            if i % 3 == 0:
                # Numeric atom
                f.write(f"{i * 0.123}\n")
            else:
                # Text atom (common words)
                words = ["the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", 
                        "data", "atom", "semantic", "spatial", "query", "inference"]
                f.write(f"{words[i % len(words)]}\n")
    
    print(f"✅ Sample data written to {output_path}")

def run_shader_pipeline(input_path: str, shader_exe: str = None):
    """Execute Shader binary to process and load atoms."""
    
    if shader_exe is None:
        # Default to release binary
        shader_exe = Path("shader/target/release/hartonomous-shader.exe")
    
    if not shader_exe.exists():
        raise FileNotFoundError(f"Shader binary not found: {shader_exe}")
    
    print(f"🚀 Running Shader pipeline: {shader_exe}")
    print(f"   Input: {input_path}")
    
    # Environment variables for PostgreSQL connection
    env = os.environ.copy()
    env.update({
        "PGHOST": "localhost",
        "PGPORT": "5432",
        "PGDATABASE": "hartonomous",
        "PGUSER": "hartonomous"
    })
    
    start_time = time.time()
    
    try:
        # Run shader with input file
        result = subprocess.run(
            [str(shader_exe), input_path],
            env=env,
            capture_output=True,
            text=True,
            timeout=300  # 5 minute timeout
        )
        
        elapsed = time.time() - start_time
        
        if result.returncode == 0:
            print(f"✅ Shader completed in {elapsed:.2f}s")
            print(f"   Output: {result.stdout}")
            
            # Parse throughput from output
            lines_processed = result.stdout.count('\n')
            throughput = lines_processed / elapsed if elapsed > 0 else 0
            print(f"   Throughput: {throughput:.0f} atoms/sec")
        else:
            print(f"❌ Shader failed (exit code {result.returncode})")
            print(f"   Error: {result.stderr}")
            return False
        
    except subprocess.TimeoutExpired:
        print("❌ Shader timed out after 5 minutes")
        return False
    except Exception as e:
        print(f"❌ Shader execution failed: {e}")
        return False
    
    return True

def verify_ingestion(expected_count: int = None):
    """Verify atoms were loaded into database."""
    import psycopg2
    from psycopg2.extras import RealDictCursor
    
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    cur = conn.cursor(cursor_factory=RealDictCursor)
    
    # Get atom counts
    cur.execute("SELECT COUNT(*) as total, COUNT(DISTINCT modality) as mods FROM atom")
    stats = cur.fetchone()
    
    print(f"📊 Database verification:")
    print(f"   Total atoms: {stats['total']}")
    print(f"   Modalities: {stats['mods']}")
    
    if expected_count and stats['total'] < expected_count:
        print(f"⚠️  Expected {expected_count} atoms, found {stats['total']}")
    
    # Sample some atoms
    cur.execute("""
        SELECT atom_hash, ST_X(geom) as x, ST_Y(geom) as y, ST_Z(geom) as z, ST_M(geom) as m
        FROM atom
        ORDER BY RANDOM()
        LIMIT 5
    """)
    
    print(f"\n📍 Sample atoms:")
    for row in cur.fetchall():
        print(f"   {row['atom_hash'][:16]}... → ({row['x']:.2f}, {row['y']:.2f}, {row['z']:.2f}, {row['m']:.2f})")
    
    cur.close()
    conn.close()

def main():
    """Execute full batch ingestion workflow."""
    print("=" * 80)
    print("HARTONOMOUS BATCH SHADER INGESTION")
    print("=" * 80)
    
    # Create temporary input file
    with tempfile.NamedTemporaryFile(mode='w', suffix='.txt', delete=False) as tmp:
        input_file = tmp.name
    
    try:
        # Step 1: Generate sample data
        num_atoms = 10000
        generate_sample_data(input_file, num_atoms)
        
        # Step 2: Run shader pipeline
        success = run_shader_pipeline(input_file)
        
        if not success:
            print("\n❌ Ingestion failed")
            return 1
        
        # Step 3: Verify results
        print("\n" + "=" * 80)
        verify_ingestion(expected_count=num_atoms)
        
        print("\n✅ Batch ingestion complete")
        return 0
        
    finally:
        # Cleanup temp file
        if os.path.exists(input_file):
            os.unlink(input_file)

if __name__ == "__main__":
    exit(main())
