#!/usr/bin/env python3
"""Smoke test for database connectivity.

Tests basic database connection and query functionality.
"""
import asyncio
import asyncpg
import pytest
from api.config import settings


@pytest.mark.smoke
@pytest.mark.asyncio
async def test_database_connection():
    """Test database connectivity using settings from .env."""
    try:
        conn = await asyncpg.connect(
            host=settings.pghost,
            port=settings.pgport,
            database=settings.pgdatabase,
            user=settings.pguser,
            password=settings.pgpassword
        )
        
        version = await conn.fetchval("SELECT version()")
        print(f"✅ Connected as {settings.pguser}@{settings.pghost}: {version[:60]}...")
        
        atom_count = await conn.fetchval("SELECT COUNT(*) FROM atom")
        print(f"✅ Atoms in database: {atom_count}")
        
        await conn.close()
        assert True
    except Exception as e:
        raise RuntimeError(f"Database not available: {e}") from e


if __name__ == "__main__":
    asyncio.run(test_database_connection())
