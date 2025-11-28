"""Authentication package."""

from .entra_id_auth import EntraIDAuth
from .b2c_auth import B2CAuth
from .dependencies import (
    get_current_user,
    require_internal_user,
    require_admin,
)

__all__ = [
    "EntraIDAuth",
    "B2CAuth",
    "get_current_user",
    "require_internal_user",
    "require_admin",
]
