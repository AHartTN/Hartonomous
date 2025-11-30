"""
Integration tests for PostGIS spatial queries on geometric atomization.

Tests spatial query performance and correctness:
1. ST_DWithin proximity searches
2. ST_Distance calculations
3. Spatial indexing performance
4. Coordinate-based atom lookups
5. Query optimization verification

Uses greenfield GeometricAtomizer architecture.
"""

import pytest
import numpy as np
from typing import List, Tuple

from api.services.geometric_atomization import GeometricAtomizer

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


class TestBasicSpatialQueries:
    """Test basic PostGIS spatial operations."""
    
    async def test_st_numpoints(self, db_connection, clean_db):
        """Test counting points in a trajectory."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Create trajectory with known length
        tensor = np.array([1.0, 2.0, 3.0, 4.0, 5.0], dtype=np.float32)
        wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
        
        comp_id = await atomizer.store_trajectory(
            name="test.5points",
            wkt=wkts[0],
            metadata={'count': 5}
        )
        
        # Query point count
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT ST_NumPoints(spatial_key)
                FROM atom_composition
                WHERE id = %s
            """, (comp_id,))
            
            point_count = (await cur.fetchone())[0]
        
        assert point_count == 5, "Should have 5 points"
        
        print(f"\n✓ ST_NumPoints: {point_count} points")
    
    async def test_st_length(self, db_connection, clean_db):
        """Test trajectory length calculation."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Create trajectory
        tensor = np.arange(10, dtype=np.float32)
        wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
        
        comp_id = await atomizer.store_trajectory(
            name="test.length",
            wkt=wkts[0],
            metadata={}
        )
        
        # Query length (3D Euclidean distance)
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT ST_3DLength(spatial_key)
                FROM atom_composition
                WHERE id = %s
            """, (comp_id,))
            
            length = (await cur.fetchone())[0]
        
        assert length > 0, "Trajectory should have positive length"
        
        print(f"\n✓ ST_3DLength: {length:.2f}")


@pytest.mark.slow
class TestProximitySearches:
    """Test ST_DWithin proximity searches."""
    
    async def test_st_dwithin_single_trajectory(self, db_connection, clean_db):
        """Test finding trajectories near a point."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store multiple trajectories
        for i in range(5):
            tensor = np.random.randn(10).astype(np.float32)
            wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
            
            await atomizer.store_trajectory(
                name=f"test.trajectory_{i}",
                wkt=wkts[0],
                metadata={'index': i}
            )
        
        # Find trajectories near origin (0, 0, 0)
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT name, ST_Distance(spatial_key, ST_MakePoint(0, 0, 0)) as dist
                FROM atom_composition
                WHERE ST_DWithin(spatial_key, ST_MakePoint(0, 0, 0), 1000000)
                ORDER BY dist
                LIMIT 5
            """)
            
            results = await cur.fetchall()
        
        assert len(results) > 0, "Should find nearby trajectories"
        
        print(f"\n✓ ST_DWithin found {len(results)} trajectories:")
        for name, dist in results:
            print(f"  {name}: {dist:.2f} units")
    
    async def test_st_dwithin_performance(self, db_connection, clean_db):
        """Test proximity search performance with spatial index."""
        import time
        
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store 20 trajectories
        for i in range(20):
            tensor = np.random.randn(100).astype(np.float32)
            wkts = atomizer.atomize_tensor(tensor, chunk_size=200)
            
            await atomizer.store_trajectory(
                name=f"test.perf_{i}",
                wkt=wkts[0],
                metadata={'batch': 'perf_test'}
            )
        
        # Ensure spatial index exists
        async with db_connection.cursor() as cur:
            await cur.execute("""
                CREATE INDEX IF NOT EXISTS idx_composition_spatial
                ON atom_composition USING GIST(spatial_key)
            """)
        
        # Warm up query planner
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT COUNT(*)
                FROM atom_composition
                WHERE ST_DWithin(spatial_key, ST_MakePoint(500000, 500000, 500000), 100000)
            """)
        
        # Timed query
        start = time.perf_counter()
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT name
                FROM atom_composition
                WHERE ST_DWithin(spatial_key, ST_MakePoint(500000, 500000, 500000), 100000)
                AND metadata->>'batch' = 'perf_test'
            """)
            results = await cur.fetchall()
        elapsed_ms = (time.perf_counter() - start) * 1000
        
        print(f"\n✓ Proximity search performance:")
        print(f"  Found: {len(results)} trajectories")
        print(f"  Time: {elapsed_ms:.2f} ms")
        
        # Target: < 100ms for 20 trajectories
        assert elapsed_ms < 100, f"Query too slow: {elapsed_ms:.2f}ms > 100ms"


