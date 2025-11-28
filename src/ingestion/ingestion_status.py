"""
Ingestion status enumeration.
"""

from enum import Enum


class IngestionStatus(Enum):
    """Status of ingestion operation."""

    PENDING = "pending"
    PROCESSING = "processing"
    COMPLETED = "completed"
    FAILED = "failed"
