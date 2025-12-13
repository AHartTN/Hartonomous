#!/usr/bin/env python3
"""Example: Basic inference operations"""

from connector import Hartonomous
import sys

def main():
    # Connect
    hart = Hartonomous()
    
    print("=== Hartonomous Inference Demo ===\n")
    
    # Check system status
    status = hart.status()
    print(f"System Status:")
    print(f"  Cortex Running: {status.get('is_running', False)}")
    print(f"  Model Version: {status.get('model_version', 0)}")
    print(f"  Atoms Processed: {status.get('atoms_processed', 0)}")
    print(f"  Current Stress: {status.get('current_stress', 0.0):.4f}")
    print()
    
    # Search near origin
    print("Searching near semantic origin (0, 0, 0)...")
    results = hart.search(x=0.0, y=0.0, z=0.0, m=0.0, k=5)
    
    if results:
        print(f"Found {len(results)} atoms:")
        for i, atom in enumerate(results, 1):
            print(f"  {i}. Hash: {atom.atom_hash[:4].hex()}... at ({atom.x:.2f}, {atom.y:.2f}, {atom.z:.2f})")
    else:
        print("  No atoms found. Database may be empty.")
        print("  Run: psql -d hartonomous -f database/test_data.sql")
    
    print()
    
    # If we have atoms, demonstrate k-NN
    if results:
        target = results[0]
        print(f"Finding neighbors of {target.atom_hash[:4].hex()}...")
        neighbors = hart.query(target.atom_hash, k=5)
        
        print(f"Found {len(neighbors)} neighbors:")
        for i, atom in enumerate(neighbors, 1):
            dist = ((atom.x - target.x)**2 + (atom.y - target.y)**2)**0.5
            print(f"  {i}. Hash: {atom.atom_hash[:4].hex()}... distance: {dist:.2f}")
    
    hart.close()
    print("\nDemo complete.")

if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
