"""
Video parser - handles video with frame-level atomization.
"""

import hashlib
from pathlib import Path
from typing import Any, Dict

from ...core.atomization import BaseAtomizer


class VideoParser(BaseAtomizer):
    """Parse and atomize video via SQL functions."""

    async def parse(self, video_path: Path, conn) -> int:
        """
        Parse video file into atoms.

        Process:
        1. Extract frames via opencv/ffmpeg
        2. For each frame, call ImageParser
        3. Create parent atom for video
        4. Link frame atoms as components

        Returns parent atom_id.
        """
        try:
            import cv2
        except ImportError:
            raise ImportError("Install opencv: pip install opencv-python")

        # Create parent atom
        video_hash = hashlib.sha256(str(video_path).encode()).digest()

        # Open video and get metadata
        cap = cv2.VideoCapture(str(video_path))
        fps = cap.get(cv2.CAP_PROP_FPS)
        frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

        parent_atom_id = await self.create_atom(
            conn,
            video_hash,
            str(video_path.name),
            {
                "modality": "video",
                "fps": float(fps),
                "frame_count": frame_count,
                "width": width,
                "height": height,
                "duration_seconds": float(frame_count / fps) if fps > 0 else 0,
                "file_path": str(video_path),
            },
        )

        # Sample frames (every Nth frame to avoid explosion)
        frame_skip = max(1, int(fps))  # 1 frame per second
        frame_idx = 0
        composition_idx = 0

        while True:
            ret, frame = cap.read()
            if not ret:
                break

            if frame_idx % frame_skip == 0:
                # Atomize frame (BGR to RGB, normalize)
                import numpy as np

                frame_rgb = (
                    cv2.cvtColor(frame, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
                )
                frame_hash = hashlib.sha256(frame_rgb.tobytes()).digest()

                frame_atom_id = await self.create_atom(
                    conn,
                    frame_hash,
                    None,
                    {
                        "modality": "image",
                        "frame_number": frame_idx,
                        "timestamp": float(frame_idx / fps) if fps > 0 else 0,
                        "width": width,
                        "height": height,
                    },
                )

                frame_atom_ids.append(frame_atom_id)
                composition_idx += 1
                self.stats["atoms_created"] += 1

            frame_idx += 1
            self.stats["total_processed"] += 1

        cap.release()

        # Batch create all compositions at once
        if frame_atom_ids:
            await self.create_compositions_batch(
                conn, parent_atom_id, frame_atom_ids, list(range(len(frame_atom_ids)))
            )

        return parent_atom_id
