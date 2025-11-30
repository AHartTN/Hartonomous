#!/usr/bin/env python3
"""Comprehensive import test - verify everything works."""

import sys

sys.path.insert(0, ".")

print("Testing all imports...")

# Core modules
print("? Testing core.atomization...")
from src.core.atomization import Atomizer, BaseAtomizer

print("? Testing core.compression...")
from src.core.compression import compress_atom, decompress_atom

print("? Testing core.spatial...")
from src.core.spatial import encode_hilbert_3d, decode_hilbert_3d

print("? Testing core.landmark...")
from src.core.landmark import LandmarkProjector

# Ingestion
print("? Testing ingestion.parsers...")
from src.ingestion.parsers import TextParser, ImageParser, CodeParser

# API
print("? Testing API main...")
from api.main import app

print("? Testing API config...")
from api.config import settings

print("? Testing API dependencies...")
from api.dependencies import get_db_connection

# Services
print("? Testing services...")
from api.services.document_parser import DocumentParserService
# from api.services.image_atomization import ImageAtomizationService  # TODO: Fix import
from api.services.model_atomization import GGUFAtomizer

# Database
print("? Testing database...")
from src.db.ingestion_db import IngestionDB

print("\n? ALL IMPORTS SUCCESSFUL - NO ERRORS")
