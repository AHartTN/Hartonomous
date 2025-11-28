"""Batch train response model."""

from pydantic import BaseModel, Field


class BatchTrainResponse(BaseModel):
    """Response model for batch training."""

    status: str = Field(default="success", description="Training status")

    total_samples: int = Field(..., description="Total samples processed")

    epochs_completed: int = Field(..., description="Epochs completed")

    average_loss: float = Field(..., description="Average loss across all samples")

    min_loss: float = Field(..., description="Minimum loss")

    max_loss: float = Field(..., description="Maximum loss")

    convergence_rate: float = Field(
        ..., description="Percentage of samples converged (0-1)"
    )

    processing_time_ms: float = Field(..., description="Total processing time")

    samples_per_second: float = Field(..., description="Training throughput")

    message: str = Field(
        default="Training complete", description="Human-readable message"
    )
