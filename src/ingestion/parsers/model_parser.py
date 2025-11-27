"""
AI Model parser - extracts weights, activations, and model structure.
Supports PyTorch, TensorFlow, ONNX, and other formats.
"""

import numpy as np
from typing import Dict, List, Any, Optional, Iterator
from pathlib import Path

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark_projection import LandmarkProjector


class ModelParser:
    """Parse and atomize AI models."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_formats = ['.pt', '.pth', '.onnx', '.pb', '.h5', '.safetensors']
    
    def parse_pytorch_model(self, model_path: Path) -> Iterator[Dict[str, Any]]:
        """Parse PyTorch model file, yielding atomic records."""
        import torch
        
        checkpoint = torch.load(model_path, map_location='cpu')
        state_dict = checkpoint.get('state_dict', checkpoint) if isinstance(checkpoint, dict) else checkpoint
        
        for layer_name, param_tensor in state_dict.items():
            param_np = param_tensor.detach().cpu().numpy()
            
            modality = ModalityType.MODEL_BIAS if 'bias' in layer_name else ModalityType.MODEL_WEIGHT
            atoms = self.atomizer.atomize_array(param_np, modality)
            landmarks = self.landmark_projector.extract_model_landmarks(param_np, layer_name)
            
            for atom in atoms:
                for landmark in landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'layer_name': layer_name,
                        'param_shape': param_np.shape,
                        'model_path': str(model_path)
                    }
    
    def parse(self, model_path: Path) -> Iterator[Dict[str, Any]]:
        """Auto-detect format and parse model."""
        suffix = model_path.suffix.lower()
        
        if suffix in ['.pt', '.pth']:
            yield from self.parse_pytorch_model(model_path)
        else:
            raise ValueError(f"Unsupported model format: {suffix}")
