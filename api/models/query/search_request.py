"""Search request model."""

from typing import Optional

from pydantic import BaseModel, Field


class SearchRequest(BaseModel):
    """Request model for spatial search."""

    query: str = Field(
        ..., min_length=1, max_length=1000, description="Search query (text or atom ID)"
    )

    limit: int = Field(default=10, ge=1, le=1000, description="Maximum results")

    radius: Optional[float] = Field(
        default=None, ge=0.0, le=10.0, description="Spatial search radius (optional)"
    )
