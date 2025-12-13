"""
Performance benchmarking suite for Hartonomous spatial operations
Tests k-NN, radius search, trajectory matching, and composition reconstruction
"""

import time
import psycopg2
import numpy as np
from typing import List, Dict
import json

class HartonomousBenchmark:
    def __init__(self, db_config: dict):
        self.conn = psycopg2.connect(**db_config)
        self.results = []
        
    def benchmark_knn(self, k_values: List[int] = [10, 50, 100, 500, 1000]):
        """Benchmark k-NN queries at various k"""
        cursor = self.conn.cursor()
        
        # Get random target atom
        cursor.execute("SELECT atom_id FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1")
        target = cursor.fetchone()[0]
        
        for k in k_values:
            start = time.time()
            
            cursor.execute("""
                SELECT atom_id, ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
                FROM atom
                WHERE atom_id != %s
                ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
                LIMIT %s
            """, (target, target, target, k))
            
            results = cursor.fetchall()
            elapsed = (time.time() - start) * 1000  # ms
            
            self.results.append({
                'operation': 'knn',
                'k': k,
                'time_ms': elapsed,
                'results_count': len(results),
                'qps': 1000 / elapsed if elapsed > 0 else 0
            })
            
            print(f"k-NN (k={k}): {elapsed:.2f}ms, {len(results)} results, {1000/elapsed:.2f} QPS")
    
    def benchmark_radius_search(self, radius_values: List[float] = [0.1, 0.5, 1.0, 2.0, 5.0]):
        """Benchmark radius searches"""
        cursor = self.conn.cursor()
        
        cursor.execute("SELECT atom_id FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1")
        target = cursor.fetchone()[0]
        
        for radius in radius_values:
            start = time.time()
            
            cursor.execute("""
                SELECT atom_id, ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
                FROM atom
                WHERE atom_id != %s
                  AND ST_DWithin(geom, (SELECT geom FROM atom WHERE atom_id = %s), %s)
            """, (target, target, target, radius))
            
            results = cursor.fetchall()
            elapsed = (time.time() - start) * 1000
            
            self.results.append({
                'operation': 'radius_search',
                'radius': radius,
                'time_ms': elapsed,
                'results_count': len(results),
                'qps': 1000 / elapsed if elapsed > 0 else 0
            })
            
            print(f"Radius search (r={radius}): {elapsed:.2f}ms, {len(results)} results")
    
    def benchmark_trajectory_similarity(self, n_tests: int = 10):
        """Benchmark Fréchet distance trajectory matching"""
        cursor = self.conn.cursor()
        
        # Get composition atoms
        cursor.execute("""
            SELECT atom_id FROM atom 
            WHERE atom_class = 1 
            ORDER BY random() 
            LIMIT %s
        """, (n_tests,))
        
        compositions = [row[0] for row in cursor.fetchall()]
        
        for comp_id in compositions:
            start = time.time()
            
            cursor.execute("""
                SELECT atom_id, ST_FrechetDistance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
                FROM atom
                WHERE atom_class = 1 AND atom_id != %s
                ORDER BY dist
                LIMIT 10
            """, (comp_id, comp_id))
            
            results = cursor.fetchall()
            elapsed = (time.time() - start) * 1000
            
            self.results.append({
                'operation': 'trajectory_similarity',
                'time_ms': elapsed,
                'results_count': len(results),
                'qps': 1000 / elapsed if elapsed > 0 else 0
            })
    
    def benchmark_composition_reconstruction(self, n_tests: int = 10):
        """Benchmark streaming reconstruction"""
        cursor = self.conn.cursor()
        
        cursor.execute("""
            SELECT atom_id FROM atom 
            WHERE atom_class = 1 
            ORDER BY random() 
            LIMIT %s
        """, (n_tests,))
        
        compositions = [row[0] for row in cursor.fetchall()]
        
        if not compositions:
            print("No compositions found, skipping reconstruction benchmark")
            return
        
        for comp_id in compositions:
            start = time.time()
            
            # Just query component count via atom_compositions
            cursor.execute("""
                SELECT COUNT(*) 
                FROM atom_compositions 
                WHERE parent_atom_id = %s
            """, (comp_id,))
            
            count = cursor.fetchone()[0]
            elapsed = (time.time() - start) * 1000
            
            self.results.append({
                'operation': 'composition_reconstruction',
                'time_ms': elapsed,
                'atoms_reconstructed': count
            })
            
            print(f"Composition {comp_id.hex()[:8]}: {count} atoms in {elapsed:.2f}ms")
    
    def benchmark_cortex_cycle(self, n_cycles: int = 5):
        """Benchmark Cortex recalibration"""
        cursor = self.conn.cursor()
        
        for _ in range(n_cycles):
            start = time.time()
            
            cursor.execute("SELECT cortex_cycle_once()")
            
            elapsed = (time.time() - start) * 1000
            
            # Get metrics
            cursor.execute("SELECT * FROM v_cortex_metrics")
            metrics = cursor.fetchone()
            
            self.results.append({
                'operation': 'cortex_cycle',
                'time_ms': elapsed,
                'atoms_processed': metrics[1],
                'landmarks': metrics[4]
            })
    
    def benchmark_bulk_insert(self, n_atoms: int = 10000):
        """Benchmark bulk atom insertion via COPY"""
        import struct
        from blake3 import blake3
        
        cursor = self.conn.cursor()
        
        # Generate test atoms
        atoms = []
        for i in range(n_atoms):
            atom_id = blake3(struct.pack('I', i)).digest()
            atoms.append((
                atom_id,
                0,  # atom_class
                4,  # modality
                'test',
                struct.pack('f', float(i)),
                f'SRID=4326;POINT ZM(0 0 0 1)',
                None,
                '{}'
            ))
        
        start = time.time()
        
        # Use COPY for bulk insert
        with cursor.copy("COPY atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata) FROM STDIN") as copy:
            for atom in atoms:
                copy.write_row(atom)
        
        self.conn.commit()
        elapsed = (time.time() - start) * 1000
        
        self.results.append({
            'operation': 'bulk_insert',
            'n_atoms': n_atoms,
            'time_ms': elapsed,
            'atoms_per_second': n_atoms / (elapsed / 1000)
        })
        
        print(f"Bulk insert: {n_atoms} atoms in {elapsed:.2f}ms ({n_atoms/(elapsed/1000):.0f} atoms/sec)")
    
    def run_all_benchmarks(self):
        """Run complete benchmark suite"""
        print("=== Hartonomous Performance Benchmark ===\n")
        
        print("1. k-NN Query Performance:")
        self.benchmark_knn()
        
        print("\n2. Radius Search Performance:")
        self.benchmark_radius_search()
        
        print("\n3. Trajectory Similarity:")
        self.benchmark_trajectory_similarity()
        
        print("\n4. Composition Reconstruction:")
        self.benchmark_composition_reconstruction()
        
        print("\n5. Cortex Recalibration:")
        self.benchmark_cortex_cycle()
        
        # Save results
        with open('benchmark_results.json', 'w') as f:
            json.dump(self.results, f, indent=2)
        
        print("\nBenchmark complete. Results saved to benchmark_results.json")
        
        # Summary statistics
        print("\n=== Summary ===")
        for op in ['knn', 'radius_search', 'trajectory_similarity']:
            op_results = [r for r in self.results if r['operation'] == op]
            if op_results:
                avg_time = np.mean([r['time_ms'] for r in op_results])
                print(f"{op}: avg {avg_time:.2f}ms")
    
    def close(self):
        self.conn.close()

if __name__ == '__main__':
    bench = HartonomousBenchmark({
        'host': 'localhost',
        'dbname': 'hartonomous',
        'user': 'hartonomous'
    })
    
    bench.run_all_benchmarks()
    bench.close()
