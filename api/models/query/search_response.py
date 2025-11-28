"""Search response model."""

from typing import List

from pydantic import BaseModel, Field

from .search_result import SearchResult


class SearchResponse(BaseModel):
    """Response model for search query."""

    query: str = Field(..., description="Original query")
    results: List[SearchResult] = Field(..., description="Search results")
    total_count: int = Field(..., description="Total matching atoms")
    processing_time_ms: float = Field(..., description="Processing time")
