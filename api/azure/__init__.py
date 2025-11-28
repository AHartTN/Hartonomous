"""Azure configuration package."""

import logging
from functools import lru_cache
from typing import Optional

from .azure_app_config_client import AzureAppConfigClient
from .azure_key_vault_client import AzureKeyVaultClient
from .azure_settings import AzureSettings

logger = logging.getLogger(__name__)


@lru_cache()
def get_azure_settings() -> AzureSettings:
    """Get cached Azure settings."""
    return AzureSettings()


@lru_cache()
def get_key_vault_client() -> Optional[AzureKeyVaultClient]:
    """Get cached Key Vault client."""
    settings = get_azure_settings()

    if not settings.key_vault_url:
        logger.warning("Key Vault URL not configured, skipping...")
        return None

    return AzureKeyVaultClient(
        key_vault_url=settings.key_vault_url, client_id=settings.azure_client_id
    )


@lru_cache()
def get_app_config_client() -> Optional[AzureAppConfigClient]:
    """Get cached App Configuration client."""
    settings = get_azure_settings()

    if not settings.app_config_endpoint:
        logger.warning("App Configuration endpoint not configured, skipping...")
        return None

    return AzureAppConfigClient(
        endpoint=settings.app_config_endpoint, client_id=settings.azure_client_id
    )


__all__ = [
    "AzureSettings",
    "AzureKeyVaultClient",
    "AzureAppConfigClient",
    "get_azure_settings",
    "get_key_vault_client",
    "get_app_config_client",
]
