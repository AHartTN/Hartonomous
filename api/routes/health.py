"""
Health check routes for monitoring and readiness probes.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from fastapi import APIRouter, Depends, HTTPException, status
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.config import settings

router = APIRouter()


@router.get("/health")
async def health_check():
    """
    Basic health check.
    
    Returns OK if service is running.
    Used by load balancers for basic availability.
    
    Returns:
        dict: Status message
    """
    return {
        "status": "ok",
        "service": "hartonomous-api",
        "version": "0.6.0"
    }


@router.get("/ready")
async def readiness_check(conn: AsyncConnection = Depends(get_db_connection)):
    """
    Readiness check with database connectivity.
    
    Returns OK only if:
    - Service is running
    - Database connection successful
    - Core tables exist
    
    Used by Kubernetes readiness probes.
    
    Returns:
        dict: Status and database info
    
    Raises:
        HTTPException: If database check fails
    """
    try:
        # Test database connection
        async with conn.cursor() as cur:
            await cur.execute("SELECT version();")
            pg_version = await cur.fetchone()
            
            # Check if core tables exist
            await cur.execute("""
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name IN ('atom', 'atom_composition', 'atom_relation');
            """)
            table_count = await cur.fetchone()
            
            # Check extensions (only PostGIS is required)
            await cur.execute("""
                SELECT COUNT(*) FROM pg_extension
                WHERE extname IN ('postgis');
            """)
            ext_count = await cur.fetchone()
            
            if table_count[0] != 3:
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail=f"Core tables missing (found {table_count[0]}/3)"
                )
            
            if ext_count[0] != 1:
                raise HTTPException(
                    status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                    detail=f"Required extensions missing (found {ext_count[0]}/1: PostGIS required)"
                )
            
            return {
                "status": "ready",
                "service": "hartonomous-api",
                "version": "0.6.0",
                "database": {
                    "connected": True,
                    "version": pg_version[0],
                    "tables": table_count[0],
                    "extensions": ext_count[0]
                }
            }
    
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=f"Database check failed: {str(e)}"
        )


@router.get("/stats")
async def statistics(conn: AsyncConnection = Depends(get_db_connection)):
    """
    Database statistics.
    
    Returns:
        dict: Atom counts, relation counts, etc.
    """
    try:
        async with conn.cursor() as cur:
            # Atom count
            await cur.execute("SELECT COUNT(*) FROM atom;")
            atom_count = await cur.fetchone()
            
            # Composition count
            await cur.execute("SELECT COUNT(*) FROM atom_composition;")
            composition_count = await cur.fetchone()
            
            # Relation count
            await cur.execute("SELECT COUNT(*) FROM atom_relation;")
            relation_count = await cur.fetchone()
            
            # Database size
            await cur.execute("""
                SELECT pg_size_pretty(pg_database_size(current_database()));
            """)
            db_size = await cur.fetchone()
            
            return {
                "status": "ok",
                "statistics": {
                    "atoms": atom_count[0],
                    "compositions": composition_count[0],
                    "relations": relation_count[0],
                    "database_size": db_size[0]
                }
            }
    
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to fetch statistics: {str(e)}"
        )


__all__ = ["router"]
