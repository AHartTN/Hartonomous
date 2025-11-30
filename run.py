"""Uvicorn startup script for API."""

import sys

if __name__ == "__main__":
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
