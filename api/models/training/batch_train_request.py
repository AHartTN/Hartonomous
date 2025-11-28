"""Batch train request model."""

from typing import List

from pydantic import BaseModel, Field

from .training_sample import TrainingSample


class BatchTrainRequest(BaseModel):
    """Request model for batch training."""

    samples: List[TrainingSample] = Field(
        ..., min_length=1, max_length=10000, description="Training samples"
    )

    learning_rate: float = Field(
        default=0.01, gt=0.0, le=1.0, description="Learning rate (SGD)"
    )

    epochs: int = Field(
        default=1, ge=1, le=100, description="Number of training epochs"
    )
