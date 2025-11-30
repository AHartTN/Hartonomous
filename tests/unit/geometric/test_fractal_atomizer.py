"""
Unit Tests: FractalAtomizer

Tests deduplication, composition creation, and greedy crystallization (no DB required).

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest
from api.services.geometric_atomization import FractalAtomizer


class TestHashFunctions:
    """Test deterministic hashing."""
    
    def test_hash_value_deterministic(self):
        """Same value produces same hash."""
        atomizer = FractalAtomizer()
        
        value = b"Hello"
        hash1 = atomizer.hash_value(value)
        hash2 = atomizer.hash_value(value)
        
        assert hash1 == hash2
        assert len(hash1) == 32  # SHA-256 = 32 bytes
    
    def test_hash_value_unique(self):
        """Different values produce different hashes."""
        atomizer = FractalAtomizer()
        
        hash_a = atomizer.hash_value(b"A")
        hash_b = atomizer.hash_value(b"B")
        hash_c = atomizer.hash_value(b"C")
        
        assert hash_a != hash_b
        assert hash_b != hash_c
        assert hash_a != hash_c
    
    def test_hash_composition_deterministic(self):
        """Same child IDs produce same composition hash."""
        atomizer = FractalAtomizer()
        
        child_ids = [1, 2, 3]
        hash1 = atomizer.hash_composition(child_ids)
        hash2 = atomizer.hash_composition(child_ids)
        
        assert hash1 == hash2
        assert len(hash1) == 32
    
    def test_hash_composition_order_matters(self):
        """Order of child IDs affects hash (not commutative)."""
        atomizer = FractalAtomizer()
        
        hash_123 = atomizer.hash_composition([1, 2, 3])
        hash_321 = atomizer.hash_composition([3, 2, 1])
        
        assert hash_123 != hash_321  # Order matters


class TestPrimitiveLocations:
    """Test primitive atom coordinate calculation."""
    
    def test_locate_primitive_deterministic(self):
        """Same primitive always maps to same coordinate."""
        atomizer = FractalAtomizer()
        
        value = b"Hello"
        coord1 = atomizer.locate_primitive(value)
        coord2 = atomizer.locate_primitive(value)
        
        assert coord1 == coord2
    
    def test_locate_primitive_unique(self):
        """Different primitives map to different coordinates."""
        atomizer = FractalAtomizer()
        
        coord_a = atomizer.locate_primitive(b"A")
        coord_b = atomizer.locate_primitive(b"B")
        coord_c = atomizer.locate_primitive(b"C")
        
        coords = [coord_a, coord_b, coord_c]
        assert len(coords) == len(set(coords))  # All unique
    
    def test_locate_primitive_in_range(self):
        """Coordinates stay within specified range."""
        atomizer = FractalAtomizer(coordinate_range=1e6)
        
        for i in range(100):
            value = f"test_{i}".encode('utf-8')
            x, y, z = atomizer.locate_primitive(value)
            
            assert -1e6 <= x <= 1e6
            assert -1e6 <= y <= 1e6
            assert -1e6 <= z <= 1e6


class TestCompositionLocations:
    """Test composition atom coordinate calculation."""
    
    def test_locate_composition_concept_strategy(self):
        """Concept strategy: composition gets independent coordinate."""
        atomizer = FractalAtomizer()
        
        # Create some test atoms in cache
        atomizer.coord_cache[1] = (100.0, 200.0, 300.0)
        atomizer.coord_cache[2] = (400.0, 500.0, 600.0)
        
        # Composition coordinate (concept strategy)
        coord = atomizer.locate_composition([1, 2], strategy='concept')
        
        # Should be valid coordinate
        assert isinstance(coord, tuple)
        assert len(coord) == 3
        assert all(isinstance(c, float) for c in coord)
        
        # Should be deterministic
        coord2 = atomizer.locate_composition([1, 2], strategy='concept')
        assert coord == coord2
    
    def test_locate_composition_midpoint_strategy(self):
        """Midpoint strategy: composition at average of children."""
        atomizer = FractalAtomizer()
        
        # Setup child coordinates
        atomizer.coord_cache[1] = (0.0, 0.0, 0.0)
        atomizer.coord_cache[2] = (100.0, 200.0, 300.0)
        
        coord = atomizer.locate_composition([1, 2], strategy='midpoint')
        
        # Should be midpoint
        assert coord == (50.0, 100.0, 150.0)
    
    def test_locate_composition_three_children(self):
        """Midpoint with three children."""
        atomizer = FractalAtomizer()
        
        atomizer.coord_cache[1] = (0.0, 0.0, 0.0)
        atomizer.coord_cache[2] = (300.0, 300.0, 300.0)
        atomizer.coord_cache[3] = (600.0, 600.0, 600.0)
        
        coord = atomizer.locate_composition([1, 2, 3], strategy='midpoint')
        
        # Average: (0+300+600)/3 = 300
        assert coord == (300.0, 300.0, 300.0)


class TestPrimitiveCreation:
    """Test primitive atom creation (no database)."""
    
    @pytest.mark.asyncio
    async def test_create_primitive_no_db(self):
        """Create primitive without database connection."""
        atomizer = FractalAtomizer()
        
        value = b"Hello"
        atom_id = await atomizer.get_or_create_primitive(value)
        
        # Should assign temporary ID
        assert isinstance(atom_id, int)
        assert atom_id > 0
        
        # Should be cached
        assert value in atomizer.atom_cache
        assert atomizer.atom_cache[value] == atom_id
        
        # Coordinate should be cached
        assert atom_id in atomizer.coord_cache
    
    @pytest.mark.asyncio
    async def test_primitive_deduplication(self):
        """Same primitive reuses same atom ID."""
        atomizer = FractalAtomizer()
        
        value = b"Hello"
        id1 = await atomizer.get_or_create_primitive(value)
        id2 = await atomizer.get_or_create_primitive(value)
        id3 = await atomizer.get_or_create_primitive(value)
        
        # Should all be the same ID
        assert id1 == id2 == id3
    
    @pytest.mark.asyncio
    async def test_different_primitives_different_ids(self):
        """Different primitives get different IDs."""
        atomizer = FractalAtomizer()
        
        id_a = await atomizer.get_or_create_primitive(b"A")
        id_b = await atomizer.get_or_create_primitive(b"B")
        id_c = await atomizer.get_or_create_primitive(b"C")
        
        ids = [id_a, id_b, id_c]
        assert len(ids) == len(set(ids))  # All unique


class TestCompositionCreation:
    """Test composition atom creation (no database)."""
    
    @pytest.mark.asyncio
    async def test_create_composition_no_db(self):
        """Create composition without database."""
        atomizer = FractalAtomizer()
        
        # Create primitives first
        id1 = await atomizer.get_or_create_primitive(b"H")
        id2 = await atomizer.get_or_create_primitive(b"i")
        
        # Create composition
        comp_id = await atomizer.get_or_create_composition([id1, id2])
        
        assert isinstance(comp_id, int)
        assert comp_id > 0
        
        # Should be cached
        assert (id1, id2) in atomizer.composition_cache
        assert atomizer.composition_cache[(id1, id2)] == comp_id
    
    @pytest.mark.asyncio
    async def test_composition_deduplication(self):
        """Same composition reuses same ID."""
        atomizer = FractalAtomizer()
        
        id1 = await atomizer.get_or_create_primitive(b"A")
        id2 = await atomizer.get_or_create_primitive(b"B")
        
        comp_id1 = await atomizer.get_or_create_composition([id1, id2])
        comp_id2 = await atomizer.get_or_create_composition([id1, id2])
        comp_id3 = await atomizer.get_or_create_composition([id1, id2])
        
        assert comp_id1 == comp_id2 == comp_id3
    
    @pytest.mark.asyncio
    async def test_composition_order_matters(self):
        """[A,B] is different from [B,A]."""
        atomizer = FractalAtomizer()
        
        id1 = await atomizer.get_or_create_primitive(b"A")
        id2 = await atomizer.get_or_create_primitive(b"B")
        
        comp_ab = await atomizer.get_or_create_composition([id1, id2])
        comp_ba = await atomizer.get_or_create_composition([id2, id1])
        
        assert comp_ab != comp_ba
    
    @pytest.mark.asyncio
    async def test_composition_empty_raises(self):
        """Empty composition should raise error."""
        atomizer = FractalAtomizer()
        
        with pytest.raises(ValueError, match="empty child_ids"):
            await atomizer.get_or_create_composition([])


class TestRecursiveComposition:
    """Test higher-order compositions (compositions of compositions)."""
    
    @pytest.mark.asyncio
    async def test_composition_of_compositions(self):
        """Create composition containing other compositions."""
        atomizer = FractalAtomizer()
        
        # Level 0: Primitives
        id_h = await atomizer.get_or_create_primitive(b"H")
        id_e = await atomizer.get_or_create_primitive(b"e")
        id_l = await atomizer.get_or_create_primitive(b"l")
        id_o = await atomizer.get_or_create_primitive(b"o")
        
        # Level 1: "He" and "ll"
        comp_he = await atomizer.get_or_create_composition([id_h, id_e])
        comp_ll = await atomizer.get_or_create_composition([id_l, id_l])
        
        # Level 2: "Hello" = ["He", "ll", "o"]
        comp_hello = await atomizer.get_or_create_composition([comp_he, comp_ll, id_o])
        
        assert isinstance(comp_hello, int)
        assert comp_hello > 0
        
        # Verify caching
        assert (comp_he, comp_ll, id_o) in atomizer.composition_cache


class TestCrystallizeSequence:
    """Test greedy sequence crystallization."""
    
    @pytest.mark.asyncio
    async def test_crystallize_simple_no_greedy(self):
        """Crystallize without greedy mode."""
        atomizer = FractalAtomizer()
        
        sequence = [b"H", b"e", b"l", b"l", b"o"]
        atom_ids = await atomizer.crystallize_sequence(sequence, greedy=False)
        
        # Should create 5 primitives (4 unique: H, e, l, o)
        assert len(atom_ids) == 5
        
        # 'l' appears twice - should have same ID
        assert atom_ids[2] == atom_ids[3]
    
    @pytest.mark.asyncio
    async def test_crystallize_with_known_pairs(self):
        """Greedy crystallization collapses known pairs."""
        atomizer = FractalAtomizer()
        
        # Create primitives
        id_a = await atomizer.get_or_create_primitive(b"A")
        id_b = await atomizer.get_or_create_primitive(b"B")
        id_c = await atomizer.get_or_create_primitive(b"C")
        
        # Pre-create composition for "AB"
        comp_ab = await atomizer.get_or_create_composition([id_a, id_b])
        
        # Now crystallize sequence "ABC"
        sequence = [b"A", b"B", b"C"]
        atom_ids = await atomizer.crystallize_sequence(sequence, greedy=True)
        
        # Should collapse to [comp_ab, id_c] instead of [id_a, id_b, id_c]
        assert len(atom_ids) == 2
        assert atom_ids[0] == comp_ab
        assert atom_ids[1] == id_c
    
    @pytest.mark.asyncio
    async def test_crystallize_no_known_patterns(self):
        """Greedy mode with no known patterns = same as non-greedy."""
        atomizer = FractalAtomizer()
        
        sequence = [b"X", b"Y", b"Z"]
        
        # Crystallize with greedy (but no patterns learned yet)
        atom_ids = await atomizer.crystallize_sequence(sequence, greedy=True)
        
        # Should just be 3 primitives
        assert len(atom_ids) == 3


class TestCompressionRatio:
    """Test compression ratio calculation."""
    
    @pytest.mark.asyncio
    async def test_repeated_pattern_compression(self):
        """Repeated patterns achieve high compression."""
        atomizer = FractalAtomizer()
        
        # Create primitive + composition
        id_a = await atomizer.get_or_create_primitive(b"A")
        comp_aa = await atomizer.get_or_create_composition([id_a, id_a])
        
        # Sequence: "AAAA" (4 As)
        sequence = [b"A"] * 4
        
        # Without greedy: 4 primitives
        ids_simple = await atomizer.crystallize_sequence(sequence, greedy=False)
        assert len(ids_simple) == 4
        
        # With greedy: 2 compositions (AA, AA)
        ids_greedy = await atomizer.crystallize_sequence(sequence, greedy=True)
        assert len(ids_greedy) == 2  # [comp_aa, comp_aa]
        
        # 50% compression!
        compression_ratio = len(ids_simple) / len(ids_greedy)
        assert compression_ratio == 2.0


if __name__ == '__main__':
    pytest.main([__file__, '-v'])
