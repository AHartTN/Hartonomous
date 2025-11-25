"""
Pydantic models for query requests/responses.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import Optional, Dict, Any, List
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
    

class LineageNode(BaseModel):
    """Node in provenance lineage graph."""
    
    atom_id: int = Field(..., description="Atom ID")
    content_hash: str = Field(..., description="Content hash")
    canonical_text: Optional[str] = Field(None, description="Text")
    depth: int = Field(..., description="Depth in lineage tree")
    parent_atom_id: Optional[int] = Field(None, description="Parent atom (if any)")


class LineageResponse(BaseModel):
    """Response model for lineage query."""
    
    root_atom_id: int = Field(..., description="Starting atom ID")
    max_depth: int = Field(..., description="Maximum depth queried")
    nodes: List[LineageNode] = Field(..., description="Lineage nodes")
    total_ancestors: int = Field(..., description="Total ancestor count")


class SearchRequest(BaseModel):
    """Request model for spatial search."""
    
    query: str = Field(
        ...,
        min_length=1,
        max_length=1000,
        description="Search query (text or atom ID)"
    )
    
    limit: int = Field(
        default=10,
        ge=1,
        le=1000,
        description="Maximum results"
    )
    
    radius: Optional[float] = Field(
        default=None,
        ge=0.0,
        le=10.0,
        description="Spatial search radius (optional)"
    )


class SearchResult(BaseModel):
    """Single search result."""
    
    atom_id: int = Field(..., description="Atom ID")
    canonical_text: Optional[str] = Field(None, description="Text")
    distance: float = Field(..., description="Spatial distance")
    relevance: float = Field(..., description="Relevance score (0-1)")


class SearchResponse(BaseModel):
    """Response model for search query."""
    
    query: str = Field(..., description="Original query")
    results: List[SearchResult] = Field(..., description="Search results")
    total_count: int = Field(..., description="Total matching atoms")
    processing_time_ms: float = Field(..., description="Processing time")


__all__ = [
    "AtomResponse",
    "LineageNode",
    "LineageResponse",
    "SearchRequest",
    "SearchResult",
    "SearchResponse",
]
