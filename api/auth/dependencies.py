"""Authentication dependencies and utilities."""

from typing import Any, Dict, Optional
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from api.config import settings
from .entra_id_auth import EntraIDAuth
from .b2c_auth import B2CAuth

security = HTTPBearer()

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
    credentials: HTTPAuthorizationCredentials = Depends(security),
) -> Dict[str, Any]:
    """Dependency: Get current authenticated user."""
    if not settings.auth_enabled:
        return {
            "sub": "anonymous",
            "name": "Anonymous User",
            "email": "anonymous@example.com",
        }

    token = credentials.credentials

    if settings.entra_tenant_id:
        try:
            entra_auth = get_entra_auth()
            user = entra_auth.validate_token(token)
            user["auth_type"] = "entra_id"
            return user
        except HTTPException:
            pass

    if settings.b2c_enabled:
        try:
            b2c_auth = get_b2c_auth()
            user = b2c_auth.validate_token(token)
            user["auth_type"] = "b2c"
            return user
        except HTTPException:
            pass

    raise HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Invalid or expired token",
        headers={"WWW-Authenticate": "Bearer"},
    )


async def require_internal_user(
    user: Dict[str, Any] = Depends(get_current_user),
) -> Dict[str, Any]:
    """Dependency: Require internal user (Entra ID)."""
    if user.get("auth_type") != "entra_id":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail="Internal users only"
        )

    return user


async def require_admin(
    user: Dict[str, Any] = Depends(get_current_user),
) -> Dict[str, Any]:
    """Dependency: Require admin role."""
    roles = user.get("roles", [])

    if "admin" not in roles and "Admin" not in roles:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail="Admin access required"
        )

    return user


__all__ = [
    "get_current_user",
    "require_internal_user",
    "require_admin",
]
