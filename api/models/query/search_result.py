"""Search result model."""

from typing import Optional
from pydantic import BaseModel, Field


class SearchResult(BaseModel):
    """Single search result."""

    atom_id: int = Field(..., description="Atom ID")
    canonical_text: Optional[str] = Field(None, description="Text")
    distance: float = Field(..., description="Spatial distance")
    relevance: float = Field(..., description="Relevance score (0-1)")
