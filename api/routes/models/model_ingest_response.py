"""Model ingest response model."""

from pydantic import BaseModel


class ModelIngestResponse(BaseModel):
    """Response model for model ingestion."""

    success: bool
    model_name: str
    model_format: str
    file_size_gb: float
    tensors_processed: int
    total_weights: int
    total_atoms: int
    unique_atoms: int
    deduplication_ratio: float
    message: str
