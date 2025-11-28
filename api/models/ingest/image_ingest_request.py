"""Image ingest request model."""

import base64
from typing import Any, Dict, Optional
from pydantic import BaseModel, Field, field_validator


class ImageIngestRequest(BaseModel):
    """Request model for image ingestion."""

    image_data: str = Field(..., description="Base64-encoded image data")

    width: int = Field(..., gt=0, le=10000, description="Image width in pixels")
    height: int = Field(..., gt=0, le=10000, description="Image height in pixels")

    metadata: Optional[Dict[str, Any]] = Field(
        default=None, description="Optional metadata (JSON)"
    )

    @field_validator("image_data")
    @classmethod
    def validate_base64(cls, v: str) -> str:
        """Validate base64 encoding."""
        try:
            base64.b64decode(v)
            return v
        except Exception as e:
            raise ValueError(f"Invalid base64 encoding: {e}")
