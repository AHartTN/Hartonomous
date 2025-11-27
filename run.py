"""
Uvicorn startup with proper event loop configuration for psycopg3 async

This script ensures the correct event loop is set before any async operations begin.
"""
import sys

if __name__ == "__main__":
    # Set event loop policy BEFORE importing anything that uses asyncio
    if sys.platform == "win32":
        import asyncio
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    
    import uvicorn
    from api.config import settings
    
    uvicorn.run(
        "api.main:app",
        host=settings.api_host,
        port=settings.api_port,
        reload=settings.api_reload,
        log_level=settings.log_level.lower(),
        loop="asyncio",
    )
