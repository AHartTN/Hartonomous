"""
Configuration tests for Hartonomous API

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""
import pytest


class TestConfiguration:
    """Test configuration loading and validation."""

    def test_settings_defaults(self):
        """Test default settings are loaded."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert settings.api_port >= 1000
        assert settings.api_port <= 65535
        assert settings.log_level in ["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"]

    def test_database_config(self):
        """Test database configuration."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert hasattr(settings, "pghost")
        assert hasattr(settings, "pgport")
        assert hasattr(settings, "pgdatabase")
        assert hasattr(settings, "pguser")

    def test_connection_pool_config(self):
        """Test connection pool configuration."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert settings.pool_min_size > 0
        assert settings.pool_max_size >= settings.pool_min_size
        assert settings.pool_timeout > 0

    def test_feature_flags(self):
        """Test feature flag configuration."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert isinstance(settings.neo4j_enabled, bool)
        assert isinstance(settings.age_worker_enabled, bool)

    def test_cors_configuration(self):
        """Test CORS configuration."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert isinstance(settings.cors_origins, list)

    def test_api_prefix(self):
        """Test API prefix configuration."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert settings.api_v1_prefix.startswith("/")
        assert not settings.api_v1_prefix.endswith("/")


class TestConfigValidation:
    """Test configuration validation logic."""

    def test_port_ranges(self):
        """Test port number validation."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        # API port
        assert 1024 <= settings.api_port <= 65535

        # Database port
        assert 1 <= settings.pgport <= 65535

    def test_pool_size_constraints(self):
        """Test connection pool size constraints."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert settings.pool_min_size >= 1
        assert settings.pool_max_size <= 100
        assert settings.pool_max_size >= settings.pool_min_size

    def test_timeout_values(self):
        """Test timeout values are positive."""
        # pylint: disable=import-outside-toplevel
        from api.config import settings

        assert settings.pool_timeout > 0
        assert settings.pool_max_idle > 0


@pytest.mark.asyncio
async def test_connection_string_generation():
    """Test database connection string generation."""
    # pylint: disable=import-outside-toplevel
    from api.config import settings

    conn_string = settings.get_connection_string()

    assert "host=" in conn_string or "postgresql://" in conn_string
    assert isinstance(conn_string, str)
    assert len(conn_string) > 0
