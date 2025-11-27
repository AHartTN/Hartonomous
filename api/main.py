# -*- coding: utf-8 -*-
"""
Hartonomous REST API

FastAPI application with PostgreSQL connection pooling and background workers.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from psycopg_pool import AsyncConnectionPool

from api.config import settings
from api.dependencies import set_connection_pool
from api.routes import health

# Configure logging
logging.basicConfig(
    level=settings.log_level,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Lifespan context manager for startup/shutdown.

    Startup:
    - Create AsyncConnectionPool
    - Start AGE sync worker (if enabled)

    Shutdown:
    - Stop AGE worker
    - Close connection pool gracefully
    """
    logger.info("Starting Hartonomous API v0.6.0...")

    # Create connection pool
    logger.info(
        f"Creating connection pool (min={settings.pool_min_size}, max={settings.pool_max_size})..."
    )
    pool = AsyncConnectionPool(
        conninfo=settings.get_connection_string(),
        min_size=settings.pool_min_size,
        max_size=settings.pool_max_size,
        timeout=settings.pool_timeout,
        max_idle=settings.pool_max_idle,
        open=False,  # Don't open yet
    )

    # Open pool
    await pool.open()
    logger.info("Connection pool opened successfully")

    # Set global pool for dependencies
    set_connection_pool(pool)

    # Start background workers
    age_worker_task = None
    neo4j_worker_task = None

    # Start Neo4j provenance worker (RECOMMENDED for production)
    if settings.neo4j_enabled:
        logger.info("Starting Neo4j provenance worker...")
        import asyncio

        from api.workers.neo4j_sync import Neo4jProvenanceWorker

        neo4j_worker = Neo4jProvenanceWorker(pool)
        neo4j_worker_task = asyncio.create_task(neo4j_worker.start())
        logger.info("Neo4j provenance worker started (production-ready)")

    # Start AGE worker (EXPERIMENTAL - disabled by default)
    if settings.age_worker_enabled:
        logger.warning(
            "Starting EXPERIMENTAL AGE sync worker (not recommended for production)"
        )
        import asyncio

        from api.workers.age_sync import AGESyncWorker

        age_worker = AGESyncWorker(pool)
        age_worker_task = asyncio.create_task(age_worker.start())
        logger.info("AGE sync worker started (experimental only)")

    logger.info("? Hartonomous API ready")

    # Application runs here
    yield

    # Shutdown
    logger.info("Shutting down Hartonomous API...")

    # Stop AGE worker
    if age_worker_task:
        logger.info("Stopping AGE sync worker...")
        age_worker_task.cancel()
        try:
            await age_worker_task
        except asyncio.CancelledError:
            pass
        logger.info("AGE sync worker stopped")

    # Stop Neo4j worker
    if neo4j_worker_task:
        logger.info("Stopping Neo4j provenance worker...")
        neo4j_worker_task.cancel()
        try:
            await neo4j_worker_task
        except asyncio.CancelledError:
            pass
        logger.info("Neo4j provenance worker stopped")

    # Close connection pool
    logger.info("Closing connection pool...")
    await pool.close()
    logger.info("Connection pool closed")

    logger.info("? Hartonomous API shutdown complete")


# Create FastAPI app
app = FastAPI(
    title="Hartonomous API",
    description=(
        "REST API for Hartonomous - The First Self-Organizing Intelligence Substrate\n\n"
        "In-Database AI � Zero Latency � Provenance Tracking � 100x Performance"
    ),
    version="0.6.0",
    lifespan=lifespan,
    docs_url="/docs",
    redoc_url="/redoc",
    openapi_url="/openapi.json",
)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
app.include_router(health.router, prefix=settings.api_v1_prefix, tags=["health"])

# Ingest routes
from api.routes import ingest

app.include_router(
    ingest.router, prefix=f"{settings.api_v1_prefix}/ingest", tags=["ingest"]
)

# Query routes
from api.routes import query

app.include_router(
    query.router, prefix=f"{settings.api_v1_prefix}/query", tags=["query"]
)

# Train routes
from api.routes import train

app.include_router(
    train.router, prefix=f"{settings.api_v1_prefix}/train", tags=["train"]
)

# Export routes
from api.routes import export

app.include_router(
    export.router, prefix=f"{settings.api_v1_prefix}/export", tags=["export"]
)

# Code routes (Roslyn/Tree-sitter microservice)
from api.routes import code

app.include_router(
    code.router, prefix=f"{settings.api_v1_prefix}/ingest", tags=["code"]
)

# GitHub routes (repository ingestion)
from api.routes import github

app.include_router(
    github.router, prefix=f"{settings.api_v1_prefix}/ingest", tags=["github"]
)

# Model routes (GGUF, SafeTensors, PyTorch, ONNX)
from api.routes import models

app.include_router(
    models.router, prefix=f"{settings.api_v1_prefix}/ingest", tags=["models"]
)


@app.get("/")
async def root():
    """Root endpoint - redirect to docs."""
    return {
        "message": "Hartonomous API v0.6.0",
        "docs": "/docs",
        "health": f"{settings.api_v1_prefix}/health",
        "github": "https://github.com/AHartTN/Hartonomous",
    }


if __name__ == "__main__":
    import asyncio
    import sys

    import uvicorn

    # Windows-specific: Use SelectorEventLoop for psycopg async compatibility
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    uvicorn.run(
        "api.main:app",
        host=settings.api_host,
        port=settings.api_port,
        reload=settings.api_reload,
        log_level=settings.log_level.lower(),
    )
