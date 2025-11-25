"""
Dependency injection for FastAPI routes.

Provides reusable dependencies for database connections, authentication, etc.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

from typing import AsyncGenerator
from fastapi import Depends, HTTPException, status
from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from api.config import settings


# Global connection pool (initialized in main.py lifespan)
_connection_pool: AsyncConnectionPool | None = None


def set_connection_pool(pool: AsyncConnectionPool) -> None:
    """Set the global connection pool (called from lifespan)."""
    global _connection_pool
    _connection_pool = pool


def get_connection_pool() -> AsyncConnectionPool:
    """Get the global connection pool."""
    if _connection_pool is None:
        raise RuntimeError("Connection pool not initialized")
    return _connection_pool


async def get_db_connection() -> AsyncGenerator[AsyncConnection, None]:
    """
    Dependency: Get database connection from pool.
    
    Usage:
        @app.get("/atoms")
        async def list_atoms(conn = Depends(get_db_connection)):
            async with conn.cursor() as cur:
                await cur.execute("SELECT * FROM atom LIMIT 10")
                return await cur.fetchall()
    
    Yields:
        AsyncConnection: Database connection from pool
    
    Raises:
        HTTPException: If connection pool unavailable
    """
    pool = get_connection_pool()
    
    try:
        async with pool.connection() as conn:
            yield conn
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=f"Database connection failed: {str(e)}"
        )


async def get_db_cursor():
    """
    Dependency: Get database cursor.
    
    Usage:
        @app.get("/atoms")
        async def list_atoms(cur = Depends(get_db_cursor)):
            await cur.execute("SELECT * FROM atom LIMIT 10")
            return await cur.fetchall()
    
    Yields:
        Cursor: Database cursor
    """
    async with get_connection_pool().connection() as conn:
        async with conn.cursor() as cur:
            yield cur


# Future: Authentication dependencies
async def get_current_user():
    """
    Dependency: Get current authenticated user.
    
    Uses Entra ID (internal) or B2C (external) authentication.
    
    Raises:
        HTTPException: If authentication required but not provided
    """
    if settings.auth_enabled:
        # Import here to avoid circular dependency
        from api.auth import get_current_user as auth_get_current_user
        return await auth_get_current_user()
    
    # For now, return None (no auth)
    return None


async def require_auth(user = Depends(get_current_user)):
    """
    Dependency: Require authentication.
    
    Usage:
        @app.post("/admin/reset", dependencies=[Depends(require_auth)])
        async def reset_database():
            ...
    
    Raises:
        HTTPException: If user not authenticated
    """
    if settings.auth_enabled and user is None:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authentication required"
        )
    return user


__all__ = [
    "get_db_connection",
    "get_db_cursor",
    "get_current_user",
    "require_auth",
    "set_connection_pool",
    "get_connection_pool",
]
