"""Test GGUFAtomizer."""

import pytest

from api.services.model_atomization import GGUFAtomizer

pytestmark = [pytest.mark.asyncio, pytest.mark.integration, pytest.mark.gguf]


class TestGGUFAtomizer:
    """Test GGUF model atomization."""

    async def test_atomizer_initialization(self):
        """Test atomizer can be initialized."""
        atomizer = GGUFAtomizer(threshold=0.01)

        assert atomizer.threshold == 0.01
        assert len(atomizer.cache) == 0
        assert atomizer.stats["total_processed"] == 0

    async def test_atomizer_threshold(self):
        """Test atomizer threshold configuration."""
        atomizer = GGUFAtomizer(threshold=0.01)

        assert atomizer.threshold == 0.01
        assert atomizer.encoder.sparse_threshold == 0.01

        # Test default threshold
        default_atomizer = GGUFAtomizer()
        assert default_atomizer.threshold == 1e-6

    async def test_weight_deduplication(self, db_connection, clean_db):
        """Test that identical weights are deduplicated."""
        atomizer = GGUFAtomizer()

        # Atomize same weight twice
        weight_id_1 = await atomizer._atomize_weight(db_connection, 0.5)
        weight_id_2 = await atomizer._atomize_weight(db_connection, 0.5)

        assert weight_id_1 == weight_id_2  # Should be same atom
        assert atomizer.stats["atoms_deduped"] == 1
