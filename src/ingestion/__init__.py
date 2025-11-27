"""Ingestion pipeline - handles all data ingestion."""

from .parsers import ModelParser, ImageParser

__all__ = ['ModelParser', 'ImageParser']
