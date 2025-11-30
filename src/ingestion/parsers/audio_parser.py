"""
Audio parser - handles audio with sample-level atomization.
"""

import hashlib
from pathlib import Path
from typing import Any, Dict

import numpy as np

from src.core.atomization import BaseAtomizer


class AudioParser(BaseAtomizer):
    """Parse and atomize audio via SQL functions."""

    async def parse(self, audio_path: Path, conn) -> int:
        """
        Parse audio file into atoms.

        Process:
        1. Load audio via soundfile/librosa
        2. Convert to float32 samples
        3. Create parent atom for audio
        4. Chunk samples and atomize via SQL
        5. Build composition with sequence_index

        Returns parent atom_id.
        """
        try:
            import soundfile as sf
        except ImportError:
            try:
                import librosa

                audio_data, sample_rate = librosa.load(audio_path, sr=None, mono=False)
            except ImportError:
                raise ImportError(
                    "Install soundfile or librosa: pip install soundfile librosa"
                )
        else:
            audio_data, sample_rate = sf.read(audio_path, dtype="float32")

        # Ensure 1D (mono) or flatten to mono
        if audio_data.ndim > 1:
            audio_data = audio_data.mean(axis=1)

        audio_data = audio_data.astype(np.float32)

        # Create parent atom
        audio_hash = hashlib.sha256(audio_data.tobytes()).digest()
        parent_atom_id = await self.create_atom(
            conn,
            audio_hash,
            str(audio_path.name),
            {
                "modality": "audio",
                "sample_rate": int(sample_rate),
                "duration_seconds": float(len(audio_data) / sample_rate),
                "num_samples": len(audio_data),
                "file_path": str(audio_path),
            },
        )

        # Chunk samples (12 samples = 48 bytes at float32)
        chunk_size = 12

        self.stats["total_processed"] = len(audio_data)

        # Collect all non-silent chunks first
        component_ids = []
        sequence_indices = []
        
        for idx in range(0, len(audio_data), chunk_size):
            chunk = audio_data[idx : idx + chunk_size]

            # Sparse: skip silence
            if np.abs(chunk).max() < self.threshold:
                self.stats["sparse_skipped"] += 1
                continue

            chunk_bytes = chunk.tobytes()

            component_id = await self.create_atom(
                conn, chunk_bytes, None, {"dtype": "float32", "audio_chunk": True}
            )
            component_ids.append(component_id)
            sequence_indices.append(idx // chunk_size)
            self.stats["atoms_created"] += 1

        # Batch create all compositions at once
        if component_ids:
            await self.create_compositions_batch(
                conn, parent_atom_id, component_ids, sequence_indices
            )

        return parent_atom_id
