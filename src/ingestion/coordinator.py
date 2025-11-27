"""
Ingestion coordinator - orchestrates parsing, atomization, and storage.
Main entry point for ingesting data into the system.
"""

import asyncio
import logging
from typing import Dict, Any, List, Optional, Iterator
from pathlib import Path
from dataclasses import dataclass
from enum import Enum

from ..core.atomization import Atom
from ..core.landmark_projection import Landmark
from ..db.ingestion import IngestionDB
from .parsers import (
    TextParser, ImageParser, AudioParser, VideoParser,
    CodeParser, ModelParser, StructuredParser
)


logger = logging.getLogger(__name__)


class IngestionStatus(Enum):
    """Status of ingestion operation."""
    PENDING = "pending"
    PROCESSING = "processing"
    COMPLETED = "completed"
    FAILED = "failed"


@dataclass
class IngestionResult:
    """Result of ingestion operation."""
    source_id: str
    status: IngestionStatus
    atoms_created: int
    landmarks_created: int
    associations_created: int
    error: Optional[str] = None
    metadata: Dict[str, Any] = None


class IngestionCoordinator:
    """
    Main coordinator for data ingestion.
    Handles all modalities and orchestrates the full pipeline.
    """
    
    def __init__(self, db: IngestionDB):
        self.db = db
        
        # Initialize parsers
        self.text_parser = TextParser()
        self.image_parser = ImageParser()
        self.audio_parser = AudioParser()
        self.video_parser = VideoParser()
        self.code_parser = CodeParser()
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
        for ext in ['.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.webp']:
            mapping[ext] = self.image_parser
        
        # Audio
        for ext in self.audio_parser.supported_formats:
            mapping[ext] = self.audio_parser
        
        # Video
        for ext in self.video_parser.supported_formats:
            mapping[ext] = self.video_parser
        
        # Code
        for ext in ['.py', '.js', '.ts', '.java', '.cpp', '.c', '.cs', '.go', '.rs', '.rb', '.php']:
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
        metadata: Optional[Dict[str, Any]] = None
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
                    landmarks_created=0,
                    associations_created=0,
                    error=f"No parser found for {file_path.suffix}",
                    metadata=metadata
                )
            
            # Parse file
            atoms_created = 0
            landmarks_created = 0
            associations_created = 0
            
            for record in parser.parse(file_path):
                atom = record['atom']
                landmark = record['landmark']
                
                # Store atom
                await self.db.store_atom(atom)
                atoms_created += 1
                
                # Store landmark
                await self.db.store_landmark(landmark)
                landmarks_created += 1
                
                # Create association
                await self.db.create_association(atom.atom_id, landmark.landmark_id)
                associations_created += 1
                
                # Log progress periodically
                if atoms_created % 1000 == 0:
                    logger.info(f"Progress: {atoms_created} atoms, {landmarks_created} landmarks")
            
            logger.info(f"Completed ingestion of {file_path}: "
                       f"{atoms_created} atoms, {landmarks_created} landmarks")
            
            return IngestionResult(
                source_id=source_id,
                status=IngestionStatus.COMPLETED,
                atoms_created=atoms_created,
                landmarks_created=landmarks_created,
                associations_created=associations_created,
                metadata=metadata
            )
        
        except Exception as e:
            logger.error(f"Failed to ingest {file_path}: {e}", exc_info=True)
            return IngestionResult(
                source_id=source_id,
                status=IngestionStatus.FAILED,
                atoms_created=0,
                landmarks_created=0,
                associations_created=0,
                error=str(e),
                metadata=metadata
            )
    
    async def ingest_directory(
        self,
        directory: Path,
        recursive: bool = True,
        file_pattern: str = "*"
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
        supported_files = [f for f in files if f.is_file() and self._detect_parser(f) is not None]
        
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
        
        logger.info(f"Directory ingestion complete: {successful} succeeded, {failed} failed. "
                   f"Total: {total_atoms} atoms, {total_landmarks} landmarks")
        
        return results
    
    async def ingest_batch(
        self,
        items: List[Dict[str, Any]]
    ) -> List[IngestionResult]:
        """
        Ingest a batch of items in parallel.
        
        Args:
            items: List of dicts with 'path' and optional 'source_id', 'metadata'
        
        Returns:
            List of IngestionResult
        """
        tasks = []
        for item in items:
            path = Path(item['path'])
            source_id = item.get('source_id')
            metadata = item.get('metadata')
            
            task = self.ingest_file(path, source_id, metadata)
            tasks.append(task)
        
        results = await asyncio.gather(*tasks, return_exceptions=True)
        
        # Convert exceptions to failed results
        final_results = []
        for i, result in enumerate(results):
            if isinstance(result, Exception):
                final_results.append(IngestionResult(
                    source_id=items[i].get('source_id', items[i]['path']),
                    status=IngestionStatus.FAILED,
                    atoms_created=0,
                    landmarks_created=0,
                    associations_created=0,
                    error=str(result)
                ))
            else:
                final_results.append(result)
        
        return final_results
