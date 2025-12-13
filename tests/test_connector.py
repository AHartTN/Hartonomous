"""Unit tests for Hartonomous Python connector"""

import unittest
import os
from connector import Hartonomous, Atom


class TestHartonomousConnector(unittest.TestCase):
    """Test suite for database connector"""
    
    @classmethod
    def setUpClass(cls):
        """Initialize connection to test database"""
        cls.hart = Hartonomous({
            'host': os.getenv('PGHOST', 'localhost'),
            'port': int(os.getenv('PGPORT', '5432')),
            'database': os.getenv('PGDATABASE', 'hartonomous'),
            'user': os.getenv('PGUSER', 'postgres'),
            'password': os.getenv('PGPASSWORD', ''),
        })
    
    @classmethod
    def tearDownClass(cls):
        """Clean up connections"""
        cls.hart.close()
    
    def test_connection(self):
        """Test database connection"""
        status = self.hart.status()
        self.assertIsInstance(status, dict)
        self.assertIn('model_version', status)
    
    def test_coordinate_search(self):
        """Test search by coordinates"""
        # Search near origin
        results = self.hart.search(x=0.0, y=0.0, z=0.0, m=0.0, k=10)
        
        self.assertIsInstance(results, list)
        self.assertLessEqual(len(results), 10)
        
        # Verify Atom structure
        if len(results) > 0:
            atom = results[0]
            self.assertIsInstance(atom, Atom)
            self.assertIsInstance(atom.atom_hash, bytes)
            self.assertEqual(len(atom.atom_hash), 32)  # BLAKE3 = 32 bytes
            self.assertIsInstance(atom.x, float)
            self.assertIsInstance(atom.y, float)
            self.assertIsInstance(atom.z, float)
            self.assertIsInstance(atom.m, float)
    
    def test_cortex_status(self):
        """Test Cortex status retrieval"""
        status = self.hart.status()
        
        self.assertIn('model_version', status)
        self.assertIn('atoms_processed', status)
        self.assertIn('recalibrations', status)
        self.assertIn('current_stress', status)
        
        # Verify types
        self.assertIsInstance(status['model_version'], int)
        self.assertIsInstance(status['atoms_processed'], int)
        self.assertIsInstance(status['current_stress'], (int, float))
    
    def test_insert_atom(self):
        """Test single atom insertion"""
        test_atom = Atom(
            atom_hash=b'\x00' * 32,
            x=0.5,
            y=0.5,
            z=0.0,
            m=1.0,
            atom_class=0,
            modality=1,
            metadata={'test': True}
        )
        
        # Insert (may already exist)
        result = self.hart.connector.insert_atom(test_atom, hilbert_index=0)
        self.assertIsInstance(result, bool)


class TestSpatialQueries(unittest.TestCase):
    """Test spatial query operations"""
    
    @classmethod
    def setUpClass(cls):
        """Set up test connection"""
        cls.hart = Hartonomous()
    
    @classmethod
    def tearDownClass(cls):
        """Clean up"""
        cls.hart.close()
    
    def test_knn_determinism(self):
        """Test k-NN query returns consistent results"""
        # Insert test atoms first
        for i in range(5):
            atom = Atom(
                atom_hash=bytes([i] * 32),
                x=float(i) / 10.0,
                y=float(i) / 10.0,
                z=0.0,
                m=1.0,
                atom_class=0,
                modality=1
            )
            self.hart.connector.insert_atom(atom, hilbert_index=i)
        
        # Query twice
        target = bytes([2] * 32)
        results1 = self.hart.query(target, k=3)
        results2 = self.hart.query(target, k=3)
        
        # Should return same results
        self.assertEqual(len(results1), len(results2))
        
        if len(results1) > 0:
            self.assertEqual(results1[0].atom_hash, results2[0].atom_hash)


if __name__ == '__main__':
    unittest.main()
