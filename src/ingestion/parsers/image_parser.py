"""Image parser - handles images with pixel-level atomization."""

import numpy as np
from typing import Dict, Any, Iterator
from pathlib import Path
from PIL import Image

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark import LandmarkProjector


class ImageParser:
    """Parse and atomize images."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
    
    def parse(self, image_path: Path) -> Iterator[Dict[str, Any]]:
        """Parse image file into atoms."""
        img = Image.open(image_path)
        img_array = np.array(img)
        
        if img_array.dtype != np.float64:
            img_array = img_array.astype(np.float64) / 255.0
        
        atoms = self.atomizer.atomize_array(img_array, ModalityType.IMAGE_PIXEL)
        landmarks = self.landmark_projector.extract_image_landmarks(img_array)
        
        for atom in atoms:
            for landmark in landmarks:
                yield {
                    'atom': atom,
                    'landmark': landmark,
                    'image_shape': img_array.shape,
                    'image_path': str(image_path)
                }
