"""Azure AD B2C authentication handler."""

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


class B2CAuth:
    """Azure AD B2C authentication handler for external users."""

    def __init__(self):
        """Initialize B2C authentication."""
        self.tenant_name = settings.b2c_tenant_name
        self.client_id = settings.b2c_client_id
        self.policy_name = settings.b2c_policy_name

        if not all([self.tenant_name, self.client_id, self.policy_name]):
            logger.warning("B2C not fully configured")
            return

        self.authority = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{self.tenant_name}.onmicrosoft.com/{self.policy_name}"
        )

        self.jwks_uri = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{self.tenant_name}.onmicrosoft.com/{self.policy_name}/discovery/v2.0/keys"
        )

        self.issuer = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{settings.b2c_tenant_id}/{self.policy_name}/v2.0/"
        )

        self.app = msal.PublicClientApplication(
            client_id=self.client_id, authority=self.authority
        )

        logger.info(f"B2C initialized for tenant: {self.tenant_name}")

    def _get_jwks(self) -> Dict[str, Any]:
        """Get B2C JWKS keys with 24-hour caching."""
        global _jwks_cache

        now = datetime.utcnow()
        cache_key = self.jwks_uri

        if cache_key in _jwks_cache:
            cached_jwks, cached_time = _jwks_cache[cache_key]
            if now - cached_time < timedelta(hours=24):
                logger.debug("Using cached B2C JWKS keys")
                return cached_jwks

        logger.debug("Fetching fresh B2C JWKS keys")
        try:
            jwks_response = requests.get(self.jwks_uri, timeout=5)
            jwks_response.raise_for_status()
            jwks = jwks_response.json()

            _jwks_cache[cache_key] = (jwks, now)

            return jwks
        except Exception as e:
            logger.error(f"Failed to fetch B2C JWKS: {e}")
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Unable to fetch B2C JWT signing keys",
            )

    def validate_token(self, token: str) -> Dict[str, Any]:
        """Validate B2C JWT token with full signature verification."""
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
                    detail="B2C token signing key not found",
                )

            payload = jwt.decode(
                token,
                signing_key,
                algorithms=["RS256"],
                audience=self.client_id,
                issuer=self.issuer,
                options={"verify_signature": True},
            )

            logger.debug(f"B2C token validated for user: {payload.get('sub')}")

            return payload

        except jwt.ExpiredSignatureError:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED, detail="B2C token expired"
            )
        except jwt.InvalidTokenError as e:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=f"Invalid B2C token: {str(e)}",
            )
        except Exception as e:
            logger.error(f"B2C token validation failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="B2C token validation failed",
            )
