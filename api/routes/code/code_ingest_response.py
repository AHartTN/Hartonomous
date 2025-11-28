"""Code ingest response model."""

from pydantic import BaseModel


class CodeIngestResponse(BaseModel):
    """Response model for code ingestion."""

    success: bool
    total_atoms: int
    unique_atoms: int
    compositions: int
    relations: int
    message: str
