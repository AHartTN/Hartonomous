"""ONNX export response model."""

from pydantic import BaseModel, Field


class OnnxExportResponse(BaseModel):
    """Response model for ONNX export."""

    status: str = Field(default="success", description="Export status")

    model_name: str = Field(..., description="Exported model name")

    output_path: str = Field(..., description="Output file path")

    atom_count: int = Field(..., description="Number of atoms exported")

    relation_count: int = Field(..., description="Number of relations exported")

    file_size_bytes: int = Field(..., description="Output file size")

    processing_time_ms: float = Field(..., description="Export processing time")

    message: str = Field(
        default="Model exported successfully", description="Human-readable message"
    )
