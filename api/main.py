"""
Hartonomous REST API

FastAPI application with PostgreSQL connection pooling and background workers.

Copyright © 2025 Anthony Hart. All Rights Reserved.
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
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
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
    logger.info(f"Creating connection pool (min={settings.pool_min_size}, max={settings.pool_max_size})...")
    pool = AsyncConnectionPool(
        conninfo=settings.get_connection_string(),
        min_size=settings.pool_min_size,
        max_size=settings.pool_max_size,
        timeout=settings.pool_timeout,
        max_idle=settings.pool_max_idle,
        open=False  # Don't open yet
    )
    
    # Open pool
    await pool.open()
    logger.info("Connection pool opened successfully")
    
    # Set global pool for dependencies
    set_connection_pool(pool)
    
    # Start AGE worker (if enabled)
    age_worker_task = None
    if settings.age_worker_enabled:
        logger.info("Starting AGE sync worker...")
        from api.workers.age_sync import AGESyncWorker
        import asyncio
        
        age_worker = AGESyncWorker(pool)
        age_worker_task = asyncio.create_task(age_worker.start())
        logger.info("AGE sync worker started")
    
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
        "In-Database AI • Zero Latency • Provenance Tracking • 100x Performance"
    ),
    version="0.6.0",
    lifespan=lifespan,
    docs_url="/docs",
    redoc_url="/redoc",
    openapi_url="/openapi.json"
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
app.include_router(
    health.router,
    prefix=settings.api_v1_prefix,
    tags=["health"]
)

# Ingest routes
from api.routes import ingest
app.include_router(
    ingest.router,
    prefix=f"{settings.api_v1_prefix}/ingest",
    tags=["ingest"]
)

# Future routers (will be added as we build them)
# from api.routes import query, train, export
# app.include_router(query.router, prefix=f"{settings.api_v1_prefix}/query", tags=["query"])
# app.include_router(train.router, prefix=f"{settings.api_v1_prefix}/train", tags=["train"])
# app.include_router(export.router, prefix=f"{settings.api_v1_prefix}/export", tags=["export"])


@app.get("/")
async def root():
    """Root endpoint - redirect to docs."""
    return {
        "message": "Hartonomous API v0.6.0",
        "docs": "/docs",
        "health": f"{settings.api_v1_prefix}/health",
        "github": "https://github.com/AHartTN/Hartonomous"
    }


if __name__ == "__main__":
    import uvicorn
    
    uvicorn.run(
        "api.main:app",
        host=settings.api_host,
        port=settings.api_port,
        reload=settings.api_reload,
        log_level=settings.log_level.lower()
    )
