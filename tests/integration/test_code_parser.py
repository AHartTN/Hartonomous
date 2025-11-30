"""Test Code parser."""

import os
from pathlib import Path

import httpx
import pytest

from src.ingestion.parsers.code_parser import CodeParser

pytestmark = pytest.mark.integration


def check_service_available(url: str) -> bool:
    """Check if CodeAtomizer service is available."""
    try:
        response = httpx.get(f"{url}/api/v1/atomize/health", timeout=2.0)
        return response.status_code == 200
    except (httpx.ConnectError, httpx.TimeoutException):
        return False


class TestCodeParser:
    """Test code parsing."""

    def test_detect_language(self):
        """Test language detection from extension."""
        parser = CodeParser()

        assert parser._detect_language(".py") == "python"
        assert parser._detect_language(".cs") == "csharp"
        assert parser._detect_language(".js") == "javascript"
        assert parser._detect_language(".ts") == "typescript"
        assert parser._detect_language(".java") == "java"
        assert parser._detect_language(".cpp") == "cpp"
        assert parser._detect_language(".rs") == "rust"
        assert parser._detect_language(".go") == "go"

    @pytest.mark.asyncio
    async def test_parse_python_file(self, db_connection, clean_db, tmp_path):
        """Test parsing Python file (fallback to character-level)."""
        code_file = tmp_path / "test.py"
        code_file.write_text("print('hello')")

        parser = CodeParser()
        parent_atom_id = await parser.parse(code_file, db_connection)

        assert parent_atom_id is not None

        # Verify metadata
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s", (parent_atom_id,)
            )
            metadata = (await cur.fetchone())[0]

            assert metadata["modality"] == "code"
            assert metadata["language"] == "python"
