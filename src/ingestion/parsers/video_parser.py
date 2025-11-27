"""
Video parser - handles video files, frame extraction, and temporal features.
Supports MP4, AVI, MOV, and other formats.
"""

import numpy as np
from typing import Dict, Any, Iterator, Optional, Tuple
from pathlib import Path

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark_projection import LandmarkProjector


class VideoParser:
    """Parse and atomize video files."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_formats = ['.mp4', '.avi', '.mov', '.mkv', '.webm']
    
    def _extract_frames(
        self,
        video_path: Path,
        frame_skip: int = 30
    ) -> Iterator[Tuple[int, np.ndarray]]:
        """Extract frames from video."""
        try:
            import cv2
            
            cap = cv2.VideoCapture(str(video_path))
            frame_idx = 0
            
            while True:
                ret, frame = cap.read()
                if not ret:
                    break
                
                if frame_idx % frame_skip == 0:
                    # Convert BGR to RGB
                    frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                    # Normalize to float64
                    frame_normalized = frame_rgb.astype(np.float64) / 255.0
                    yield frame_idx, frame_normalized
                
                frame_idx += 1
            
            cap.release()
        
        except ImportError:
            raise ImportError("opencv-python required for video parsing: pip install opencv-python")
    
    def _extract_optical_flow(
        self,
        frame1: np.ndarray,
        frame2: np.ndarray
    ) -> np.ndarray:
        """Calculate optical flow between frames."""
        import cv2
        
        # Convert to grayscale
        gray1 = cv2.cvtColor((frame1 * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY)
        gray2 = cv2.cvtColor((frame2 * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY)
        
        # Calculate optical flow
        flow = cv2.calcOpticalFlowFarneback(
            gray1, gray2, None,
            pyr_scale=0.5, levels=3, winsize=15,
            iterations=3, poly_n=5, poly_sigma=1.2, flags=0
        )
        
        return flow.astype(np.float64)
    
    def parse(
        self,
        video_path: Path,
        frame_skip: int = 30,
        extract_flow: bool = True
    ) -> Iterator[Dict[str, Any]]:
        """
        Parse video file into atoms.
        
        Args:
            video_path: Path to video file
            frame_skip: Process every Nth frame
            extract_flow: Whether to extract optical flow features
        """
        prev_frame = None
        
        for frame_idx, frame in self._extract_frames(video_path, frame_skip):
            # Atomize frame as image
            frame_atoms = self.atomizer.atomize_array(frame, ModalityType.VIDEO_FRAME)
            frame_landmarks = self.landmark_projector.extract_image_landmarks(frame)
            
            # Yield frame atoms
            for atom in frame_atoms:
                for landmark in frame_landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'video_path': str(video_path),
                        'frame_index': frame_idx,
                        'feature_type': 'frame',
                        'frame_shape': frame.shape
                    }
            
            # Extract optical flow if requested
            if extract_flow and prev_frame is not None:
                flow = self._extract_optical_flow(prev_frame, frame)
                flow_atoms = self.atomizer.atomize_array(flow, ModalityType.VIDEO_FRAME)
                flow_landmarks = self.landmark_projector.extract_image_landmarks(flow)
                
                for atom in flow_atoms:
                    for landmark in flow_landmarks:
                        yield {
                            'atom': atom,
                            'landmark': landmark,
                            'video_path': str(video_path),
                            'frame_index': frame_idx,
                            'feature_type': 'optical_flow',
                            'frame_shape': flow.shape
                        }
            
            prev_frame = frame
