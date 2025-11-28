"""Pytest configuration and fixtures."""
import pytest
import asyncio
import os
from pathlib import Path
from psycopg import AsyncConnection
from dotenv import load_dotenv

# Load environment
load_dotenv()

@pytest.fixture(scope="session")
def event_loop():
    """Create event loop for async tests."""
    loop = asyncio.get_event_loop_policy().new_event_loop()
    yield loop
    loop.close()

@pytest.fixture(scope="session")
async def db_connection():
    """Create database connection for tests."""
    conn_string = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/hartonomous_test")
    conn = await AsyncConnection.connect(conn_string)
    yield conn
    await conn.close()

@pytest.fixture
async def clean_db(db_connection):
    """Clean test database before each test."""
    async with db_connection.cursor() as cur:
        await cur.execute("TRUNCATE atom, atom_composition, atom_relation CASCADE")
    await db_connection.commit()
    yield
    
@pytest.fixture
def test_data_dir():
    """Test data directory."""
    return Path(__file__).parent / "data"
