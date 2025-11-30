"""Pytest configuration and fixtures."""

import asyncio
import sys
from pathlib import Path

import pytest
from dotenv import load_dotenv
from psycopg import AsyncConnection

# Load environment
load_dotenv()


def pytest_configure(config):
    """Configure pytest - set event loop policy for Windows before any async tests."""
    if sys.platform == "win32":
        # On Windows, psycopg requires SelectorEventLoop instead of ProactorEventLoop
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())


@pytest.fixture(scope="session")
def event_loop():
    """Create event loop for async tests."""
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    yield loop
    loop.close()


@pytest.fixture(scope="session")
async def db_connection():
    """Create database connection for tests using settings from .env.

    Ensures database schema is initialized before running tests.
    """
    try:
        from api.config import settings

        conn_string = settings.get_connection_string()
        conn = await AsyncConnection.connect(conn_string)

        # Verify schema is initialized (check for atom table)
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'atom'
                )
            """
            )
            exists = await cur.fetchone()
            if not exists or not exists[0]:
                raise RuntimeError(
                    "Database schema not initialized. Run: .\\scripts\\Initialize-Database.ps1"
                )

        yield conn
        await conn.close()
    except Exception as e:
        raise RuntimeError(f"Database not available: {e}") from e


@pytest.fixture
async def clean_db(db_connection):
    """Clean test database before each test."""
    try:
        await db_connection.rollback()  # Clear any existing transaction state
        async with db_connection.cursor() as cur:
            # Note: atom_composition is deprecated, compositions now stored in atom.composition_ids
            await cur.execute("TRUNCATE atom, atom_relation CASCADE")
        await db_connection.commit()
    except Exception:
        await db_connection.rollback()
        raise
    yield


@pytest.fixture
def test_data_dir():
    """Test data directory."""
    return Path(__file__).parent / "data"


@pytest.fixture(scope="session")
def project_root():
    """Return the project root directory."""
    return Path(__file__).parent.parent


@pytest.fixture(scope="session")
def test_models_dir(project_root):
    """Return the cached test models directory."""
    return project_root / ".cache" / "test_models"


@pytest.fixture(scope="session")
def test_gguf_path(test_models_dir):
    """Return path to the small GGUF test model (TinyLlama ~637MB)."""
    return test_models_dir / "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"


@pytest.fixture(scope="session")
def test_safetensors_dir(project_root):
    """Return path to the cached SafeTensors embedding model."""
    snapshot_dir = (
        project_root
        / ".cache"
        / "embedding_models"
        / "all-MiniLM-L6-v2"
        / "snapshots"
        / "c9745ed1d9f207416be6d2e6f8de32d1f16199bf"
    )
    return snapshot_dir


@pytest.fixture(scope="session")
def test_safetensors_path(test_safetensors_dir):
    """Return path to the SafeTensors model file (~87MB)."""
    return test_safetensors_dir / "model.safetensors"
