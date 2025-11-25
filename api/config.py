"""
Configuration management using Pydantic Settings.

Environment variables:
- DATABASE_URL: PostgreSQL connection string
- PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE: Individual components
- API_HOST: API server host (default: 0.0.0.0)
- API_PORT: API server port (default: 8000)
- LOG_LEVEL: Logging level (default: INFO)

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

import os
from typing import Optional
from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Application settings with environment variable support."""
    
    # Database Configuration
    database_url: Optional[str] = Field(
        default=None,
        description="Full PostgreSQL connection string"
    )
    
    # Individual PostgreSQL components (fallback if DATABASE_URL not set)
    pghost: str = Field(default="localhost", description="PostgreSQL host")
    pgport: int = Field(default=5432, description="PostgreSQL port")
    pguser: str = Field(default="postgres", description="PostgreSQL user")
    pgpassword: str = Field(default="postgres", description="PostgreSQL password")
    pgdatabase: str = Field(default="hartonomous", description="PostgreSQL database")
    pgsslmode: str = Field(default="prefer", description="SSL mode")
    
    # Connection Pool Settings
    pool_min_size: int = Field(default=5, description="Min connection pool size")
    pool_max_size: int = Field(default=20, description="Max connection pool size")
    pool_timeout: int = Field(default=30, description="Pool timeout (seconds)")
    pool_max_idle: int = Field(default=600, description="Max idle time (seconds)")
    
    # API Server Settings
    api_host: str = Field(default="0.0.0.0", description="API host")
    api_port: int = Field(default=8000, description="API port")
    api_reload: bool = Field(default=False, description="Enable auto-reload")
    
    # API Versioning
    api_v1_prefix: str = Field(default="/v1", description="API v1 prefix")
    
    # CORS Settings
    cors_origins: list[str] = Field(
        default=["http://localhost:3000", "http://localhost:8000"],
        description="Allowed CORS origins"
    )
    
    # Logging
    log_level: str = Field(default="INFO", description="Logging level")
    log_json: bool = Field(default=False, description="Use JSON logging")
    
    # Rate Limiting
    rate_limit_enabled: bool = Field(default=True, description="Enable rate limiting")
    rate_limit_per_minute: int = Field(default=100, description="Requests per minute")
    
    # Authentication (future)
    auth_enabled: bool = Field(default=False, description="Enable authentication")
    auth_secret_key: Optional[str] = Field(default=None, description="JWT secret key")
    
    # AGE Worker Settings
    age_worker_enabled: bool = Field(default=True, description="Enable AGE sync worker")
    age_worker_poll_interval: int = Field(
        default=5,
        description="Worker poll interval (seconds)"
    )
    
    # Model configuration
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore"
    )
    
    @field_validator("database_url", mode="before")
    @classmethod
    def build_database_url(cls, v: Optional[str], info) -> str:
        """Build DATABASE_URL from components if not provided."""
        if v:
            return v
        
        # Get values from context (other fields being validated)
        values = info.data
        host = values.get("pghost", "localhost")
        port = values.get("pgport", 5432)
        user = values.get("pguser", "postgres")
        password = values.get("pgpassword", "postgres")
        database = values.get("pgdatabase", "hartonomous")
        sslmode = values.get("pgsslmode", "prefer")
        
        return (
            f"postgresql://{user}:{password}@{host}:{port}/{database}"
            f"?sslmode={sslmode}"
        )
    
    def get_connection_string(self) -> str:
        """Get PostgreSQL connection string."""
        return self.database_url


# Global settings instance
settings = Settings()


# Export for convenience
__all__ = ["settings", "Settings"]
