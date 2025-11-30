"""Test image parser."""

import pytest
from PIL import Image

from src.ingestion.parsers.image_parser import ImageParser

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


class TestImageParser:
    """Test image parsing and atomization."""

    async def test_parse_simple_image(self, db_connection, clean_db, tmp_path):
        """Test parsing a simple image."""
        # Create test image
        img = Image.new("RGB", (10, 10), color="red")
        img_path = tmp_path / "test.png"
        img.save(img_path)

        parser = ImageParser()
        parent_atom_id = await parser.parse(img_path, db_connection)

        assert parent_atom_id is not None

        # Verify parent atom exists
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s", (parent_atom_id,)
            )
            metadata = (await cur.fetchone())[0]

            assert metadata["modality"] == "image"
            assert metadata["width"] == 10
            assert metadata["height"] == 10
            assert metadata["channels"] == 3

    async def test_image_composition(self, db_connection, clean_db, tmp_path):
        """Test that image creates proper composition."""
        img = Image.new("RGB", (4, 4), color="blue")
        img_path = tmp_path / "test.png"
        img.save(img_path)

        parser = ImageParser()
        parent_atom_id = await parser.parse(img_path, db_connection)

        # Verify compositions were created
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_atom_id,),
            )
            comp_count = (await cur.fetchone())[0]

            assert comp_count > 0  # At least some pixel chunks atomized

    async def test_grayscale_image(self, db_connection, clean_db, tmp_path):
        """Test parsing grayscale image (converted to RGB)."""
        img = Image.new("L", (5, 5), color=128)
        img_path = tmp_path / "gray.png"
        img.save(img_path)

        parser = ImageParser()
        parent_atom_id = await parser.parse(img_path, db_connection)

        assert parent_atom_id is not None
