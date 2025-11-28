"""
Model parser - handles AI models with tensor-level atomization.
"""

import hashlib
import json
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
        """Parse SafeTensors format with full weight atomization."""
        try:
            from safetensors import safe_open
            import numpy as np
        except ImportError:
            raise ImportError("Install safetensors: pip install safetensors")
        
        with safe_open(model_path, framework="numpy") as f:
            for tensor_idx, key in enumerate(f.keys()):
                tensor = f.get_tensor(key)
                
                # Create tensor metadata atom
                tensor_hash = hashlib.sha256(key.encode()).digest()
                tensor_atom_id = await self.create_atom(
                    conn,
                    tensor_hash,
                    key,
                    {
                        'modality': 'tensor',
                        'shape': list(tensor.shape),
                        'dtype': str(tensor.dtype),
                        'sparse_threshold': self.threshold
                    }
                )
                
                await self.create_composition(conn, parent_atom_id, tensor_atom_id, tensor_idx)
                self.stats["atoms_created"] += 1
                
                # Atomize individual weights
                weights = tensor.flatten()
                for weight_idx, weight in enumerate(weights):
                    self.stats["total_processed"] = self.stats.get("total_processed", 0) + 1
                    
                    # Sparse encoding: skip near-zero weights
                    if abs(float(weight)) < self.threshold:
                        self.stats["sparse_skipped"] = self.stats.get("sparse_skipped", 0) + 1
                        continue
                    
                    # Atomize weight and create composition
                    weight_atom_id = await self._atomize_weight(conn, float(weight))
                    await self.create_composition(conn, tensor_atom_id, weight_atom_id, weight_idx)
                
                self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1
    
    async def _parse_pytorch(self, model_path: Path, parent_atom_id: int, conn):
        """Parse PyTorch checkpoint with full weight atomization."""
        try:
            import torch
            import numpy as np
        except ImportError:
            raise ImportError("Install pytorch: pip install torch")
        
        checkpoint = torch.load(model_path, map_location='cpu')
        
        if isinstance(checkpoint, dict):
            state_dict = checkpoint.get('state_dict', checkpoint)
        else:
            state_dict = checkpoint
        
        for tensor_idx, (key, tensor) in enumerate(state_dict.items()):
            # Create tensor metadata atom
            tensor_hash = hashlib.sha256(key.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn,
                tensor_hash,
                key,
                {
                    'modality': 'tensor',
                    'shape': list(tensor.shape),
                    'dtype': str(tensor.dtype),
                    'sparse_threshold': self.threshold
                }
            )
            
            await self.create_composition(conn, parent_atom_id, tensor_atom_id, tensor_idx)
            self.stats["atoms_created"] += 1
            
            # Atomize individual weights
            weights = tensor.flatten().numpy()
            for weight_idx, weight in enumerate(weights):
                self.stats["total_processed"] = self.stats.get("total_processed", 0) + 1
                
                # Sparse encoding: skip near-zero weights
                if abs(float(weight)) < self.threshold:
                    self.stats["sparse_skipped"] = self.stats.get("sparse_skipped", 0) + 1
                    continue
                
                # Atomize weight and create composition
                weight_atom_id = await self._atomize_weight(conn, float(weight))
                await self.create_composition(conn, tensor_atom_id, weight_atom_id, weight_idx)
            
            self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1
    
    async def _parse_onnx(self, model_path: Path, parent_atom_id: int, conn):
        """Parse ONNX model with full weight atomization."""
        try:
            import onnx
            import numpy as np
            from onnx import numpy_helper
        except ImportError:
            raise ImportError("Install onnx: pip install onnx")
        
        model = onnx.load(str(model_path))
        
        for tensor_idx, initializer in enumerate(model.graph.initializer):
            # Convert ONNX tensor to numpy
            tensor = numpy_helper.to_array(initializer)
            
            # Create tensor metadata atom
            tensor_hash = hashlib.sha256(initializer.name.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn,
                tensor_hash,
                initializer.name,
                {
                    'modality': 'tensor',
                    'onnx_type': initializer.data_type,
                    'shape': list(tensor.shape),
                    'dtype': str(tensor.dtype),
                    'sparse_threshold': self.threshold
                }
            )
            
            await self.create_composition(conn, parent_atom_id, tensor_atom_id, tensor_idx)
            self.stats["atoms_created"] += 1
            
            # Atomize individual weights
            weights = tensor.flatten()
            for weight_idx, weight in enumerate(weights):
                self.stats["total_processed"] = self.stats.get("total_processed", 0) + 1
                
                # Sparse encoding: skip near-zero weights
                if abs(float(weight)) < self.threshold:
                    self.stats["sparse_skipped"] = self.stats.get("sparse_skipped", 0) + 1
                    continue
                
                # Atomize weight and create composition
                weight_atom_id = await self._atomize_weight(conn, float(weight))
                await self.create_composition(conn, tensor_atom_id, weight_atom_id, weight_idx)
            
            self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1
    
    async def _atomize_weight(self, conn, weight: float) -> int:
        """Atomize single weight value with deduplication cache."""
        if weight in self.cache:
            self.stats["atoms_deduped"] = self.stats.get("atoms_deduped", 0) + 1
            return self.cache[weight]
        
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, %s::jsonb)",
                (weight, json.dumps({"modality": "weight", "value": float(weight)}))
            )
            weight_atom_id = (await cur.fetchone())[0]
        
        self.cache[weight] = weight_atom_id
        self.stats["atoms_created"] = self.stats.get("atoms_created", 0) + 1
        return weight_atom_id
    
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
