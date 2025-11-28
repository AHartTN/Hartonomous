"""
Ingestion result dataclass.
"""

from dataclasses import dataclass
from typing import Any, Dict, Optional

from .ingestion_status import IngestionStatus


@dataclass
class IngestionResult:
    """Result of ingestion operation."""

    source_id: str
    status: IngestionStatus
    atoms_created: int
    compositions_created: int
    relations_created: int
    error: Optional[str] = None
    metadata: Dict[str, Any] = None
