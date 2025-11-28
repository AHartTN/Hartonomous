"""Ingestion pipeline - handles all data ingestion."""

from .parsers import ImageParser, ModelParser

__all__ = ["ModelParser", "ImageParser"]
