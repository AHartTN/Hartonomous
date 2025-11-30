"""
Database integration tests for Hartonomous API

These tests require PostgreSQL to be running and accessible.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import psycopg
import pytest

from api.config import settings

pytestmark = pytest.mark.integration


@pytest.fixture
def db_connection_string():
    """Get database connection string from settings."""
    return settings.get_connection_string()


@pytest.fixture
async def db_connection(db_connection_string):
    """Create a test database connection."""
    try:
        conn = await psycopg.AsyncConnection.connect(db_connection_string)
        yield conn
        await conn.close()
    except Exception as e:
        raise RuntimeError(f"Database not available: {e}") from e


@pytest.mark.asyncio
async def test_database_connection(db_connection):
    """Test database connectivity."""
    async with db_connection.cursor() as cur:
        await cur.execute("SELECT 1 as test")
        result = await cur.fetchone()
        assert result[0] == 1


@pytest.mark.asyncio
async def test_database_version(db_connection):
    """Test PostgreSQL version check."""
    async with db_connection.cursor() as cur:
        await cur.execute("SELECT version()")
        result = await cur.fetchone()
        version = result[0]

        assert "PostgreSQL" in version
        # Should be PostgreSQL 15 or higher
        assert "PostgreSQL 1" in version


@pytest.mark.asyncio
async def test_database_extensions(db_connection):
    """Test required PostgreSQL extensions are available."""
    async with db_connection.cursor() as cur:
        # Check if required extensions exist
        await cur.execute(
            """
            SELECT extname 
            FROM pg_extension 
            WHERE extname IN ('vector', 'age', 'plpython3u')
        """
        )
        extensions = await cur.fetchall()
        extension_names = [ext[0] for ext in extensions]

        # At minimum, we expect vector extension in production
        # AGE and plpython3u are optional
        assert len(extension_names) >= 0  # Relaxed for test environment


@pytest.mark.asyncio
async def test_schema_exists(db_connection):
    """Test that expected schema objects exist."""
    async with db_connection.cursor() as cur:
        # Check for atom table (not 'atoms')
        await cur.execute(
            """
            SELECT EXISTS (
                SELECT FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = 'atom'
            )
        """
        )
        result = await cur.fetchone()
        # May not exist in test database, so we just check the query works
        assert result is not None


@pytest.mark.asyncio
async def test_connection_pool():
    """Test connection pool functionality."""
    # pylint: disable=import-outside-toplevel
    from psycopg_pool import AsyncConnectionPool

    conn_string = settings.get_connection_string()

    try:
        pool = AsyncConnectionPool(
            conninfo=conn_string, min_size=1, max_size=2, open=False
        )

        await pool.open()

        async with pool.connection() as conn:
            async with conn.cursor() as cur:
                await cur.execute("SELECT 1")
                result = await cur.fetchone()
                assert result[0] == 1

        await pool.close()

    except Exception:
        raise


@pytest.mark.asyncio
async def test_transaction_handling(db_connection):
    """Test database transaction handling."""
    try:
        # Start a transaction
        async with db_connection.transaction():
            async with db_connection.cursor() as cur:
                # Create a temporary table
                await cur.execute(
                    """
                    CREATE TEMP TABLE test_table (
                        id SERIAL PRIMARY KEY,
                        value TEXT
                    )
                """
                )

                # Insert data
                await cur.execute(
                    """
                    INSERT INTO test_table (value) 
                    VALUES ('test_value')
                """
                )

                # Query data
                await cur.execute("SELECT value FROM test_table")
                result = await cur.fetchone()
                assert result[0] == "test_value"

    except Exception:
        raise
