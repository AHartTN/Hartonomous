"""Ingestion writer - alias for IngestionDB."""

from .ingestion_db import IngestionDB


class IngestionWriter(IngestionDB):
    """Alias for backwards compatibility."""

    pass
