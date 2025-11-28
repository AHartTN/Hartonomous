"""Ingest response model."""

from typing import Optional

from pydantic import BaseModel, Field


class IngestResponse(BaseModel):
    """Response model for successful ingestion."""

    status: str = Field(default="success", description="Operation status")

    atom_count: int = Field(..., description="Number of atoms created")

    root_atom_id: Optional[int] = Field(
        default=None, description="Root atom ID (for compositions)"
    )

    processing_time_ms: float = Field(
        ..., description="Processing time in milliseconds"
    )

    message: str = Field(
        default="Successfully atomized content", description="Human-readable message"
    )
