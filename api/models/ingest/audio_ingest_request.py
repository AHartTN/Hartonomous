"""Audio ingest request model."""

from typing import Any, Dict, Optional
from pydantic import BaseModel, Field


class AudioIngestRequest(BaseModel):
    """Request model for audio ingestion."""

    audio_data: str = Field(..., description="Base64-encoded audio data")

    sample_rate: int = Field(..., gt=0, le=192000, description="Audio sample rate (Hz)")

    channels: int = Field(default=1, ge=1, le=8, description="Number of audio channels")

    metadata: Optional[Dict[str, Any]] = Field(
        default=None, description="Optional metadata (JSON)"
    )
