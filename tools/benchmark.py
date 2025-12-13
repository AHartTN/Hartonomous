#!/usr/bin/env python3
"""Performance benchmarking tool"""

import time
import statistics
from connector import Hartonomous, PerformanceMonitor
import random


def benchmark_knn_queries(hart: Hartonomous, iterations: int = 100):
    """Benchmark k-NN query performance"""
    print(f"\n=== k-NN Query Benchmark ({iterations} iterations) ===")
    
    # Get some atoms to query
    base_atoms = hart.search(0, 0, 0, 0, k=10)
    if not base_atoms:
        print("No atoms in database")
        return
    
    times = []
    
    for i in range(iterations):
        target = random.choice(base_atoms)
        
        start = time.perf_counter()
        results = hart.query(target.atom_hash, k=10)
        elapsed = (time.perf_counter() - start) * 1000  # ms
        
        times.append(elapsed)
        
        if (i + 1) % 10 == 0:
            print(f"  Progress: {i+1}/{iterations}")
    
    print(f"\nResults:")
    print(f"  Mean:   {statistics.mean(times):.2f} ms")
    print(f"  Median: {statistics.median(times):.2f} ms")
    print(f"  Min:    {min(times):.2f} ms")
    print(f"  Max:    {max(times):.2f} ms")
    print(f"  StdDev: {statistics.stdev(times):.2f} ms")


def benchmark_radius_search(hart: Hartonomous, iterations: int = 50):
    """Benchmark radius search performance"""
    print(f"\n=== Radius Search Benchmark ({iterations} iterations) ===")
    
    base_atoms = hart.search(0, 0, 0, 0, k=10)
    if not base_atoms:
        return
    
    times = []
    
    for i in range(iterations):
        target = random.choice(base_atoms)
        radius = random.uniform(1.0, 10.0)
        
        start = time.perf_counter()
        results = hart.neighborhood(target.atom_hash, radius)
        elapsed = (time.perf_counter() - start) * 1000
        
        times.append(elapsed)
    
    print(f"\nResults:")
    print(f"  Mean:   {statistics.mean(times):.2f} ms")
    print(f"  Median: {statistics.median(times):.2f} ms")


def show_query_stats(hart: Hartonomous):
    """Show database query statistics"""
    print("\n=== Database Query Statistics ===")
    
    monitor = PerformanceMonitor(hart.pool)
    stats = monitor.get_query_stats(top_n=5)
    
    for i, stat in enumerate(stats, 1):
        print(f"\n{i}. {stat['query'][:80]}...")
        print(f"   Calls: {stat['calls']}")
        print(f"   Mean:  {stat['mean_time_ms']:.2f} ms")
        print(f"   Max:   {stat['max_time_ms']:.2f} ms")


def show_index_usage(hart: Hartonomous):
    """Show index usage statistics"""
    print("\n=== Index Usage Statistics ===")
    
    monitor = PerformanceMonitor(hart.pool)
    indexes = monitor.get_index_usage()
    
    for idx in indexes:
        print(f"\n{idx['index']}:")
        print(f"  Scans:  {idx['scans']}")
        print(f"  Size:   {idx['size']}")
        print(f"  Tuples: {idx['tuples_fetched']}")


def main():
    hart = Hartonomous()
    
    print("=== Hartonomous Performance Benchmark ===")
    
    # Run benchmarks
    benchmark_knn_queries(hart, iterations=100)
    benchmark_radius_search(hart, iterations=50)
    
    # Show statistics
    show_query_stats(hart)
    show_index_usage(hart)
    
    hart.close()
    print("\nBenchmark complete.")


if __name__ == '__main__':
    main()
