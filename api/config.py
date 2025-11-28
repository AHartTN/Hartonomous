"""
Configuration management using Pydantic Settings.

Supports both local .env files and Azure Key Vault + App Configuration.

Environment variables:
- DATABASE_URL: PostgreSQL connection string
- PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE: Individual components
- API_HOST: API server host (default: 0.0.0.0)
- API_PORT: API server port (default: 8000)
- LOG_LEVEL: Logging level (default: INFO)

Azure (production):
- KEY_VAULT_URL: Azure Key Vault URL
- APP_CONFIG_ENDPOINT: Azure App Configuration endpoint
- AZURE_CLIENT_ID: Managed Identity client ID (optional)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
import os
from typing import Optional

from pydantic import Field, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

logger = logging.getLogger(__name__)


class Settings(BaseSettings):
    """Application settings with environment variable support."""

    # Azure Configuration (production)
    use_azure_config: bool = Field(
        default=False, description="Use Azure Key Vault + App Configuration"
    )
    key_vault_url: Optional[str] = Field(
        default=None, description="Azure Key Vault URL"
    )
    app_config_endpoint: Optional[str] = Field(
        default=None, description="Azure App Configuration endpoint"
    )
    azure_client_id: Optional[str] = Field(
        default=None, description="Managed Identity client ID"
    )

    # Database Configuration
    database_url: Optional[str] = Field(
        default=None, description="Full PostgreSQL connection string"
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
    api_host: str = Field(
        default="127.0.0.1",  # nosec B104 - Bind to localhost by default for security
        description="API host",
    )
    api_port: int = Field(default=8000, description="API port")
    api_reload: bool = Field(default=False, description="Enable auto-reload")

    # API Versioning
    api_v1_prefix: str = Field(default="/v1", description="API v1 prefix")

    # CORS Settings
    cors_origins: str = Field(
        default="http://localhost:3000,http://localhost:8000",
        description="Allowed CORS origins (comma-separated)",
    )

    @field_validator("cors_origins", mode="after")
    @classmethod
    def parse_cors_origins(cls, v):
        """Parse CORS origins from comma-separated string to list."""
        if isinstance(v, str):
            return [origin.strip() for origin in v.split(",") if origin.strip()]
        if isinstance(v, list):
            return v
        return [str(v)]

    # Logging
    log_level: str = Field(default="INFO", description="Logging level")
    log_json: bool = Field(default=False, description="Use JSON logging")

    # Rate Limiting
    rate_limit_enabled: bool = Field(default=True, description="Enable rate limiting")
    rate_limit_per_minute: int = Field(default=100, description="Requests per minute")

    # Authentication (Entra ID)
    auth_enabled: bool = Field(default=False, description="Enable authentication")
    entra_tenant_id: Optional[str] = Field(
        default=None, description="Entra ID tenant ID"
    )
    entra_client_id: Optional[str] = Field(
        default=None, description="Entra ID client ID"
    )
    entra_client_secret: Optional[str] = Field(
        default=None, description="Entra ID client secret"
    )

    # B2C (CIAM for external users)
    b2c_enabled: bool = Field(default=False, description="Enable Azure AD B2C")
    b2c_tenant_name: Optional[str] = Field(default=None, description="B2C tenant name")
    b2c_client_id: Optional[str] = Field(default=None, description="B2C client ID")
    b2c_policy_name: Optional[str] = Field(default=None, description="B2C policy name")

    # Neo4j Settings (Production-ready provenance graph)
    neo4j_enabled: bool = Field(
        default=True, description="Enable Neo4j provenance sync"
    )
    neo4j_uri: str = Field(default="bolt://localhost:7687", description="Neo4j URI")
    neo4j_user: str = Field(default="neo4j", description="Neo4j username")
    neo4j_password: str = Field(default="neo4jneo4j", description="Neo4j password")
    neo4j_database: str = Field(default="neo4j", description="Neo4j database name")

    # Code Atomizer Microservice
    code_atomizer_url: str = Field(
        default="http://localhost:8001",
        description="URL for the Code Atomizer microservice",
    )

    # Model configuration
    model_config = SettingsConfigDict(
        env_file=".env", env_file_encoding="utf-8", case_sensitive=False, extra="ignore"
    )

    @model_validator(mode="after")
    def load_from_azure(self):
        """Load secrets from Azure Key Vault and settings from App Configuration."""
        if self.use_azure_config:
            logger.info("Loading configuration from Azure...")

            try:
                from api.azure_config import (get_app_config_client,
                                              get_key_vault_client)

                kv_client = get_key_vault_client()
                app_config_client = get_app_config_client()

                # Load from App Configuration
                if app_config_client:
                    try:
                        # API settings
                        self.api_host = (
                            app_config_client.get_setting("Hartonomous:Api:Host")
                            or self.api_host
                        )
                        self.api_port = int(
                            app_config_client.get_setting("Hartonomous:Api:Port")
                            or self.api_port
                        )
                        self.log_level = (
                            app_config_client.get_setting("Hartonomous:Api:LogLevel")
                            or self.log_level
                        )

                        # CORS origins (comma-separated)
                        cors_origins_str = app_config_client.get_setting(
                            "Hartonomous:Api:CorsOrigins"
                        )
                        if cors_origins_str:
                            self.cors_origins = [
                                origin.strip() for origin in cors_origins_str.split(",")
                            ]

                        # Auth settings
                        auth_enabled_str = app_config_client.get_setting(
                            "Hartonomous:Api:AuthEnabled"
                        )
                        if auth_enabled_str:
                            self.auth_enabled = auth_enabled_str.lower() == "true"

                        # Pool settings
                        pool_min = app_config_client.get_setting(
                            "Hartonomous:Api:PoolMinSize"
                        )
                        if pool_min:
                            self.pool_min_size = int(pool_min)

                        pool_max = app_config_client.get_setting(
                            "Hartonomous:Database:PoolMaxSize"
                        )
                        if pool_max:
                            self.pool_max_size = int(pool_max)

                        # AGE worker removed - Neo4j is the chosen graph database

                        # PostgreSQL connection string (choose based on environment)
                        # For now, use HART-DESKTOP (current machine)
                        import socket

                        hostname = socket.gethostname().upper()

                        if hostname == "HART-DESKTOP":
                            pg_host_str = app_config_client.get_setting(
                                "ConnectionStrings:PostgreSQL-HART-DESKTOP"
                            )
                        elif hostname.lower() == "hart-server":
                            pg_host_str = app_config_client.get_setting(
                                "ConnectionStrings:PostgreSQL-hart-server"
                            )
                        else:
                            # Fallback to HART-DESKTOP
                            pg_host_str = app_config_client.get_setting(
                                "ConnectionStrings:PostgreSQL-HART-DESKTOP"
                            )

                        if pg_host_str:
                            # Parse connection string components
                            for part in pg_host_str.split(";"):
                                if "=" in part:
                                    key, val = part.split("=", 1)
                                    key = key.strip().lower()
                                    val = val.strip()

                                    if key == "host":
                                        self.pghost = val
                                    elif key == "port":
                                        self.pgport = int(val)
                                    elif key == "database":
                                        self.pgdatabase = val
                                    elif key == "username":
                                        self.pguser = val

                        logger.info("Loaded settings from App Configuration")

                    except Exception as e:
                        logger.warning(f"Could not load from App Configuration: {e}")

                # Load secrets from Key Vault
                if kv_client:
                    try:
                        # PostgreSQL password
                        self.pgpassword = kv_client.get_secret(
                            "PostgreSQL-Hartonomous-Password"
                        )
                        logger.info("Loaded PostgreSQL password from Key Vault")
                    except Exception as e:
                        logger.warning(f"Could not load PostgreSQL password: {e}")

                    # Neo4j credentials (for hart-server)
                    if self.neo4j_enabled:
                        try:
                            # Determine which Neo4j instance to use based on hostname
                            import socket

                            hostname = socket.gethostname().upper()

                            if hostname.lower() == "hart-server":
                                # Load hart-server Neo4j credentials from Key Vault
                                neo4j_password = kv_client.get_secret(
                                    "Neo4j-hart-server-Password"
                                )
                                if neo4j_password:
                                    self.neo4j_password = neo4j_password
                                    # Update URI for hart-server
                                    neo4j_uri = (
                                        app_config_client.get_setting(
                                            "Neo4j:hart-server:Uri"
                                        )
                                        if app_config_client
                                        else None
                                    )
                                    if neo4j_uri:
                                        self.neo4j_uri = neo4j_uri
                                    logger.info(
                                        "Loaded Neo4j credentials for hart-server from Key Vault"
                                    )
                            # HART-DESKTOP uses default config (neo4j:neo4jneo4j @ localhost:7687)
                            else:
                                logger.info(
                                    f"Using local Neo4j Desktop configuration for {hostname}"
                                )
                        except Exception as e:
                            logger.warning(f"Could not load Neo4j credentials: {e}")

                    # Entra ID client secret
                    if self.auth_enabled:
                        try:
                            self.entra_client_secret = kv_client.get_secret(
                                "AzureAd-ClientSecret"
                            )
                            logger.info("Loaded Entra ID client secret from Key Vault")
                        except Exception as e:
                            logger.warning(
                                f"Could not load Entra ID client secret: {e}"
                            )

                    # B2C client secret
                    if self.b2c_enabled:
                        try:
                            # Note: Using EntraExternalId-ClientSecret (CIAM successor to B2C)
                            self.entra_client_secret = kv_client.get_secret(
                                "EntraExternalId-ClientSecret"
                            )
                            logger.info("Loaded CIAM client secret from Key Vault")
                        except Exception as e:
                            logger.warning(f"Could not load CIAM client secret: {e}")

            except ImportError:
                logger.error(
                    "Azure SDKs not installed (azure-identity, azure-keyvault-secrets, azure-appconfiguration)"
                )

        return self

    @field_validator("database_url", mode="after")
    @classmethod
    def build_database_url(cls, v: Optional[str], info) -> str:
        """Build DATABASE_URL from components if not provided."""
        if v:
            return v

        # At this point, all fields should be populated from .env
        # Access via info.data doesn't work in 'after' mode, use self instead
        # This validator should actually be removed and we should use a property
        return None  # Will be built by get_connection_string()

    def get_connection_string(self) -> str:
        """Get PostgreSQL connection string."""
        if self.database_url:
            return self.database_url

        # Build from components
        return (
            f"postgresql://{self.pguser}:{self.pgpassword}@{self.pghost}:{self.pgport}/{self.pgdatabase}"
            f"?sslmode={self.pgsslmode}"
        )


# Global settings instance
settings = Settings()


# Export for convenience
__all__ = ["settings", "Settings"]