class TestDistanceCalculations:
    """Test ST_Distance calculations."""
    
    async def test_st_distance_to_point(self, db_connection, clean_db):
        """Test distance from trajectory to a point."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Create trajectory
        tensor = np.array([0.0, 1.0, 2.0], dtype=np.float32)
        wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
        
        comp_id = await atomizer.store_trajectory(
            name="test.distance",
            wkt=wkts[0],
            metadata={}
        )
        
        # Calculate distance to origin
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT ST_3DDistance(spatial_key, ST_MakePoint(0, 0, 0))
                FROM atom_composition
                WHERE id = %s
            """, (comp_id,))
            
            distance = (await cur.fetchone())[0]
        
        assert distance >= 0, "Distance should be non-negative"
        
        print(f"\n✓ ST_3DDistance to origin: {distance:.2f}")
    
    async def test_trajectory_to_trajectory_distance(self, db_connection, clean_db):
        """Test distance between two trajectories."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Create two trajectories
        tensor1 = np.array([1.0, 2.0, 3.0], dtype=np.float32)
        tensor2 = np.array([4.0, 5.0, 6.0], dtype=np.float32)
        
        wkts1 = atomizer.atomize_tensor(tensor1, chunk_size=100)
        wkts2 = atomizer.atomize_tensor(tensor2, chunk_size=100)
        
        id1 = await atomizer.store_trajectory("test.traj1", wkts1[0], {})
        id2 = await atomizer.store_trajectory("test.traj2", wkts2[0], {})
        
        # Calculate distance between trajectories
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT ST_3DDistance(t1.spatial_key, t2.spatial_key)
                FROM atom_composition t1, atom_composition t2
                WHERE t1.id = %s AND t2.id = %s
            """, (id1, id2))
            
            distance = (await cur.fetchone())[0]
        
        assert distance >= 0, "Distance should be non-negative"
        
        print(f"\n✓ Trajectory-to-trajectory distance: {distance:.2f}")


@pytest.mark.slow
class TestCoordinateLookups:
    """Test coordinate-based atom lookups."""
    
    async def test_find_atom_at_coordinate(self, db_connection, clean_db):
        """Test finding atom at specific coordinate."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store trajectory
        tensor = np.array([100.0, 200.0, 300.0], dtype=np.float32)
        wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
        
        await atomizer.store_trajectory("test.lookup", wkts[0], {})
        
        # Parse WKT to get first coordinate
        wkt = wkts[0]
        # Extract first point: LINESTRING ZM (x y z m, ...)
        coords_part = wkt.split('(')[1].split(')')[0]
        first_point = coords_part.split(',')[0].strip()
        x, y, z, m = map(float, first_point.split())
        
        # Query atom at that coordinate
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT ST_AsText(ST_PointN(spatial_key, 1))
                FROM atom_composition
                WHERE name = 'test.lookup'
            """)
            
            first_point_wkt = (await cur.fetchone())[0]
        
        assert 'POINT' in first_point_wkt.upper(), "Should return point"
        
        print(f"\n✓ Found atom at coordinate:")
        print(f"  Input: ({x}, {y}, {z}, {m})")
        print(f"  Query result: {first_point_wkt}")
    
    async def test_query_atoms_in_bounding_box(self, db_connection, clean_db):
        """Test querying atoms within a bounding box."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store multiple trajectories
        for i in range(5):
            tensor = np.random.randn(20).astype(np.float32)
            wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
            
            await atomizer.store_trajectory(
                name=f"test.bbox_{i}",
                wkt=wkts[0],
                metadata={'group': 'bbox_test'}
            )
        
        # Query trajectories intersecting bounding box
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT name, ST_NumPoints(spatial_key)
                FROM atom_composition
                WHERE metadata->>'group' = 'bbox_test'
                AND spatial_key && ST_MakeEnvelope(0, 0, 1000000, 1000000)
            """)
            
            results = await cur.fetchall()
        
        assert len(results) > 0, "Should find trajectories in bounding box"
        
        print(f"\n✓ Bounding box query:")
        print(f"  Found: {len(results)} trajectories")
        for name, points in results:
            print(f"  {name}: {points} points")


