"""Training sample model."""

from typing import List

from pydantic import BaseModel, Field


class TrainingSample(BaseModel):
    """Single training sample."""

    input_atom_ids: List[int] = Field(
        ..., min_length=1, max_length=1000, description="Input atom IDs (sequence)"
    )

    target_atom_id: int = Field(..., description="Target atom ID (expected output)")
