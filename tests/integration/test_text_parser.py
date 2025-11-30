"""Test text parser."""

from pathlib import Path

import pytest

from src.ingestion.parsers.text_parser import TextParser

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


class TestTextParser:
    """Test text parsing."""

    async def test_parse_simple_text(self, db_connection, clean_db, tmp_path):
        """Test parsing simple text file."""
        text_file = tmp_path / "test.txt"
        text_file.write_text("Hello")

        parser = TextParser()
        parent_atom_id = await parser.parse(text_file, db_connection)

        assert parent_atom_id is not None

        # Verify parent atom
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s", (parent_atom_id,)
            )
            metadata = (await cur.fetchone())[0]

            assert metadata["modality"] == "text"
            assert metadata["char_count"] == 5
            assert metadata["word_count"] == 1

    async def test_text_character_composition(self, db_connection, clean_db, tmp_path):
        """Test text is decomposed into characters."""
        text_file = tmp_path / "test.txt"
        text_file.write_text("ABC")

        parser = TextParser()
        parent_atom_id = await parser.parse(text_file, db_connection)

        # Verify 3 character atoms were linked
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_atom_id,),
            )
            count = (await cur.fetchone())[0]

            assert count == 3
