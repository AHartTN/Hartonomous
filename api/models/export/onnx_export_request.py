"""ONNX export request model."""

from typing import List, Optional
from pydantic import BaseModel, Field


class OnnxExportRequest(BaseModel):
    """Request model for ONNX export."""

    atom_ids: List[int] = Field(
        ...,
        min_length=1,
        max_length=100000,
        description="Atom IDs to include in exported model",
    )

    model_name: str = Field(
        ..., min_length=1, max_length=100, description="Model name for ONNX file"
    )

    output_path: Optional[str] = Field(
        default=None,
        description="Output file path (optional, defaults to /tmp/{model_name}.onnx)",
    )
