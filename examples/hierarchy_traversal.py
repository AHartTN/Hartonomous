#!/usr/bin/env python3
"""Example: Hierarchy traversal"""

from connector import Hartonomous
import sys

def main():
    hart = Hartonomous()
    
    print("=== Hierarchy Traversal Demo ===\n")
    
    # Find a base atom
    base_atoms = hart.search(x=0.0, y=0.0, z=0.0, m=1.0, k=1)
    
    if not base_atoms:
        print("No atoms found. Populate database first.")
        sys.exit(1)
    
    base = base_atoms[0]
    print(f"Base atom: {base.atom_hash[:4].hex()}...")
    print(f"Position: ({base.x:.2f}, {base.y:.2f}, {base.z:.2f})")
    print()
    
    # Abstraction (move UP hierarchy)
    print("Moving UP hierarchy (abstraction):")
    abstract = hart.abstract(base.atom_hash, levels=1, k=3)
    
    if abstract:
        for i, atom in enumerate(abstract, 1):
            print(f"  {i}. Z={atom.z:.0f} at ({atom.x:.2f}, {atom.y:.2f})")
    else:
        print("  No higher-level abstractions found")
    
    print()
    
    # Refinement (move DOWN hierarchy)
    print("Moving DOWN hierarchy (refinement):")
    refined = hart.refine(base.atom_hash, levels=1, k=3)
    
    if refined:
        for i, atom in enumerate(refined, 1):
            print(f"  {i}. Z={atom.z:.0f} at ({atom.x:.2f}, {atom.y:.2f})")
    else:
        print("  No lower-level details found")
    
    hart.close()

if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
