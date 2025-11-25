"""
Entra ID (Azure AD) authentication for FastAPI.

Supports:
- Internal users (Entra ID)
- External users (Azure AD B2C / CIAM)
- OAuth2 / OIDC flows
- JWT validation

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Optional, Dict, Any
from datetime import datetime, timedelta
from functools import lru_cache

import jwt
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
import msal
import requests

from api.config import settings

logger = logging.getLogger(__name__)

# HTTP Bearer token scheme
security = HTTPBearer()

# JWKS key cache (TTL: 24 hours)
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
        
        # MSAL confidential client
        self.app = msal.ConfidentialClientApplication(
            client_id=self.client_id,
            client_credential=self.client_secret,
            authority=f"https://login.microsoftonline.com/{self.tenant_id}"
        )
        
        # JWT validation settings
        self.issuer = f"https://sts.windows.net/{self.tenant_id}/"
        self.jwks_uri = f"https://login.microsoftonline.com/{self.tenant_id}/discovery/v2.0/keys"
        
        logger.info(f"Entra ID initialized for tenant: {self.tenant_id}")
    
    def _get_jwks(self) -> Dict[str, Any]:
        """
        Get JWKS keys with 24-hour caching.

        Returns:
            JWKS response
        """
        global _jwks_cache

        now = datetime.utcnow()
        cache_key = self.jwks_uri

        # Check cache
        if cache_key in _jwks_cache:
            cached_jwks, cached_time = _jwks_cache[cache_key]
            if now - cached_time < timedelta(hours=24):
                logger.debug("Using cached JWKS keys")
                return cached_jwks

        # Fetch fresh JWKS
        logger.debug("Fetching fresh JWKS keys")
        try:
            jwks_response = requests.get(self.jwks_uri, timeout=5)
            jwks_response.raise_for_status()
            jwks = jwks_response.json()

            # Cache for 24 hours
            _jwks_cache[cache_key] = (jwks, now)

            return jwks
        except Exception as e:
            logger.error(f"Failed to fetch JWKS: {e}")
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Unable to fetch JWT signing keys"
            )

    def validate_token(self, token: str) -> Dict[str, Any]:
        """
        Validate JWT access token.

        Args:
            token: JWT access token

        Returns:
            Decoded token claims

        Raises:
            HTTPException: If token invalid
        """
        try:
            # Decode without verification first (to get kid)
            unverified_header = jwt.get_unverified_header(token)
            kid = unverified_header.get("kid")

            # Get signing key from JWKS endpoint (with caching)
            jwks = self._get_jwks()

            # Find matching key
            signing_key = None
            for key in jwks.get("keys", []):
                if key.get("kid") == kid:
                    signing_key = jwt.algorithms.RSAAlgorithm.from_jwk(key)
                    break

            if not signing_key:
                raise HTTPException(
                    status_code=status.HTTP_401_UNAUTHORIZED,
                    detail="Token signing key not found"
                )

            # Verify and decode token
            payload = jwt.decode(
                token,
                signing_key,
                algorithms=["RS256"],
                audience=self.client_id,
                issuer=self.issuer
            )

            logger.debug(f"Token validated for user: {payload.get('preferred_username')}")

            return payload

        except jwt.ExpiredSignatureError:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token expired"
            )
        except jwt.InvalidTokenError as e:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=f"Invalid token: {str(e)}"
            )
        except Exception as e:
            logger.error(f"Token validation failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Token validation failed"
            )


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

        # B2C authority
        self.authority = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{self.tenant_name}.onmicrosoft.com/{self.policy_name}"
        )

        # B2C JWKS URI
        self.jwks_uri = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{self.tenant_name}.onmicrosoft.com/{self.policy_name}/discovery/v2.0/keys"
        )

        # B2C issuer
        self.issuer = (
            f"https://{self.tenant_name}.b2clogin.com/"
            f"{settings.b2c_tenant_id}/{self.policy_name}/v2.0/"
        )

        # MSAL public client (B2C uses public client flow)
        self.app = msal.PublicClientApplication(
            client_id=self.client_id,
            authority=self.authority
        )

        logger.info(f"B2C initialized for tenant: {self.tenant_name}")

    def _get_jwks(self) -> Dict[str, Any]:
        """
        Get B2C JWKS keys with 24-hour caching.

        Returns:
            JWKS response
        """
        global _jwks_cache

        now = datetime.utcnow()
        cache_key = self.jwks_uri

        # Check cache
        if cache_key in _jwks_cache:
            cached_jwks, cached_time = _jwks_cache[cache_key]
            if now - cached_time < timedelta(hours=24):
                logger.debug("Using cached B2C JWKS keys")
                return cached_jwks

        # Fetch fresh JWKS
        logger.debug("Fetching fresh B2C JWKS keys")
        try:
            jwks_response = requests.get(self.jwks_uri, timeout=5)
            jwks_response.raise_for_status()
            jwks = jwks_response.json()

            # Cache for 24 hours
            _jwks_cache[cache_key] = (jwks, now)

            return jwks
        except Exception as e:
            logger.error(f"Failed to fetch B2C JWKS: {e}")
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Unable to fetch B2C JWT signing keys"
            )

    def validate_token(self, token: str) -> Dict[str, Any]:
        """
        Validate B2C JWT token with full signature verification.

        Args:
            token: JWT access token

        Returns:
            Decoded token claims

        Raises:
            HTTPException: If token invalid
        """
        try:
            # Decode without verification first (to get kid)
            unverified_header = jwt.get_unverified_header(token)
            kid = unverified_header.get("kid")

            # Get signing key from B2C JWKS endpoint (with caching)
            jwks = self._get_jwks()

            # Find matching key
            signing_key = None
            for key in jwks.get("keys", []):
                if key.get("kid") == kid:
                    signing_key = jwt.algorithms.RSAAlgorithm.from_jwk(key)
                    break

            if not signing_key:
                raise HTTPException(
                    status_code=status.HTTP_401_UNAUTHORIZED,
                    detail="B2C token signing key not found"
                )

            # Verify and decode token
            payload = jwt.decode(
                token,
                signing_key,
                algorithms=["RS256"],
                audience=self.client_id,
                issuer=self.issuer,
                options={"verify_signature": True}  # ✅ Signature verification ENABLED
            )

            logger.debug(f"B2C token validated for user: {payload.get('sub')}")

            return payload

        except jwt.ExpiredSignatureError:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="B2C token expired"
            )
        except jwt.InvalidTokenError as e:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=f"Invalid B2C token: {str(e)}"
            )
        except Exception as e:
            logger.error(f"B2C token validation failed: {e}", exc_info=True)
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="B2C token validation failed"
            )


# Global auth instances
_entra_auth: Optional[EntraIDAuth] = None
_b2c_auth: Optional[B2CAuth] = None


def get_entra_auth() -> EntraIDAuth:
    """Get Entra ID auth instance."""
    global _entra_auth
    if _entra_auth is None:
        _entra_auth = EntraIDAuth()
    return _entra_auth


def get_b2c_auth() -> B2CAuth:
    """Get B2C auth instance."""
    global _b2c_auth
    if _b2c_auth is None:
        _b2c_auth = B2CAuth()
    return _b2c_auth


async def get_current_user(
    credentials: HTTPAuthorizationCredentials = Depends(security)
) -> Dict[str, Any]:
    """
    Dependency: Get current authenticated user.
    
    Validates JWT token from Authorization header.
    Supports both Entra ID (internal) and B2C (external) users.
    
    Returns:
        User claims from JWT
    
    Raises:
        HTTPException: If authentication fails
    """
    if not settings.auth_enabled:
        # Auth disabled - return mock user
        return {
            "sub": "anonymous",
            "name": "Anonymous User",
            "email": "anonymous@example.com"
        }
    
    token = credentials.credentials
    
    # Try Entra ID first (internal users)
    if settings.entra_tenant_id:
        try:
            entra_auth = get_entra_auth()
            user = entra_auth.validate_token(token)
            user["auth_type"] = "entra_id"
            return user
        except HTTPException:
            pass  # Fall through to B2C
    
    # Try B2C (external users)
    if settings.b2c_enabled:
        try:
            b2c_auth = get_b2c_auth()
            user = b2c_auth.validate_token(token)
            user["auth_type"] = "b2c"
            return user
        except HTTPException:
            pass
    
    # Authentication failed
    raise HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Invalid or expired token",
        headers={"WWW-Authenticate": "Bearer"}
    )


async def require_internal_user(
    user: Dict[str, Any] = Depends(get_current_user)
) -> Dict[str, Any]:
    """
    Dependency: Require internal user (Entra ID).
    
    Rejects B2C users.
    
    Returns:
        User claims
    
    Raises:
        HTTPException: If user is not internal
    """
    if user.get("auth_type") != "entra_id":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Internal users only"
        )
    
    return user


async def require_admin(
    user: Dict[str, Any] = Depends(get_current_user)
) -> Dict[str, Any]:
    """
    Dependency: Require admin role.
    
    Checks for 'admin' role in token claims.
    
    Returns:
        User claims
    
    Raises:
        HTTPException: If user is not admin
    """
    roles = user.get("roles", [])
    
    if "admin" not in roles and "Admin" not in roles:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Admin access required"
        )
    
    return user


__all__ = [
    "EntraIDAuth",
    "B2CAuth",
    "get_current_user",
    "require_internal_user",
    "require_admin",
]
