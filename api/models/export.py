"""
Pydantic models for export requests/responses.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import List, Optional
from pydantic import BaseModel, Field


class OnnxExportRequest(BaseModel):
    """Request model for ONNX export."""
    
    atom_ids: List[int] = Field(
        ...,
        min_length=1,
        max_length=100000,
        description="Atom IDs to include in exported model"
    )
    
    model_name: str = Field(
        ...,
        min_length=1,
        max_length=100,
        description="Model name for ONNX file"
    )
    
    output_path: Optional[str] = Field(
        default=None,
        description="Output file path (optional, defaults to /tmp/{model_name}.onnx)"
    )


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
        default="Model exported successfully",
        description="Human-readable message"
    )


__all__ = [
    "OnnxExportRequest",
    "OnnxExportResponse",
]
