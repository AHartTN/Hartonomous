"""Model ingest request model."""

from typing import Optional
from pydantic import BaseModel, Field


class ModelIngestRequest(BaseModel):
    """Request model for AI model ingestion."""

    model_path: str = Field(..., description="Path to model file on server")
    model_name: str = Field(..., description="Human-readable model name")
    model_format: str = Field(..., description="Model format: gguf, safetensors, pytorch, onnx")
    threshold: float = Field(default=0.01, description="Sparse encoding threshold")
    max_tensors: Optional[int] = Field(default=None, description="Max tensors (for testing)")
