"""Entra ID authentication handler."""

import logging
from datetime import datetime, timedelta
from typing import Any, Dict

import jwt
import msal
import requests
from fastapi import HTTPException, status

from api.config import settings

logger = logging.getLogger(__name__)

_jwks_cache: Dict[str, tuple[Dict[str, Any], datetime]] = {}


class EntraIDAuth:
    """Entra ID authentication handler."""

    def __init__(self):
        """Initialize Entra ID authentication."""
        self.tenant_id = settings.entra_tenant_id
        self.client_id = settings.entra_client_id
        self.client_secret = settings.entra_client_secret

        if not all([self.tenant_id, self.client_id]):
            logger.warning("Entra ID not fully configured")
            return

        self.app = msal.ConfidentialClientApplication(
            client_id=self.client_id,
            client_credential=self.client_secret,
            authority=f"https://login.microsoftonline.com/{self.tenant_id}",
        )

        self.issuer = f"https://sts.windows.net/{self.tenant_id}/"
        self.jwks_uri = (
            f"https://login.microsoftonline.com/{self.tenant_id}/discovery/v2.0/keys"
        )

        logger.info(f"Entra ID initialized for tenant: {self.tenant_id}")

    def _get_jwks(self) -> Dict[str, Any]:
        """Get JWKS keys with 24-hour caching."""
        global _jwks_cache

        now = datetime.utcnow()
        cache_key = self.jwks_uri

        if cache_key in _jwks_cache:
            cached_jwks, cached_time = _jwks_cache[cache_key]
            if now - cached_time < timedelta(hours=24):
                logger.debug("Using cached JWKS keys")
                return cached_jwks

        logger.debug("Fetching fresh JWKS keys")
        try:
            jwks_response = requests.get(self.jwks_uri, timeout=5)
            jwks_response.raise_for_status()
            jwks = jwks_response.json()

            _jwks_cache[cache_key] = (jwks, now)

            return jwks
        except Exception as e:
            logger.error(f"Failed to fetch JWKS: {e}")
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Unable to fetch JWT signing keys",
            )

    def validate_token(self, token: str) -> Dict[str, Any]:
        """Validate JWT access token."""
        try:
            unverified_header = jwt.get_unverified_header(token)
            kid = unverified_header.get("kid")

            jwks = self._get_jwks()

            signing_key = None
            for key in jwks.get("keys", []):
                if key.get("kid") == kid:
                    signing_key = jwt.algorithms.RSAAlgorithm.from_jwk(key)
                    break

            if not signing_key:
                raise HTTPException(
                    status_code=status.HTTP_401_UNAUTHORIZED,
                    detail="Token signing key not found",
                )

            payload = jwt.decode(
                token,
                signing_key,
                algorithms=["RS256"],
                audience=self.client_id,
                issuer=self.issuer,
            )

            logger.debug(
                f"Token validated for user: {payload.get('preferred_username')}"
            )

            return payload

        except jwt.ExpiredSignatureError:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED, detail="Token expired"
            )
        except jwt.InvalidTokenError as e:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=f"Invalid token: {str(e)}",
            )
        except Exception as e:
            logger.error(f"Token validation failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token validation failed",
            )
