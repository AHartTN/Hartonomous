"""Text ingest request model."""

from typing import Any, Dict, Optional
from pydantic import BaseModel, Field, field_validator


class TextIngestRequest(BaseModel):
    """Request model for text ingestion."""

    text: str = Field(
        ..., min_length=1, max_length=1_000_000, description="Text content to atomize"
    )

    metadata: Optional[Dict[str, Any]] = Field(
        default=None, description="Optional metadata (JSON)"
    )

    @field_validator("text")
    @classmethod
    def validate_text(cls, v: str) -> str:
        """Ensure text is not empty after stripping."""
        if not v.strip():
            raise ValueError("Text cannot be empty")
        return v
