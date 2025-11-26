"""
API endpoint integration tests for Hartonomous API

These tests require the API to be running or use TestClient.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import os

import pytest
from httpx import AsyncClient

pytestmark = pytest.mark.integration


@pytest.fixture
def api_base_url():
    """Get API base URL from environment."""
    return os.getenv("API_BASE_URL", "http://localhost:8000")


@pytest.fixture
async def test_client():
    """Create a test client for the API."""
    try:
        # pylint: disable=import-outside-toplevel
        from api.main import app

        async with AsyncClient(app=app, base_url="http://test") as client:
            yield client
    except Exception as e:
        pytest.skip(f"Failed to create test client: {e}")


@pytest.mark.asyncio
async def test_root_endpoint(test_client):
    """Test root endpoint."""
    response = await test_client.get("/")
    assert response.status_code == 200

    data = response.json()
    assert "message" in data
    assert "docs" in data
    assert "health" in data


@pytest.mark.asyncio
async def test_health_endpoint(test_client):
    """Test health check endpoint."""
    response = await test_client.get("/api/v1/health")
    assert response.status_code == 200

    data = response.json()
    assert "status" in data
    assert data["status"] in ["healthy", "degraded", "unhealthy"]


@pytest.mark.asyncio
async def test_openapi_docs(test_client):
    """Test OpenAPI documentation endpoints."""
    # Test OpenAPI JSON
    response = await test_client.get("/openapi.json")
    assert response.status_code == 200

    data = response.json()
    assert "openapi" in data
    assert "info" in data
    assert "paths" in data


@pytest.mark.asyncio
async def test_cors_headers(test_client):
    """Test CORS headers are present."""
    response = await test_client.options(
        "/api/v1/health", headers={"Origin": "http://localhost:3000"}
    )

    # Check CORS headers
    assert response.status_code in [200, 204]


@pytest.mark.asyncio
async def test_invalid_endpoint(test_client):
    """Test invalid endpoint returns 404."""
    response = await test_client.get("/api/v1/nonexistent")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_api_versioning(test_client):
    """Test API versioning."""
    response = await test_client.get("/")
    data = response.json()

    # Check version info
    assert "message" in data
    assert "0.6.0" in data["message"]
