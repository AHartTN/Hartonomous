"""Lineage response model."""

from typing import List
from pydantic import BaseModel, Field

from .lineage_node import LineageNode


class LineageResponse(BaseModel):
    """Response model for lineage query."""

    root_atom_id: int = Field(..., description="Starting atom ID")
    max_depth: int = Field(..., description="Maximum depth queried")
    nodes: List[LineageNode] = Field(..., description="Lineage nodes")
    total_ancestors: int = Field(..., description="Total ancestor count")
