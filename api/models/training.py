"""
Pydantic models for training requests/responses.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field


class TrainingSample(BaseModel):
    """Single training sample."""
    
    input_atom_ids: List[int] = Field(
        ...,
        min_length=1,
        max_length=1000,
        description="Input atom IDs (sequence)"
    )
    
    target_atom_id: int = Field(
        ...,
        description="Target atom ID (expected output)"
    )


class BatchTrainRequest(BaseModel):
    """Request model for batch training."""
    
    samples: List[TrainingSample] = Field(
        ...,
        min_length=1,
        max_length=10000,
        description="Training samples"
    )
    
    learning_rate: float = Field(
        default=0.01,
        gt=0.0,
        le=1.0,
        description="Learning rate (SGD)"
    )
    
    epochs: int = Field(
        default=1,
        ge=1,
        le=100,
        description="Number of training epochs"
    )


class TrainingSampleResult(BaseModel):
    """Result for single training sample."""
    
    sample_index: int = Field(..., description="Sample index in batch")
    loss: float = Field(..., description="Loss value")
    converged: bool = Field(..., description="Whether loss < threshold")


class BatchTrainResponse(BaseModel):
    """Response model for batch training."""
    
    status: str = Field(default="success", description="Training status")
    
    total_samples: int = Field(..., description="Total samples processed")
    
    epochs_completed: int = Field(..., description="Epochs completed")
    
    average_loss: float = Field(..., description="Average loss across all samples")
    
    min_loss: float = Field(..., description="Minimum loss")
    
    max_loss: float = Field(..., description="Maximum loss")
    
    convergence_rate: float = Field(
        ...,
        description="Percentage of samples converged (0-1)"
    )
    
    processing_time_ms: float = Field(
        ...,
        description="Total processing time"
    )
    
    samples_per_second: float = Field(
        ...,
        description="Training throughput"
    )
    
    message: str = Field(
        default="Training complete",
        description="Human-readable message"
    )


__all__ = [
    "TrainingSample",
    "BatchTrainRequest",
    "TrainingSampleResult",
    "BatchTrainResponse",
]
