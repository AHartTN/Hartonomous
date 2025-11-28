"""Code ingest request model."""

from typing import Optional
from pydantic import BaseModel, Field


class CodeIngestRequest(BaseModel):
    """Request model for code ingestion."""

    code: str = Field(..., description="Source code to atomize")
    filename: str = Field(
        default="code.txt", description="Filename (determines language)"
    )
    language: str = Field(default="csharp", description="Programming language")
    metadata: Optional[dict] = Field(
        default=None, description="Optional metadata (JSON)"
    )
