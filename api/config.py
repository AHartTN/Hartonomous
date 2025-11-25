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

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

import os
import logging
from typing import Optional
from pydantic import Field, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

logger = logging.getLogger(__name__)


class Settings(BaseSettings):
    """Application settings with environment variable support."""
    
    # Azure Configuration (production)
    use_azure_config: bool = Field(
        default=False,
        description="Use Azure Key Vault + App Configuration"
    )
    key_vault_url: Optional[str] = Field(
        default=None,
        description="Azure Key Vault URL"
    )
    app_config_endpoint: Optional[str] = Field(
        default=None,
        description="Azure App Configuration endpoint"
    )
    azure_client_id: Optional[str] = Field(
        default=None,
        description="Managed Identity client ID"
    )
    
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
    
    # Authentication (Entra ID)
    auth_enabled: bool = Field(default=False, description="Enable authentication")
    entra_tenant_id: Optional[str] = Field(default=None, description="Entra ID tenant ID")
    entra_client_id: Optional[str] = Field(default=None, description="Entra ID client ID")
    entra_client_secret: Optional[str] = Field(default=None, description="Entra ID client secret")
    
    # B2C (CIAM for external users)
    b2c_enabled: bool = Field(default=False, description="Enable Azure AD B2C")
    b2c_tenant_name: Optional[str] = Field(default=None, description="B2C tenant name")
    b2c_client_id: Optional[str] = Field(default=None, description="B2C client ID")
    b2c_policy_name: Optional[str] = Field(default=None, description="B2C policy name")
    
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
    
    @model_validator(mode="after")
    def load_from_azure(self):
        """Load secrets from Azure Key Vault if enabled."""
        if self.use_azure_config and self.key_vault_url:
            logger.info("Loading configuration from Azure Key Vault...")
            
            try:
                from api.azure_config import get_key_vault_client
                
                kv_client = get_key_vault_client()
                
                if kv_client:
                    # Load database password from Key Vault
                    try:
                        self.pgpassword = kv_client.get_secret("postgres-password")
                        logger.info("Loaded postgres-password from Key Vault")
                    except Exception as e:
                        logger.warning(f"Could not load postgres-password: {e}")
                    
                    # Load Entra ID secret from Key Vault
                    if self.auth_enabled:
                        try:
                            self.entra_client_secret = kv_client.get_secret("entra-client-secret")
                            logger.info("Loaded entra-client-secret from Key Vault")
                        except Exception as e:
                            logger.warning(f"Could not load entra-client-secret: {e}")
            
            except ImportError:
                logger.error("azure-identity or azure-keyvault-secrets not installed")
        
        return self
    
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
