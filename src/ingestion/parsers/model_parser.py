"""
Model parser - handles AI models with tensor-level atomization.
"""

import hashlib
from pathlib import Path
from typing import Dict, Any

from ...core.atomization import BaseAtomizer


class ModelParser(BaseAtomizer):
    """Parse and atomize AI models via SQL functions."""
    
    async def parse(self, model_path: Path, conn) -> int:
        """
        Parse model file into atoms.
        
        Routes to appropriate format handler:
        - .gguf -> GGUFAtomizer
        - .safetensors -> SafeTensorsAtomizer
        - .pt/.pth -> PyTorchAtomizer
        - .onnx -> ONNXAtomizer
        
        Returns parent atom_id.
        """
        format_type = self._detect_format(model_path.suffix)
        
        model_hash = hashlib.sha256(str(model_path).encode()).digest()
        
        parent_atom_id = await self.create_atom(
            conn,
            model_hash,
            str(model_path.name),
            {
                'modality': 'model',
                'format': format_type,
                'file_path': str(model_path),
                'size_bytes': model_path.stat().st_size
            }
        )
        
        # Route to format-specific handler
        if format_type == 'gguf':
            from api.services.model_atomization import GGUFAtomizer
            atomizer = GGUFAtomizer(threshold=self.threshold)
            result = await atomizer.atomize_model(model_path, model_path.name, conn)
            return result["model_atom_id"]
        
        elif format_type == 'safetensors':
            await self._parse_safetensors(model_path, parent_atom_id, conn)
        
        elif format_type in ['pytorch', 'pt']:
            await self._parse_pytorch(model_path, parent_atom_id, conn)
        
        elif format_type == 'onnx':
            await self._parse_onnx(model_path, parent_atom_id, conn)
        
        else:
            raise ValueError(f"Unsupported model format: {format_type}")
        
        return parent_atom_id
    
    async def _parse_safetensors(self, model_path: Path, parent_atom_id: int, conn):
        """Parse SafeTensors format."""
        try:
            from safetensors import safe_open
        except ImportError:
            raise ImportError("Install safetensors: pip install safetensors")
        
        with safe_open(model_path, framework="numpy") as f:
            for key in f.keys():
                tensor = f.get_tensor(key)
                
                # Atomize tensor similar to GGUF
                tensor_hash = hashlib.sha256(key.encode()).digest()
                tensor_atom_id = await self.create_atom(
                    conn,
                    tensor_hash,
                    key,
                    {
                        'modality': 'tensor',
                        'shape': list(tensor.shape),
                        'dtype': str(tensor.dtype)
                    }
                )
                
                await self.create_composition(conn, parent_atom_id, tensor_atom_id, len(self.cache))
                self.stats["atoms_created"] += 1
    
    async def _parse_pytorch(self, model_path: Path, parent_atom_id: int, conn):
        """Parse PyTorch checkpoint."""
        try:
            import torch
        except ImportError:
            raise ImportError("Install pytorch: pip install torch")
        
        checkpoint = torch.load(model_path, map_location='cpu')
        
        if isinstance(checkpoint, dict):
            state_dict = checkpoint.get('state_dict', checkpoint)
        else:
            state_dict = checkpoint
        
        for key, tensor in state_dict.items():
            tensor_hash = hashlib.sha256(key.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn,
                tensor_hash,
                key,
                {
                    'modality': 'tensor',
                    'shape': list(tensor.shape),
                    'dtype': str(tensor.dtype)
                }
            )
            
            await self.create_composition(conn, parent_atom_id, tensor_atom_id, len(self.cache))
            self.stats["atoms_created"] += 1
    
    async def _parse_onnx(self, model_path: Path, parent_atom_id: int, conn):
        """Parse ONNX model."""
        try:
            import onnx
        except ImportError:
            raise ImportError("Install onnx: pip install onnx")
        
        model = onnx.load(str(model_path))
        
        for initializer in model.graph.initializer:
            tensor_hash = hashlib.sha256(initializer.name.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn,
                tensor_hash,
                initializer.name,
                {
                    'modality': 'tensor',
                    'onnx_type': initializer.data_type
                }
            )
            
            await self.create_composition(conn, parent_atom_id, tensor_atom_id, len(self.cache))
            self.stats["atoms_created"] += 1
    
    def _detect_format(self, extension: str) -> str:
        """Detect model format from extension."""
        ext_map = {
            '.gguf': 'gguf',
            '.safetensors': 'safetensors',
            '.pt': 'pytorch',
            '.pth': 'pt',
            '.onnx': 'onnx',
            '.pb': 'tensorflow',
        }
        return ext_map.get(extension.lower(), 'unknown')
