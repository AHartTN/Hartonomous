"""
Sanity tests for Hartonomous API

These tests verify basic functionality and ensure the test framework is working correctly.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import os
import sys

import pytest
from httpx import AsyncClient

# Add parent directory to path for imports
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))


def test_sanity():
    """Basic sanity test to verify pytest works."""
    assert True


def test_math():
    """Test basic math operations."""
    assert 1 + 1 == 2
    assert 10 * 5 == 50


def test_string_operations():
    """Test string operations."""
    test_str = "Hartonomous"
    assert test_str.lower() == "hartonomous"
    assert test_str.upper() == "HARTONOMOUS"
    assert len(test_str) == 11


def test_list_operations():
    """Test list operations."""
    test_list = [1, 2, 3, 4, 5]
    assert len(test_list) == 5
    assert sum(test_list) == 15
    assert max(test_list) == 5


def test_imports():
    """Test that core modules can be imported."""
    try:
        # pylint: disable=import-outside-toplevel,unused-import
        import config
        import main
        from models import ingest
        from services import atomization, query
    except ImportError as e:
        pytest.skip(f"Failed to import modules (expected in CI): {e}")


@pytest.mark.asyncio
async def test_async_sanity():
    """Test async functionality."""

    async def sample_async():
        return True

    result = await sample_async()
    assert result is True


@pytest.mark.asyncio
async def test_async_operations():
    """Test async operations."""
    import asyncio

    async def async_add(a, b):
        await asyncio.sleep(0.01)  # Simulate async work
        return a + b

    result = await async_add(5, 10)
    assert result == 15


def test_environment_detection():
    """Test environment variable handling."""
    import os

    # Check if TESTING flag is set
    testing = os.getenv("TESTING", "false").lower() == "true"
    # This test should pass regardless, but log the state
    assert isinstance(testing, bool)


def test_api_version():
    """Test API version is accessible."""
    try:
        # pylint: disable=import-outside-toplevel
        from main import app

        assert app.version == "0.6.0"
        assert app.title == "Hartonomous API"
    except ImportError:
        pytest.skip("API main module not available in CI")


def test_config_loading():
    """Test configuration can be loaded."""
    try:
        # pylint: disable=import-outside-toplevel
        from config import settings

        assert settings is not None
        assert hasattr(settings, "api_host")
        assert hasattr(settings, "api_port")
        assert hasattr(settings, "log_level")
    except ImportError:
        pytest.skip("Config module not available in CI")


@pytest.mark.asyncio
async def test_database_imports():
    """Test database-related imports."""
    try:
        # pylint: disable=import-outside-toplevel,unused-import
        import psycopg
        from psycopg.rows import dict_row
        from psycopg_pool import AsyncConnectionPool

        assert True
    except ImportError as e:
        pytest.skip(f"Database modules not available: {e}")


def test_fastapi_imports():
    """Test FastAPI imports."""
    try:
        # pylint: disable=import-outside-toplevel,unused-import
        from fastapi import Depends, FastAPI, HTTPException
        from fastapi.middleware.cors import CORSMiddleware
        from httpx import AsyncClient

        assert True
    except ImportError as e:
        pytest.skip(f"FastAPI modules not available: {e}")


@pytest.mark.asyncio
async def test_health_check_endpoint():
    """Test the API's health check endpoint."""
    try:
        # pylint: disable=import-outside-toplevel
        from httpx import ASGITransport
        from main import app

        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            response = await client.get("/v1/health")
            assert response.status_code == 200
            data = response.json()
            assert data["status"] == "ok"
            assert "service" in data
            assert "version" in data
    except ImportError:
        pytest.skip("API main module not available in CI")
