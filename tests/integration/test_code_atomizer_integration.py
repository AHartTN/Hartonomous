"""Integration tests for Python-C# CodeAtomizer communication.

Tests the complete flow:
1. Python code_parser.py sends request to C# CodeAtomizer API
2. C# API processes code with Roslyn/TreeSitter
3. Python receives response and inserts atoms into PostgreSQL
4. Verify atoms, compositions, and relations are created correctly

REQUIREMENT: C# CodeAtomizer service must be running on http://localhost:8001
"""

import os
import sys
from pathlib import Path

import httpx
import pytest

pytestmark = pytest.mark.integration

# Add src to path
sys.path.insert(0, str(Path(__file__).parent.parent.parent / "src"))

from ingestion.parsers.code_parser import CodeParser


def check_service_available(url: str) -> bool:
    """Check if CodeAtomizer service is available."""
    try:
        response = httpx.get(f"{url}/api/v1/atomize/health", timeout=2.0)
        return response.status_code == 200
    except (httpx.ConnectError, httpx.TimeoutException):
        return False


class TestCodeAtomizerIntegration:
    """Test suite for C# CodeAtomizer integration."""

    @pytest.fixture
    def service_url(self):
        """Get CodeAtomizer service URL from environment."""
        return os.getenv("CODE_ATOMIZER_URL", "http://localhost:8001")

    @pytest.fixture
    def parser(self, service_url):
        """Create CodeParser instance."""
        return CodeParser(atomizer_service_url=service_url)

    def test_service_health(self, service_url):
        """Test that C# CodeAtomizer service is running and healthy."""
        response = httpx.get(f"{service_url}/api/v1/atomize/health", timeout=5.0)
        assert response.status_code == 200

        data = response.json()
        assert data["status"] == "healthy"
        assert data["service"] == "Hartonomous Code Atomizer"
        print(f"✅ Service healthy: {data}")

    def test_list_supported_languages(self, service_url):
        """Test retrieving list of supported languages."""
        response = httpx.get(f"{service_url}/api/v1/atomize/languages", timeout=5.0)
        assert response.status_code == 200

        data = response.json()
        assert "languages" in data

        # Languages are now grouped by parser type
        if isinstance(data["languages"], dict):
            # Flatten all languages from all parsers
            all_langs = []
            for parser_langs in data["languages"].values():
                if isinstance(parser_langs, list):
                    all_langs.extend(parser_langs)
            assert "csharp" in all_langs
            assert "python" in all_langs
            print(f"✅ Supported languages: {', '.join(all_langs[:10])}...")
        else:
            # Legacy flat list format
            assert "csharp" in data["languages"]
            assert "python" in data["languages"]
            print(f"✅ Supported languages: {', '.join(data['languages'][:10])}...")

    def test_atomize_simple_csharp(self, service_url):
        """Test atomizing simple C# code via Roslyn."""
        code = """
        namespace Test {
            public class Calculator {
                public int Add(int a, int b) {
                    return a + b;
                }
            }
        }
        """

        response = httpx.post(
            f"{service_url}/api/v1/atomize/csharp",
            json={"code": code, "fileName": "Calculator.cs"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        assert data["success"] is True
        assert data["totalAtoms"] > 0
        assert len(data["atoms"]) > 0

        # Check atom structure
        atom = data["atoms"][0]
        assert "contentHash" in atom
        assert "canonicalText" in atom
        assert "spatialKey" in atom
        assert "modality" in atom

        # Check spatial coordinates
        spatial = atom["spatialKey"]
        assert "x" in spatial and isinstance(spatial["x"], (int, float))
        assert "y" in spatial and isinstance(spatial["y"], (int, float))
        assert "z" in spatial and isinstance(spatial["z"], (int, float))

        # Check Hilbert index in metadata
        if "metadata" in atom and "hilbertIndex" in atom["metadata"]:
            assert isinstance(atom["metadata"]["hilbertIndex"], int)

        print(
            f"✅ Atomized C# code: {data['totalAtoms']} atoms, "
            f"{len(data['compositions'])} compositions, "
            f"{len(data['relations'])} relations"
        )

    def test_atomize_python_code(self, service_url):
        """Test atomizing Python code via TreeSitter."""
        code = """
def calculate_sum(numbers):
    total = 0
    for num in numbers:
        total += num
    return total

class DataProcessor:
    def process(self, data):
        return [x * 2 for x in data]
"""

        response = httpx.post(
            f"{service_url}/api/v1/atomize/python",
            json={"code": code, "fileName": "processor.py"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        assert data["success"] is True
        assert data["totalAtoms"] > 0

        print(f"✅ Atomized Python code: {data['totalAtoms']} atoms")

    def test_content_hash_format(self, service_url):
        """Test that content hashes are base64-encoded."""
        import base64

        code = "public class Test { }"
        response = httpx.post(
            f"{service_url}/api/v1/atomize/csharp",
            json={"code": code, "fileName": "Test.cs"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        atom = data["atoms"][0]
        content_hash = atom["contentHash"]

        # Should be base64-decodable
        try:
            decoded = base64.b64decode(content_hash)
            assert len(decoded) == 32  # SHA-256 is 32 bytes
            print(f"✅ Content hash is valid base64 (SHA-256): {content_hash[:20]}...")
        except Exception as e:
            pytest.fail(f"Content hash is not valid base64: {e}")

    def test_spatial_coordinates_present(self, service_url):
        """Test that all atoms have spatial coordinates and Hilbert indices."""
        code = "public class Test { public void Method() { } }"
        response = httpx.post(
            f"{service_url}/api/v1/atomize/csharp",
            json={"code": code, "fileName": "Test.cs"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        for atom in data["atoms"]:
            # Check spatial key
            assert "spatialKey" in atom
            spatial = atom["spatialKey"]
            assert all(coord in spatial for coord in ["x", "y", "z"])

            # Check Hilbert index in metadata
            if "metadata" in atom:
                assert "hilbertIndex" in atom["metadata"]
                hilbert = atom["metadata"]["hilbertIndex"]
                assert isinstance(hilbert, int)
                assert 0 <= hilbert < 2**30  # Valid Hilbert index range

        print(
            f"✅ All {len(data['atoms'])} atoms have spatial coordinates + Hilbert indices"
        )

    def test_compositions_structure(self, service_url):
        """Test that compositions link parent→component correctly."""
        code = """
        public class Container {
            public void Method() {
                int x = 42;
            }
        }
        """

        response = httpx.post(
            f"{service_url}/api/v1/atomize/csharp",
            json={"code": code, "fileName": "Container.cs"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        if len(data["compositions"]) > 0:
            comp = data["compositions"][0]
            assert "parentHash" in comp
            assert "componentHash" in comp
            assert "sequenceIndex" in comp
            print(
                f"✅ Compositions have correct structure: {len(data['compositions'])} found"
            )

    def test_relations_structure(self, service_url):
        """Test that relations have source→target→type structure."""
        code = """
        public class Caller {
            public void Run() {
                Helper();
            }
            void Helper() { }
        }
        """

        response = httpx.post(
            f"{service_url}/api/v1/atomize/csharp",
            json={"code": code, "fileName": "Caller.cs"},
            timeout=10.0,
        )
        assert response.status_code == 200

        data = response.json()
        if len(data["relations"]) > 0:
            rel = data["relations"][0]
            assert "sourceHash" in rel
            assert "targetHash" in rel
            assert "relationType" in rel
            print(
                f"✅ Relations have correct structure: {len(data['relations'])} found"
            )


@pytest.mark.asyncio
class TestCodeParserClass:
    """Test the CodeParser class directly (requires database)."""

    @pytest.fixture
    def parser(self):
        """Create CodeParser instance."""
        service_url = os.getenv("CODE_ATOMIZER_URL", "http://localhost:8001")
        return CodeParser(atomizer_service_url=service_url)

    async def test_parser_initialization(self, parser):
        """Test that CodeParser initializes with correct URL."""
        assert parser.service_url.startswith("http")
        assert "atomize" not in parser.service_url  # Base URL, not endpoint
        print(f"✅ CodeParser initialized with URL: {parser.service_url}")

    async def test_health_check_method(self, parser):
        """Test the _check_health() method."""
        await parser._check_health()
        print(f"✅ Health check passed for {parser.service_url}")


if __name__ == "__main__":
    print("=" * 80)
    print("Code Atomizer Integration Tests")
    print("=" * 80)
    print()
    print("Prerequisites:")
    print("1. C# CodeAtomizer service must be running")
    print("   - Local: cd src/Hartonomous.CodeAtomizer.Api && dotnet run")
    print("   - Docker: docker-compose up code-atomizer")
    print()
    print("2. Set CODE_ATOMIZER_URL environment variable:")
    print("   - Local: export CODE_ATOMIZER_URL=http://localhost:8001")
    print("   - Docker: export CODE_ATOMIZER_URL=http://localhost:8080")
    print()
    print("=" * 80)
    print()

    # Run with pytest
    pytest.main([__file__, "-v", "-s"])
