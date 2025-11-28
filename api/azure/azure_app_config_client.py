"""Azure App Configuration client."""

import logging
from typing import Optional

from azure.appconfiguration import AzureAppConfigurationClient
from azure.identity import DefaultAzureCredential, ManagedIdentityCredential

logger = logging.getLogger(__name__)


class AzureAppConfigClient:
    """
    Client for Azure App Configuration.

    Retrieves configuration settings and feature flags.
    """

    def __init__(self, endpoint: str, client_id: Optional[str] = None):
        """Initialize App Configuration client."""
        self.endpoint = endpoint

        if client_id:
            self.credential = ManagedIdentityCredential(client_id=client_id)
        else:
            self.credential = DefaultAzureCredential()

        self.client = AzureAppConfigurationClient(
            base_url=endpoint, credential=self.credential
        )

        logger.info(f"Connected to App Configuration: {endpoint}")

    def get_setting(self, key: str, label: Optional[str] = None) -> str:
        """Get configuration setting."""
        try:
            setting = self.client.get_configuration_setting(key=key, label=label)
            logger.debug(f"Retrieved setting: {key} (label: {label})")
            return setting.value
        except Exception as e:
            logger.error(f"Failed to retrieve setting '{key}': {e}")
            raise
