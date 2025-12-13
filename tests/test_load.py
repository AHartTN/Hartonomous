"""
Production load testing for Hartonomous spatial queries.
Simulates concurrent workload and measures throughput.
"""

import time
import statistics
import psycopg2
from psycopg2.extras import RealDictCursor
from psycopg2 import pool
import os
from concurrent.futures import ThreadPoolExecutor, as_completed
from typing import List, Tuple

class LoadTester:
    """Simulates production query load."""
    
    def __init__(self, conn_params: dict, pool_size: int = 10):
        """
        Initialize load tester.
        
        Args:
            conn_params: Database connection parameters
            pool_size: Connection pool size
        """
        self.pool = pool.ThreadedConnectionPool(
            minconn=1,
            maxconn=pool_size,
            **conn_params
        )
        self.pool_size = pool_size
    
    def get_sample_atoms(self, count: int = 100) -> List[bytes]:
        """Get sample atom IDs for testing."""
        conn = self.pool.getconn()
        cur = conn.cursor()
        
        cur.execute(f"""
            SELECT atom_id
            FROM atom
            ORDER BY RANDOM()
            LIMIT {count}
        """)
        
        atom_ids = [row[0] for row in cur.fetchall()]
        
        cur.close()
        self.pool.putconn(conn)
        
        return atom_ids
    
    def single_knn_query(self, atom_id: bytes, k: int = 10) -> float:
        """Execute single k-NN query and return duration."""
        conn = self.pool.getconn()
        cur = conn.cursor()
        
        start = time.perf_counter()
        
        cur.execute("""
            SELECT atom_id, geom <-> (SELECT geom FROM atom WHERE atom_id = %s) as dist
            FROM atom
            WHERE atom_id != %s
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
            LIMIT %s
        """, (atom_id, atom_id, atom_id, k))
        
        results = cur.fetchall()
        elapsed = time.perf_counter() - start
        
        cur.close()
        self.pool.putconn(conn)
        
        return elapsed
    
    def single_radius_query(self, atom_id: bytes, radius: float = 10.0) -> float:
        """Execute single radius search and return duration."""
        conn = self.pool.getconn()
        cur = conn.cursor()
        
        start = time.perf_counter()
        
        cur.execute("""
            SELECT atom_id, ST_3DDistance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
            FROM atom
            WHERE atom_id != %s
              AND ST_DWithin(geom, (SELECT geom FROM atom WHERE atom_id = %s), %s)
        """, (atom_id, atom_id, atom_id, radius))
        
        results = cur.fetchall()
        elapsed = time.perf_counter() - start
        
        cur.close()
        self.pool.putconn(conn)
        
        return elapsed
    
    def run_concurrent_load(self, query_func, atom_ids: List[bytes], 
                           duration_sec: int = 30) -> Tuple[int, List[float]]:
        """
        Run concurrent queries for specified duration.
        
        Args:
            query_func: Function to execute (single_knn_query or single_radius_query)
            atom_ids: List of atom IDs to query
            duration_sec: Test duration in seconds
            
        Returns:
            Tuple of (total_queries, latency_list)
        """
        start_time = time.time()
        end_time = start_time + duration_sec
        
        query_times = []
        query_count = 0
        
        with ThreadPoolExecutor(max_workers=self.pool_size) as executor:
            futures = []
            
            # Keep submitting queries until time expires
            while time.time() < end_time:
                atom_id = atom_ids[query_count % len(atom_ids)]
                future = executor.submit(query_func, atom_id)
                futures.append(future)
                query_count += 1
                
                # Check completed queries periodically
                if len(futures) >= self.pool_size * 2:
                    for future in as_completed(futures[:self.pool_size]):
                        query_times.append(future.result())
                    futures = futures[self.pool_size:]
            
            # Wait for remaining queries
            for future in as_completed(futures):
                query_times.append(future.result())
        
        return query_count, query_times
    
    def close(self):
        """Close connection pool."""
        self.pool.closeall()

def print_load_test_results(test_name: str, query_count: int, 
                           query_times: List[float], duration_sec: int):
    """Print formatted load test results."""
    qps = query_count / duration_sec
    
    avg_latency = statistics.mean(query_times) * 1000  # ms
    p50_latency = statistics.median(query_times) * 1000
    p95_latency = statistics.quantiles(query_times, n=20)[18] * 1000  # 95th percentile
    p99_latency = statistics.quantiles(query_times, n=100)[98] * 1000  # 99th percentile
    max_latency = max(query_times) * 1000
    
    print(f"\n{test_name}")
    print("=" * 60)
    print(f"Duration: {duration_sec}s")
    print(f"Total Queries: {query_count}")
    print(f"Throughput: {qps:.1f} QPS")
    print(f"\nLatency (ms):")
    print(f"  Average: {avg_latency:.2f}")
    print(f"  Median (p50): {p50_latency:.2f}")
    print(f"  p95: {p95_latency:.2f}")
    print(f"  p99: {p99_latency:.2f}")
    print(f"  Max: {max_latency:.2f}")

def main():
    """Execute production load testing."""
    print("=" * 80)
    print("HARTONOMOUS LOAD TESTING")
    print("=" * 80)
    
    conn_params = {
        "host": "localhost",
        "port": 5432,
        "database": "hartonomous",
        "user": "hartonomous",
        "password": os.environ.get("PGPASSWORD", "")
    }
    
    tester = LoadTester(conn_params, pool_size=10)
    
    print("\n📋 Preparing test data...")
    sample_atoms = tester.get_sample_atoms(100)
    print(f"   Using {len(sample_atoms)} sample atoms")
    
    # Test 1: k-NN queries (30 second sustained load)
    print("\n🔄 Running k-NN load test (30s)...")
    knn_count, knn_times = tester.run_concurrent_load(
        tester.single_knn_query,
        sample_atoms,
        duration_sec=30
    )
    print_load_test_results("k-NN Query Load Test", knn_count, knn_times, 30)
    
    # Test 2: Radius search (30 second sustained load)
    print("\n🔄 Running radius search load test (30s)...")
    radius_count, radius_times = tester.run_concurrent_load(
        tester.single_radius_query,
        sample_atoms,
        duration_sec=30
    )
    print_load_test_results("Radius Search Load Test", radius_count, radius_times, 30)
    
    # Test 3: Mixed workload (60 seconds)
    print("\n🔄 Running mixed workload test (60s)...")
    
    def mixed_query(atom_id):
        import random
        if random.random() < 0.7:  # 70% k-NN, 30% radius
            return tester.single_knn_query(atom_id, k=10)
        else:
            return tester.single_radius_query(atom_id, radius=10.0)
    
    mixed_count, mixed_times = tester.run_concurrent_load(
        mixed_query,
        sample_atoms,
        duration_sec=60
    )
    print_load_test_results("Mixed Workload Test", mixed_count, mixed_times, 60)
    
    tester.close()
    
    print("\n✅ Load testing complete")
    
    # Performance benchmarks for reference
    print("\n📊 PERFORMANCE SUMMARY")
    print("=" * 60)
    print(f"k-NN Throughput: {knn_count/30:.1f} QPS")
    print(f"Radius Search Throughput: {radius_count/30:.1f} QPS")
    print(f"Mixed Workload Throughput: {mixed_count/60:.1f} QPS")

if __name__ == "__main__":
    main()