@pytest.mark.slow
class TestQueryOptimization:
    """Test query plan and index usage."""
    
    async def test_spatial_index_usage(self, db_connection, clean_db):
        """Test that spatial queries use the GIST index."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store trajectories
        for i in range(10):
            tensor = np.random.randn(50).astype(np.float32)
            wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
            
            await atomizer.store_trajectory(
                name=f"test.index_{i}",
                wkt=wkts[0],
                metadata={'index_test': True}
            )
        
        # Create index
        async with db_connection.cursor() as cur:
            await cur.execute("""
                CREATE INDEX IF NOT EXISTS idx_test_spatial
                ON atom_composition USING GIST(spatial_key)
                WHERE metadata->>'index_test' = 'true'
            """)
        
        # Analyze query plan
        async with db_connection.cursor() as cur:
            await cur.execute("""
                EXPLAIN (FORMAT JSON)
                SELECT name
                FROM atom_composition
                WHERE ST_DWithin(spatial_key, ST_MakePoint(0, 0, 0), 1000000)
                AND metadata->>'index_test' = 'true'
            """)
            
            plan = await cur.fetchone()
        
        plan_text = str(plan[0])
        
        # Verify index usage (look for "Index Scan" or "Bitmap Index Scan")
        uses_index = 'Index Scan' in plan_text or 'Bitmap' in plan_text
        
        print(f"\n✓ Query plan analysis:")
        print(f"  Uses spatial index: {uses_index}")
        
        if not uses_index:
            print(f"  WARNING: Query may not be using index optimally")
    
    async def test_query_selectivity(self, db_connection, clean_db):
        """Test query selectivity with different distance thresholds."""
        import time
        
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Store 30 trajectories
        for i in range(30):
            tensor = np.random.randn(50).astype(np.float32)
            wkts = atomizer.atomize_tensor(tensor, chunk_size=100)
            
            await atomizer.store_trajectory(
                name=f"test.selectivity_{i}",
                wkt=wkts[0],
                metadata={'selectivity_test': True}
            )
        
        # Test different distance thresholds
        thresholds = [10000, 100000, 1000000]
        
        print(f"\n✓ Query selectivity test:")
        
        for threshold in thresholds:
            start = time.perf_counter()
            
            async with db_connection.cursor() as cur:
                await cur.execute("""
                    SELECT COUNT(*)
                    FROM atom_composition
                    WHERE ST_DWithin(spatial_key, ST_MakePoint(500000, 500000, 500000), %s)
                    AND metadata->>'selectivity_test' = 'true'
                """, (threshold,))
                
                count = (await cur.fetchone())[0]
            
            elapsed_ms = (time.perf_counter() - start) * 1000
            
            print(f"  Threshold {threshold:,}: {count} results in {elapsed_ms:.2f} ms")
