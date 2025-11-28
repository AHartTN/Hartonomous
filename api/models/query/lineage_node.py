"""Lineage node model."""

from typing import Optional
from pydantic import BaseModel, Field


class LineageNode(BaseModel):
    """Node in provenance lineage graph."""

    atom_id: int = Field(..., description="Atom ID")
    content_hash: str = Field(..., description="Content hash")
    canonical_text: Optional[str] = Field(None, description="Text")
    depth: int = Field(..., description="Depth in lineage tree")
    parent_atom_id: Optional[int] = Field(None, description="Parent atom (if any)")
