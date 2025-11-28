"""Training sample result model."""

from pydantic import BaseModel, Field


class TrainingSampleResult(BaseModel):
    """Result for single training sample."""

    sample_index: int = Field(..., description="Sample index in batch")
    loss: float = Field(..., description="Loss value")
    converged: bool = Field(..., description="Whether loss < threshold")
