"""Text parser - handles text with character/word-level atomization using geometric pipeline."""

from pathlib import Path
from typing import Any, Dict, List

from psycopg import AsyncConnection

from api.services.geometric_atomization.base_geometric_parser import BaseGeometricParser


class TextParser(BaseGeometricParser):
    """Parse and atomize text using geometric/fractal pipeline."""

    async def chunk_data(self, stream: Any, modality: str) -> List[bytes]:
        """
        Chunk text into character-level bytes.

        Args:
            stream: Path to text file or text string
            modality: Should be "text"

        Returns:
            List of single-character byte chunks
        """
        # Handle Path or string input
        if isinstance(stream, Path):
            with open(stream, "r", encoding="utf-8") as f:
                text = f.read()
        else:
            text = str(stream)

        # Chunk into character bytes
        chunks = [char.encode("utf-8") for char in text]

        return chunks

    async def parse(self, text_path: Path, conn: AsyncConnection) -> int:
        """
        Parse text file into trajectory using geometric pipeline.

        Process:
        1. Read text file
        2. Chunk into character bytes
        3. Crystallize sequence with fractal deduplication
        4. Build and save trajectory as single LINESTRING

        Returns trajectory atom_id.
        """
        # Read text
        with open(text_path, "r", encoding="utf-8") as f:
            text = f.read()

        # Prepare metadata
        metadata = {
            "modality": "text",
            "char_count": len(text),
            "word_count": len(text.split()),
            "file_path": str(text_path),
            "filename": text_path.name,
        }

        # Process through geometric pipeline
        trajectory_atom_id = await self.process_stream(
            stream=text_path, modality="text", conn=conn, metadata=metadata
        )

        return trajectory_atom_id
