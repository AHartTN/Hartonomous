"""Azure Key Vault client."""

import logging
from typing import Optional

from azure.identity import DefaultAzureCredential, ManagedIdentityCredential
from azure.keyvault.secrets import SecretClient

logger = logging.getLogger(__name__)


class AzureKeyVaultClient:
    """
    Client for Azure Key Vault secret retrieval.

    Uses Managed Identity in Azure, DefaultAzureCredential locally.
    """

    def __init__(self, key_vault_url: str, client_id: Optional[str] = None):
        """Initialize Key Vault client."""
        self.key_vault_url = key_vault_url

        if client_id:
            self.credential = ManagedIdentityCredential(client_id=client_id)
            logger.info(f"Using user-assigned managed identity: {client_id}")
        else:
            self.credential = DefaultAzureCredential()
            logger.info("Using DefaultAzureCredential (managed identity or az CLI)")

        self.client = SecretClient(vault_url=key_vault_url, credential=self.credential)

        logger.info(f"Connected to Key Vault: {key_vault_url}")

    def get_secret(self, secret_name: str) -> str:
        """Get secret from Key Vault."""
        try:
            secret = self.client.get_secret(secret_name)
            logger.debug(f"Retrieved secret: {secret_name}")
            return secret.value
        except Exception as e:
            logger.error(f"Failed to retrieve secret '{secret_name}': {e}")
            raise
