"""
Azure configuration management using Key Vault + App Configuration.

Replaces .env files with enterprise Azure security.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

import os
import logging
from typing import Optional
from functools import lru_cache

from azure.identity import DefaultAzureCredential, ManagedIdentityCredential
from azure.keyvault.secrets import SecretClient
from azure.appconfiguration import AzureAppConfigurationClient
from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

logger = logging.getLogger(__name__)


class AzureSettings(BaseSettings):
    """
    Azure-specific settings.
    
    Uses Managed Identity when running in Azure (Container Apps, AKS, VMs).
    Falls back to DefaultAzureCredential for local development (uses az CLI).
    """
    
    # Azure Key Vault
    key_vault_url: Optional[str] = Field(
        default=None,
        description="Azure Key Vault URL (e.g., https://my-vault.vault.azure.net/)"
    )
    
    # Azure App Configuration
    app_config_endpoint: Optional[str] = Field(
        default=None,
        description="Azure App Configuration endpoint"
    )
    
    # Managed Identity (for Azure deployments)
    azure_client_id: Optional[str] = Field(
        default=None,
        description="Managed Identity client ID (optional, system-assigned if not provided)"
    )
    
    # Azure Arc
    azure_arc_enabled: bool = Field(
        default=False,
        description="Enable Azure Arc connectivity"
    )
    
    # Environment
    azure_environment: str = Field(
        default="development",
        description="Environment: development, staging, production"
    )
    
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore"
    )


class AzureKeyVaultClient:
    """
    Client for Azure Key Vault secret retrieval.
    
    Uses Managed Identity in Azure, DefaultAzureCredential locally.
    """
    
    def __init__(self, key_vault_url: str, client_id: Optional[str] = None):
        """
        Initialize Key Vault client.
        
        Args:
            key_vault_url: Key Vault URL
            client_id: Optional Managed Identity client ID
        """
        self.key_vault_url = key_vault_url
        
        # Use appropriate credential based on environment
        if client_id:
            # User-assigned managed identity
            self.credential = ManagedIdentityCredential(client_id=client_id)
            logger.info(f"Using user-assigned managed identity: {client_id}")
        else:
            # System-assigned managed identity or local development
            self.credential = DefaultAzureCredential()
            logger.info("Using DefaultAzureCredential (managed identity or az CLI)")
        
        self.client = SecretClient(
            vault_url=key_vault_url,
            credential=self.credential
        )
        
        logger.info(f"Connected to Key Vault: {key_vault_url}")
    
    def get_secret(self, secret_name: str) -> str:
        """
        Get secret from Key Vault.
        
        Args:
            secret_name: Secret name
        
        Returns:
            Secret value
        """
        try:
            secret = self.client.get_secret(secret_name)
            logger.debug(f"Retrieved secret: {secret_name}")
            return secret.value
        except Exception as e:
            logger.error(f"Failed to retrieve secret '{secret_name}': {e}")
            raise


class AzureAppConfigClient:
    """
    Client for Azure App Configuration.
    
    Retrieves configuration settings and feature flags.
    """
    
    def __init__(self, endpoint: str, client_id: Optional[str] = None):
        """
        Initialize App Configuration client.
        
        Args:
            endpoint: App Configuration endpoint
            client_id: Optional Managed Identity client ID
        """
        self.endpoint = endpoint
        
        # Use appropriate credential
        if client_id:
            self.credential = ManagedIdentityCredential(client_id=client_id)
        else:
            self.credential = DefaultAzureCredential()
        
        self.client = AzureAppConfigurationClient(
            base_url=endpoint,
            credential=self.credential
        )
        
        logger.info(f"Connected to App Configuration: {endpoint}")
    
    def get_setting(self, key: str, label: Optional[str] = None) -> str:
        """
        Get configuration setting.
        
        Args:
            key: Setting key
            label: Optional label (e.g., "production", "staging")
        
        Returns:
            Setting value
        """
        try:
            setting = self.client.get_configuration_setting(key=key, label=label)
            logger.debug(f"Retrieved setting: {key} (label: {label})")
            return setting.value
        except Exception as e:
            logger.error(f"Failed to retrieve setting '{key}': {e}")
            raise


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
        key_vault_url=settings.key_vault_url,
        client_id=settings.azure_client_id
    )


@lru_cache()
def get_app_config_client() -> Optional[AzureAppConfigClient]:
    """Get cached App Configuration client."""
    settings = get_azure_settings()
    
    if not settings.app_config_endpoint:
        logger.warning("App Configuration endpoint not configured, skipping...")
        return None
    
    return AzureAppConfigClient(
        endpoint=settings.app_config_endpoint,
        client_id=settings.azure_client_id
    )


__all__ = [
    "AzureSettings",
    "AzureKeyVaultClient",
    "AzureAppConfigClient",
    "get_azure_settings",
    "get_key_vault_client",
    "get_app_config_client",
]
