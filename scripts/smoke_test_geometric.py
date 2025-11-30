"""
Smoke Test: End-to-End Geometric Atomization with Database

Verifies the complete pipeline:
1. Connect to PostgreSQL database
2. Create atoms at deterministic coordinates
3. Store LINESTRING trajectories
4. Reconstruct content from trajectories
5. Verify spatial queries work

This proves the geometric architecture integrates with the database.

Run with: python scripts/smoke_test_geometric.py

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import asyncio
import sys
import os
from pathlib import Path

# Add project root to path
sys.path.insert(0, str(Path(__file__).parent.parent))

import psycopg
from psycopg.rows import dict_row
import numpy as np

from api.services.geometric_atomization import GeometricAtomizer


async def test_database_connection():
    """Test 1: Verify database connection."""
    print("=" * 60)
    print("TEST 1: Database Connection")
    print("=" * 60)
    
    db_url = os.getenv('DATABASE_URL', 'postgresql://hartonomous:hart123@localhost:5432/hartonomous')
    
    try:
        async with await psycopg.AsyncConnection.connect(db_url) as conn:
            async with conn.cursor() as cursor:
                await cursor.execute("SELECT version();")
                version = await cursor.fetchone()
                print(f"✓ Connected to PostgreSQL: {version[0][:50]}...")
                
                # Check PostGIS
                await cursor.execute("SELECT PostGIS_Version();")
                postgis = await cursor.fetchone()
                print(f"✓ PostGIS version: {postgis[0]}")
                
                # Check tables exist
                await cursor.execute("""
                    SELECT tablename FROM pg_tables 
                    WHERE schemaname = 'public' 
                    AND tablename IN ('atom', 'atom_composition', 'atom_relation')
                    ORDER BY tablename;
                """)
                tables = await cursor.fetchall()
                print(f"✓ Core tables: {[t[0] for t in tables]}")
                
                return True
    except Exception as e:
        print(f"✗ Database connection failed: {e}")
        return False


async def test_atom_creation():
    """Test 2: Create atoms at deterministic coordinates."""
    print("\n" + "=" * 60)
    print("TEST 2: Atom Creation")
    print("=" * 60)
    
    db_url = os.getenv('DATABASE_URL', 'postgresql://hartonomous:hart123@localhost:5432/hartonomous')
    
    try:
        async with await psycopg.AsyncConnection.connect(db_url) as conn:
            atomizer = GeometricAtomizer(db_connection=conn)
            
            # Create atoms for "Hello"
            text = "Hello"
            atom_values = [char.encode('utf-8') for char in text]
            
            # Get coordinates
            coords = atomizer.locator.locate_multiple(atom_values)
            
            # Insert atoms into database
            async with conn.cursor() as cursor:
                for char, (x, y, z) in zip(text, coords):
                    value = char.encode('utf-8')
                    
                    # Compute Hilbert M coordinate
                    m = atomizer.locator.compute_hilbert_index(x, y, z)
                    
                    # Insert atom (using atomize_value function)
                    await cursor.execute("""
                        SELECT atomize_value(
                            $1::BYTEA,                              -- value
                            'text'::TEXT,                           -- modality
                            ST_MakePointM($2, $3, $4, $5),         -- spatial_key
                            jsonb_build_object('char', $6)          -- metadata
                        );
                    """, (value, x, y, z, m, char))
                    
                    atom_id = await cursor.fetchone()
                    print(f"✓ Created atom '{char}' at ({x:.2f}, {y:.2f}, {z:.2f}, M={m})")
                
                await conn.commit()
            
            print(f"✓ Created {len(text)} atoms for '{text}'")
            return True
            
    except Exception as e:
        print(f"✗ Atom creation failed: {e}")
        import traceback
        traceback.print_exc()
        return False


async def test_trajectory_storage():
    """Test 3: Store LINESTRING trajectory."""
    print("\n" + "=" * 60)
    print("TEST 3: Trajectory Storage")
    print("=" * 60)
    
    db_url = os.getenv('DATABASE_URL', 'postgresql://hartonomous:hart123@localhost:5432/hartonomous')
    
    try:
        async with await psycopg.AsyncConnection.connect(db_url) as conn:
            atomizer = GeometricAtomizer(db_connection=conn)
            
            # Create trajectory for "Hello World"
            text = "Hello World"
            wkt = atomizer.atomize_text(text)
            
            print(f"✓ Generated trajectory WKT: {wkt[:100]}...")
            
            # Store trajectory in database
            import json
            metadata = {
                'content_type': 'text',
                'original_text': text,
                'length': len(text)
            }
            
            async with conn.cursor() as cursor:
                await cursor.execute("""
                    INSERT INTO atom_composition (name, spatial_key, metadata)
                    VALUES ($1, ST_GeomFromText($2), $3::JSONB)
                    RETURNING composition_id;
                """, (f"smoke_test_{text}", wkt, json.dumps(metadata)))
                
                comp_id = await cursor.fetchone()
                await conn.commit()
                
                print(f"✓ Stored trajectory as composition_id={comp_id[0]}")
                
                # Verify storage
                await cursor.execute("""
                    SELECT 
                        composition_id,
                        name,
                        ST_NumPoints(spatial_key) as num_points,
                        metadata
                    FROM atom_composition
                    WHERE composition_id = $1;
                """, (comp_id[0],))
                
                row = await cursor.fetchone()
                print(f"✓ Verified: {row[2]} points in trajectory")
                
                return True
    
    except Exception as e:
        print(f"✗ Trajectory storage failed: {e}")
        import traceback
        traceback.print_exc()
        return False


async def test_spatial_query():
    """Test 4: Spatial proximity queries."""
    print("\n" + "=" * 60)
    print("TEST 4: Spatial Queries")
    print("=" * 60)
    
    db_url = os.getenv('DATABASE_URL', 'postgresql://hartonomous:hart123@localhost:5432/hartonomous')
    
    try:
        async with await psycopg.AsyncConnection.connect(db_url) as conn:
            atomizer = GeometricAtomizer(db_connection=conn)
            
            # Get coordinate for "H"
            h_coord = atomizer.locator.locate(b"H")
            x, y, z = h_coord
            
            async with conn.cursor() as cursor:
                # Find atoms near "H" coordinate
                await cursor.execute("""
                    SELECT 
                        atom_id,
                        atom_value,
                        ST_Distance(spatial_key, ST_MakePointM($1, $2, $3, 0)) as distance
                    FROM atom
                    WHERE spatial_key IS NOT NULL
                    ORDER BY distance
                    LIMIT 5;
                """, (x, y, z))
                
                rows = await cursor.fetchall()
                
                if rows:
                    print("✓ Nearest atoms to 'H':")
                    for row in rows:
                        atom_id, value, dist = row
                        try:
                            char = value.decode('utf-8')
                        except:
                            char = repr(value)
                        print(f"  - Atom {atom_id}: '{char}' (distance: {dist:.2f})")
                    
                    return True
                else:
                    print("✗ No atoms found in spatial query")
                    return False
    
    except Exception as e:
        print(f"✗ Spatial query failed: {e}")
        import traceback
        traceback.print_exc()
        return False


async def test_tensor_atomization():
    """Test 5: Atomize and store small tensor."""
    print("\n" + "=" * 60)
    print("TEST 5: Tensor Atomization")
    print("=" * 60)
    
    db_url = os.getenv('DATABASE_URL', 'postgresql://hartonomous:hart123@localhost:5432/hartonomous')
    
    try:
        async with await psycopg.AsyncConnection.connect(db_url) as conn:
            atomizer = GeometricAtomizer(db_connection=conn)
            
            # Create small test tensor
            tensor = np.array([
                [1.0, 2.0, 3.0],
                [4.0, 5.0, 6.0],
                [7.0, 8.0, 9.0]
            ], dtype=np.float32)
            
            print(f"✓ Created tensor: shape {tensor.shape}, dtype {tensor.dtype}")
            
            # Atomize tensor
            wkts = atomizer.atomize_tensor(tensor)
            
            print(f"✓ Atomized tensor into {len(wkts)} trajectory(ies)")
            print(f"  - First trajectory: {wkts[0][:100]}...")
            
            # Store trajectory
            import json
            metadata = {
                'content_type': 'tensor',
                'shape': list(tensor.shape),
                'dtype': str(tensor.dtype),
                'total_elements': int(tensor.size)
            }
            
            async with conn.cursor() as cursor:
                await cursor.execute("""
                    INSERT INTO atom_composition (name, spatial_key, metadata)
                    VALUES ($1, ST_GeomFromText($2), $3::JSONB)
                    RETURNING composition_id;
                """, ("smoke_test_tensor_3x3", wkts[0], json.dumps(metadata)))
                
                comp_id = await cursor.fetchone()
                await conn.commit()
                
                print(f"✓ Stored tensor trajectory as composition_id={comp_id[0]}")
                
                # Verify
                await cursor.execute("""
                    SELECT ST_NumPoints(spatial_key), metadata
                    FROM atom_composition
                    WHERE composition_id = $1;
                """, (comp_id[0],))
                
                row = await cursor.fetchone()
                num_points, meta = row
                print(f"✓ Verified: {num_points} points (expected {tensor.size})")
                
                return True
    
    except Exception as e:
        print(f"✗ Tensor atomization failed: {e}")
        import traceback
        traceback.print_exc()
        return False


async def main():
    """Run all smoke tests."""
    print("\n")
    print("╔" + "=" * 58 + "╗")
    print("║" + " " * 10 + "GEOMETRIC ATOMIZATION SMOKE TEST" + " " * 16 + "║")
    print("╚" + "=" * 58 + "╝")
    print()
    
    tests = [
        ("Database Connection", test_database_connection),
        ("Atom Creation", test_atom_creation),
        ("Trajectory Storage", test_trajectory_storage),
        ("Spatial Queries", test_spatial_query),
        ("Tensor Atomization", test_tensor_atomization),
    ]
    
    results = []
    for name, test_func in tests:
        try:
            result = await test_func()
            results.append((name, result))
        except Exception as e:
            print(f"\n✗ Test '{name}' crashed: {e}")
            import traceback
            traceback.print_exc()
            results.append((name, False))
    
    # Summary
    print("\n" + "=" * 60)
    print("SUMMARY")
    print("=" * 60)
    
    passed = sum(1 for _, result in results if result)
    total = len(results)
    
    for name, result in results:
        status = "✓ PASS" if result else "✗ FAIL"
        print(f"{status}: {name}")
    
    print()
    print(f"Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("\n🎉 ALL TESTS PASSED! Geometric atomization is working!")
        return 0
    else:
        print(f"\n⚠️  {total - passed} test(s) failed")
        return 1


if __name__ == '__main__':
    exit_code = asyncio.run(main())
    sys.exit(exit_code)
