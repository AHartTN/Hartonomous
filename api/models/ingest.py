"""
Pydantic models for ingest requests/responses.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

from typing import Optional, Dict, Any
from pydantic import BaseModel, Field, field_validator


class TextIngestRequest(BaseModel):
    """Request model for text ingestion."""
    
    text: str = Field(
        ...,
        min_length=1,
        max_length=1_000_000,
        description="Text content to atomize"
    )
    
    metadata: Optional[Dict[str, Any]] = Field(
        default=None,
        description="Optional metadata (JSON)"
    )
    
    @field_validator("text")
    @classmethod
    def validate_text(cls, v: str) -> str:
        """Ensure text is not empty after stripping."""
        if not v.strip():
            raise ValueError("Text cannot be empty")
        return v


class ImageIngestRequest(BaseModel):
    """Request model for image ingestion."""
    
    image_data: str = Field(
        ...,
        description="Base64-encoded image data"
    )
    
    width: int = Field(..., gt=0, le=10000, description="Image width in pixels")
    height: int = Field(..., gt=0, le=10000, description="Image height in pixels")
    
    metadata: Optional[Dict[str, Any]] = Field(
        default=None,
        description="Optional metadata (JSON)"
    )
    
    @field_validator("image_data")
    @classmethod
    def validate_base64(cls, v: str) -> str:
        """Validate base64 encoding."""
        import base64
        try:
            base64.b64decode(v)
            return v
        except Exception as e:
            raise ValueError(f"Invalid base64 encoding: {e}")


class AudioIngestRequest(BaseModel):
    """Request model for audio ingestion."""
    
    audio_data: str = Field(
        ...,
        description="Base64-encoded audio data"
    )
    
    sample_rate: int = Field(
        ...,
        gt=0,
        le=192000,
        description="Audio sample rate (Hz)"
    )
    
    channels: int = Field(
        default=1,
        ge=1,
        le=8,
        description="Number of audio channels"
    )
    
    metadata: Optional[Dict[str, Any]] = Field(
        default=None,
        description="Optional metadata (JSON)"
    )


class IngestResponse(BaseModel):
    """Response model for successful ingestion."""
    
    status: str = Field(default="success", description="Operation status")
    
    atom_count: int = Field(..., description="Number of atoms created")
    
    root_atom_id: Optional[int] = Field(
        default=None,
        description="Root atom ID (for compositions)"
    )
    
    processing_time_ms: float = Field(
        ...,
        description="Processing time in milliseconds"
    )
    
    message: str = Field(
        default="Successfully atomized content",
        description="Human-readable message"
    )


class ErrorResponse(BaseModel):
    """Error response model."""
    
    status: str = Field(default="error", description="Status")
    error: str = Field(..., description="Error message")
    detail: Optional[str] = Field(default=None, description="Detailed error info")


__all__ = [
    "TextIngestRequest",
    "ImageIngestRequest",
    "AudioIngestRequest",
    "IngestResponse",
    "ErrorResponse",
]
