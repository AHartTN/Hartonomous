"""
Ingestion coordinator - orchestrates parsing, atomization, and storage.
Main entry point for ingesting data into the system.
"""

import asyncio
import logging
import os
from pathlib import Path
from typing import Any, Dict, List, Optional

from ..db.ingestion_db import IngestionDB
from .ingestion_result import IngestionResult
from .ingestion_status import IngestionStatus
from .parsers import (
    AudioParser,
    CodeParser,
    ImageParser,
    ModelParser,
    StructuredParser,
    TextParser,
    VideoParser,
)

logger = logging.getLogger(__name__)


class IngestionCoordinator:
    """
    Main coordinator for data ingestion.
    Handles all modalities and orchestrates the full pipeline.
    """

    def __init__(self, db: IngestionDB):
        self.db = db

        # Get Code Atomizer URL from environment
        code_atomizer_url = os.getenv("CODE_ATOMIZER_URL", "http://localhost:8001")

        # Initialize parsers
        self.text_parser = TextParser()
        self.image_parser = ImageParser()
        self.audio_parser = AudioParser()
        self.video_parser = VideoParser()
        self.code_parser = CodeParser(atomizer_service_url=code_atomizer_url)
        self.model_parser = ModelParser()
        self.structured_parser = StructuredParser()

        # File extension to parser mapping
        self.parser_map = self._build_parser_map()

    def _build_parser_map(self) -> Dict[str, Any]:
        """Build mapping from file extensions to parsers."""
        mapping = {}

        # Text
        for ext in self.text_parser.supported_formats:
            mapping[ext] = self.text_parser

        # Images
        for ext in [".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp"]:
            mapping[ext] = self.image_parser

        # Audio
        for ext in self.audio_parser.supported_formats:
            mapping[ext] = self.audio_parser

        # Video
        for ext in self.video_parser.supported_formats:
            mapping[ext] = self.video_parser

        # Code
        for ext in [
            ".py",
            ".js",
            ".ts",
            ".java",
            ".cpp",
            ".c",
            ".cs",
            ".go",
            ".rs",
            ".rb",
            ".php",
        ]:
            mapping[ext] = self.code_parser

        # Models
        for ext in self.model_parser.supported_formats:
            mapping[ext] = self.model_parser

        # Structured
        for ext in self.structured_parser.supported_formats:
            mapping[ext] = self.structured_parser

        return mapping

    def _detect_parser(self, file_path: Path) -> Optional[Any]:
        """Detect appropriate parser for file."""
        ext = file_path.suffix.lower()
        return self.parser_map.get(ext)

    async def ingest_file(
        self,
        file_path: Path,
        source_id: Optional[str] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> IngestionResult:
        """
        Ingest a single file.

        Args:
            file_path: Path to file to ingest
            source_id: Optional source identifier
            metadata: Optional metadata dict

        Returns:
            IngestionResult with statistics
        """
        if source_id is None:
            source_id = str(file_path)

        logger.info(f"Starting ingestion of {file_path}")

        try:
            # Detect parser
            parser = self._detect_parser(file_path)
            if parser is None:
                return IngestionResult(
                    source_id=source_id,
                    status=IngestionStatus.FAILED,
                    atoms_created=0,
                    compositions_created=0,
                    relations_created=0,
                    error=f"No parser found for {file_path.suffix}",
                    metadata=metadata,
                )
            # Parse file
            atoms_created = 0
            compositions_created = 0
            relations_created = 0

            for atom in parser.parse(file_path):
                # Store atom (atomize_value handles deduplication)
                atom_id = await self.db.store_atom(atom)
                atoms_created += 1

                # Compute and set spatial position
                # SQL function handles modality-aware positioning
                async with self.db.pool.connection() as conn:
                    async with conn.cursor() as cur:
                        await cur.execute(
                            """
                            UPDATE atom 
                            SET spatial_key = compute_spatial_position(atom_id)
                            WHERE atom_id = %s AND spatial_key IS NULL
                        """,
                            (atom_id,),
                        )

                # Log progress periodically
                if atoms_created % 1000 == 0:
                    logger.info(f"Progress: {atoms_created} atoms")

            logger.info(f"Completed ingestion of {file_path}: {atoms_created} atoms")

            return IngestionResult(
                source_id=source_id,
                status=IngestionStatus.COMPLETED,
                atoms_created=atoms_created,
                compositions_created=compositions_created,
                relations_created=relations_created,
                metadata=metadata,
            )

        except Exception as e:
            logger.error(f"Failed to ingest {file_path}: {e}", exc_info=True)
            return IngestionResult(
                source_id=source_id,
                status=IngestionStatus.FAILED,
                atoms_created=0,
                compositions_created=0,
                relations_created=0,
                error=str(e),
                metadata=metadata,
            )

    async def ingest_directory(
        self, directory: Path, recursive: bool = True, file_pattern: str = "*"
    ) -> List[IngestionResult]:
        """
        Ingest all files in a directory.

        Args:
            directory: Directory to ingest
            recursive: Whether to recurse into subdirectories
            file_pattern: Glob pattern for files to include

        Returns:
            List of IngestionResult for each file
        """
        logger.info(f"Starting directory ingestion: {directory}")

        # Find all matching files
        if recursive:
            files = list(directory.rglob(file_pattern))
        else:
            files = list(directory.glob(file_pattern))

        # Filter to only supported files
        supported_files = [
            f for f in files if f.is_file() and self._detect_parser(f) is not None
        ]

        logger.info(f"Found {len(supported_files)} supported files")

        # Ingest each file
        results = []
        for file_path in supported_files:
            result = await self.ingest_file(file_path)
            results.append(result)

        # Summary
        total_atoms = sum(r.atoms_created for r in results)
        total_landmarks = sum(r.landmarks_created for r in results)
        successful = sum(1 for r in results if r.status == IngestionStatus.COMPLETED)
        failed = sum(1 for r in results if r.status == IngestionStatus.FAILED)

        logger.info(
            f"Directory ingestion complete: {successful} succeeded, {failed} failed. "
            f"Total: {total_atoms} atoms, {total_landmarks} landmarks"
        )

        return results

    async def ingest_batch(self, items: List[Dict[str, Any]]) -> List[IngestionResult]:
        """
        Ingest a batch of items in parallel.

        Args:
            items: List of dicts with 'path' and optional 'source_id', 'metadata'

        Returns:
            List of IngestionResult
        """
        tasks = []
        for item in items:
            path = Path(item["path"])
            source_id = item.get("source_id")
            metadata = item.get("metadata")

            task = self.ingest_file(path, source_id, metadata)
            tasks.append(task)

        results = await asyncio.gather(*tasks, return_exceptions=True)

        # Convert exceptions to failed results
        final_results = []
        for i, result in enumerate(results):
            if isinstance(result, Exception):
                final_results.append(
                    IngestionResult(
                        source_id=items[i].get("source_id", items[i]["path"]),
                        status=IngestionStatus.FAILED,
                        atoms_created=0,
                        landmarks_created=0,
                        associations_created=0,
                        error=str(result),
                    )
                )
            else:
                final_results.append(result)

        return final_results
