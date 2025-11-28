"""Atom response model."""

from typing import Any, Dict, Optional
from pydantic import BaseModel, Field


class AtomResponse(BaseModel):
    """Response model for atom query."""

    atom_id: int = Field(..., description="Unique atom ID")
    content_hash: str = Field(..., description="SHA-256 content hash")
    canonical_text: Optional[str] = Field(None, description="Text representation")
    byte_value: Optional[bytes] = Field(None, description="Binary value")
    spatial_position: Optional[str] = Field(None, description="3D spatial coordinates")
    metadata: Optional[Dict[str, Any]] = Field(None, description="Metadata (JSON)")
    created_at: str = Field(..., description="Creation timestamp")
