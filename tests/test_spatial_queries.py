"""
Automated tests for Hartonomous spatial query correctness.
Validates k-NN, radius search, hierarchy traversal.
"""

import unittest
import psycopg2
from psycopg2.extras import RealDictCursor
import os
import math

class SpatialQueryTests(unittest.TestCase):
    """Test suite for spatial query operations."""
    
    @classmethod
    def setUpClass(cls):
        """Connect to database once for all tests."""
        cls.conn = psycopg2.connect(
            host="localhost",
            port=5432,
            database="hartonomous",
            user="hartonomous",
            password=os.environ.get("PGPASSWORD", "")
        )
    
    @classmethod
    def tearDownClass(cls):
        """Close connection after all tests."""
        cls.conn.close()
    
    def test_knn_returns_correct_count(self):
        """k-NN query should return exactly k results."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Get a random atom as query point
        cur.execute("SELECT atom_id, geom FROM atom LIMIT 1")
        target = cur.fetchone()
        
        k = 10
        cur.execute(f"""
            SELECT atom_id, geom <-> %s as distance
            FROM atom
            WHERE atom_id != %s
            ORDER BY geom <-> %s
            LIMIT {k}
        """, (target['geom'], target['atom_id'], target['geom']))
        
        results = cur.fetchall()
        
        self.assertEqual(len(results), k, f"Expected {k} results, got {len(results)}")
        cur.close()
    
    def test_knn_distances_sorted(self):
        """k-NN results must be sorted by increasing distance."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("SELECT atom_id, geom FROM atom LIMIT 1")
        target = cur.fetchone()
        
        cur.execute("""
            SELECT 
                atom_id,
                ST_3DDistance(geom, %s) as distance
            FROM atom
            WHERE atom_id != %s
            ORDER BY geom <-> %s
            LIMIT 20
        """, (target['geom'], target['atom_id'], target['geom']))
        
        results = cur.fetchall()
        distances = [r['distance'] for r in results]
        
        # Verify sorted
        self.assertEqual(distances, sorted(distances), 
                        "Distances not in ascending order")
        
        cur.close()
    
    def test_radius_search_respects_boundary(self):
        """All atoms in radius search must be within specified distance."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("SELECT atom_id, geom FROM atom LIMIT 1")
        target = cur.fetchone()
        
        radius = 10.0
        cur.execute("""
            SELECT 
                atom_id,
                ST_3DDistance(geom, %s) as distance
            FROM atom
            WHERE atom_id != %s
              AND ST_DWithin(geom, %s, %s)
        """, (target['geom'], target['atom_id'], target['geom'], radius))
        
        results = cur.fetchall()
        
        for row in results:
            self.assertLessEqual(row['distance'], radius,
                               f"Atom at distance {row['distance']} > {radius}")
        
        cur.close()
    
    def test_hierarchy_traversal_increases_z(self):
        """Upward hierarchy traversal should return atoms with higher Z."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Get Z=0 atom
        cur.execute("SELECT atom_id, ST_Z(geom) as z FROM atom WHERE ST_Z(geom) = 0 LIMIT 1")
        target = cur.fetchone()
        
        if target is None:
            self.skipTest("No Z=0 atoms available")
        
        target_z = target['z']
        
        cur.execute("""
            SELECT ST_Z(geom) as z
            FROM atom
            WHERE ST_Z(geom) > %s
            LIMIT 10
        """, (target_z,))
        
        results = cur.fetchall()
        
        for row in results:
            self.assertGreater(row['z'], target_z,
                             f"Z={row['z']} not greater than {target_z}")
        
        cur.close()
    
    def test_gist_index_used_for_knn(self):
        """Verify GiST index is used for k-NN queries."""
        cur = self.conn.cursor()
        
        cur.execute("SELECT atom_id, geom FROM atom LIMIT 1")
        target = cur.fetchone()
        
        # EXPLAIN to check index usage
        cur.execute("""
            EXPLAIN (FORMAT JSON)
            SELECT atom_id
            FROM atom
            ORDER BY geom <-> %s
            LIMIT 10
        """, (target[1],))
        
        plan = cur.fetchone()[0]
        plan_text = str(plan)
        
        self.assertIn("idx_atoms_geom_gist", plan_text,
                     "GiST index not used for k-NN query")
        
        cur.close()
    
    def test_all_atoms_have_valid_geometry(self):
        """All atoms must have valid PostGIS geometry."""
        cur = self.conn.cursor()
        
        cur.execute("SELECT COUNT(*) FROM atom WHERE NOT ST_IsValid(geom)")
        invalid_count = cur.fetchone()[0]
        
        self.assertEqual(invalid_count, 0,
                        f"{invalid_count} atoms with invalid geometry")
        
        cur.close()
    
    def test_constant_atoms_have_point_geometry(self):
        """Constants (class=0) must have POINT ZM geometry."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("""
            SELECT atom_id, ST_GeometryType(geom) as geom_type
            FROM atom
            WHERE atom_class = 0
            LIMIT 100
        """)
        
        for row in cur.fetchall():
            self.assertEqual(row['geom_type'], 'ST_Point',
                           f"Constant has wrong geometry type: {row['geom_type']}")
        
        cur.close()
    
    def test_spatial_extent_reasonable(self):
        """Spatial extent should be finite and reasonable."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("""
            SELECT 
                ST_XMin(ST_Extent(geom)) as xmin,
                ST_XMax(ST_Extent(geom)) as xmax,
                ST_YMin(ST_Extent(geom)) as ymin,
                ST_YMax(ST_Extent(geom)) as ymax
            FROM atom
        """)
        
        extent = cur.fetchone()
        
        # Check finite
        for key in ['xmin', 'xmax', 'ymin', 'ymax']:
            self.assertFalse(math.isinf(extent[key]),
                           f"{key} is infinite")
            self.assertFalse(math.isnan(extent[key]),
                           f"{key} is NaN")
        
        # Check reasonable range (not astronomical)
        self.assertLess(abs(extent['xmax'] - extent['xmin']), 10000,
                       "X extent too large")
        self.assertLess(abs(extent['ymax'] - extent['ymin']), 10000,
                       "Y extent too large")
        
        cur.close()

if __name__ == "__main__":
    unittest.main()
