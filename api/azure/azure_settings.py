"""Azure settings configuration."""

import logging
from typing import Optional

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

logger = logging.getLogger(__name__)


class AzureSettings(BaseSettings):
    """
    Azure-specific settings.

    Uses Managed Identity when running in Azure (Container Apps, AKS, VMs).
    Falls back to DefaultAzureCredential for local development (uses az CLI).
    """

    key_vault_url: Optional[str] = Field(
        default=None,
        description="Azure Key Vault URL (e.g., https://my-vault.vault.azure.net/)",
    )

    app_config_endpoint: Optional[str] = Field(
        default=None, description="Azure App Configuration endpoint"
    )

    azure_client_id: Optional[str] = Field(
        default=None,
        description="Managed Identity client ID (optional, system-assigned if not provided)",
    )

    azure_arc_enabled: bool = Field(
        default=False, description="Enable Azure Arc connectivity"
    )

    azure_environment: str = Field(
        default="development",
        description="Environment: development, staging, production",
    )

    model_config = SettingsConfigDict(
        env_file=".env", env_file_encoding="utf-8", case_sensitive=False, extra="ignore"
    )
