"""Code atomizer client for C# microservice."""

import logging
from typing import Any, Dict, Optional

import httpx

from api.config import settings

logger = logging.getLogger(__name__)


class CodeAtomizerClient:
    """Client for Hartonomous Code Atomizer microservice (C# + Roslyn)."""

    def __init__(self, base_url: str = settings.code_atomizer_url):
        self.base_url = base_url.rstrip("/")
        self.client = httpx.AsyncClient(timeout=30.0)

    async def atomize_csharp(
        self, code: str, filename: str = "code.cs", metadata: Optional[str] = None
    ) -> Dict[str, Any]:
        """Atomize C# code using Roslyn semantic analysis."""
        try:
            response = await self.client.post(
                f"{self.base_url}/api/v1/atomize/csharp",
                json={"code": code, "fileName": filename, "metadata": metadata},
            )
            response.raise_for_status()
            return response.json()
        except httpx.HTTPError as e:
            logger.error(f"Code atomization failed: {e}")
            raise

    async def atomize_file(
        self, file_path: str, language: str = "csharp"
    ) -> Dict[str, Any]:
        """Atomize code file via microservice."""
        with open(file_path, "rb") as f:
            files = {"file": (file_path, f, "text/plain")}

            try:
                response = await self.client.post(
                    f"{self.base_url}/api/v1/atomize/{language}/file", files=files
                )
                response.raise_for_status()
                return response.json()
            except httpx.HTTPError as e:
                logger.error(f"File atomization failed: {e}")
                raise

    async def health_check(self) -> bool:
        """Check if code atomizer service is healthy."""
        try:
            response = await self.client.get(f"{self.base_url}/api/v1/atomize/health")
            return response.status_code == 200
        except httpx.HTTPError:
            return False

    async def close(self):
        """Close HTTP client."""
        await self.client.aclose()

    async def atomize_any_language(
        self,
        code: str,
        language: str,
        filename: str = "code.txt",
        metadata: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Atomize code in any supported language."""
        try:
            response = await self.client.post(
                f"{self.base_url}/api/v1/atomize/{language}",
                json={"code": code, "fileName": filename, "metadata": metadata},
            )
            response.raise_for_status()
            return response.json()
        except httpx.HTTPError as e:
            logger.error(f"Code atomization failed for {language}: {e}")
            raise

    async def get_supported_languages(self) -> Dict[str, Any]:
        """Get list of supported languages."""
        try:
            response = await self.client.get(
                f"{self.base_url}/api/v1/atomize/languages"
            )
            response.raise_for_status()
            return response.json()
        except httpx.HTTPError as e:
            logger.error(f"Failed to get supported languages: {e}")
            raise
