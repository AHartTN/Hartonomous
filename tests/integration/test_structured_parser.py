"""Test structured data parser."""
import pytest
import json
from pathlib import Path
from src.ingestion.parsers.structured_parser import StructuredParser

pytestmark = pytest.mark.asyncio

class TestStructuredParser:
    """Test structured data parsing."""
    
    async def test_parse_json(self, db_connection, clean_db, tmp_path):
        """Test parsing JSON file."""
        data = {"name": "test", "value": 123, "nested": {"key": "val"}}
        json_file = tmp_path / "test.json"
        json_file.write_text(json.dumps(data))
        
        parser = StructuredParser()
        parent_atom_id = await parser.parse(json_file, db_connection)
        
        assert parent_atom_id is not None
        
        # Verify metadata
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s",
                (parent_atom_id,)
            )
            metadata = (await cur.fetchone())[0]
            
            assert metadata['modality'] == 'structured'
            assert metadata['format'] == 'json'
    
    async def test_parse_csv(self, db_connection, clean_db, tmp_path):
        """Test parsing CSV file."""
        csv_file = tmp_path / "test.csv"
        csv_file.write_text("name,age\nAlice,30\nBob,25")
        
        parser = StructuredParser()
        parent_atom_id = await parser.parse(csv_file, db_connection)
        
        assert parent_atom_id is not None
        
        # Verify CSV was atomized
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_atom_id,)
            )
            count = (await cur.fetchone())[0]
            
            assert count > 0  # Should have atomized cells
