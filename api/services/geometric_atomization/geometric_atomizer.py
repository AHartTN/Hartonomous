"""
GeometricAtomizer: Main Orchestrator for Trajectory-Based Atomization

Coordinates AtomLocator, TrajectoryBuilder, and SpatialReconstructor
to atomize and reconstruct content using geometric trajectories.

This is the high-level API for the breakthrough architecture.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import List, Tuple, Optional, Any
import numpy as np

from .atom_locator import AtomLocator
from .trajectory_builder import TrajectoryBuilder
from .spatial_reconstructor import SpatialReconstructor
from .fractal_atomizer import FractalAtomizer


class GeometricAtomizer:
    """
    High-level orchestrator for geometric atomization.
    
    Provides simple API for:
    1. Atomizing content → LINESTRING trajectories
    2. Storing trajectories in database
    3. Reconstructing content from trajectories
    """
    
    def __init__(
        self, 
        db_connection=None,
        coordinate_range: float = 1e6
    ):
        """
        Initialize GeometricAtomizer.
        
        Args:
            db_connection: Database connection (optional)
            coordinate_range: Coordinate space size
        """
        self.locator = AtomLocator(coordinate_range=coordinate_range)
        self.builder = TrajectoryBuilder()
        self.reconstructor = SpatialReconstructor(db_connection=db_connection)
        self.fractal = FractalAtomizer(db_connection=db_connection, coordinate_range=coordinate_range)
        self.db = db_connection
    
    async def atomize_text(self, text: str) -> str:
        """
        Atomize text into trajectory WKT.
        
        Args:
            text: Input text (e.g., "Hello World")
        
        Returns:
            LINESTRING ZM WKT string
        """
        from ..utils import run_cpu_bound
        
        # Convert text to bytes (one byte per character)
        atom_values = [char.encode('utf-8') for char in text]
        
        # Use trajectory builder to create WKT (run in executor for CPU-intensive hashing)
        wkt = await run_cpu_bound(
            self.builder.build_from_atoms,
            atom_values,
            self.locator
        )
        
        return wkt
    
    async def atomize_tensor(
        self, 
        tensor: np.ndarray,
        chunk_size: int = 10000
    ) -> List[str]:
        """
        Atomize tensor into trajectory WKT(s).
        
        Large tensors are chunked into multiple trajectories.
        Chunks are processed concurrently for large models (600+ layers).
        
        Args:
            tensor: Numpy tensor (e.g., model weights)
            chunk_size: Max atoms per trajectory (for chunking)
        
        Returns:
            List of LINESTRING ZM WKT strings
        """
        from ..utils import run_cpu_bound, run_cpu_batch_simple, chunk_list, get_optimal_workers
        
        # Flatten tensor
        flat_values = tensor.flatten()
        
        # Convert to bytes (assume float32)
        atom_values = [val.tobytes() for val in flat_values]
        
        # If small enough, create single trajectory
        if len(atom_values) <= chunk_size:
            wkt = await run_cpu_bound(
                self.builder.build_from_atoms,
                atom_values,
                self.locator
            )
            return [wkt]
        
        # Otherwise, chunk it and process chunks concurrently
        chunks = chunk_list(atom_values, chunk_size)
        
        # Build partial function for each chunk
        def build_chunk(chunk_values):
            return self.builder.build_from_atoms(chunk_values, self.locator)
        
        # Process all chunks concurrently with optimal workers
        optimal_workers = get_optimal_workers(len(chunks), max_workers=8)
        wkts = await run_cpu_batch_simple(build_chunk, chunks, max_workers=optimal_workers)
        
        return list(wkts)
    
    async def store_trajectory(
        self,
        name: str,
        wkt: str,
        metadata: dict = None
    ) -> int:
        """
        Store trajectory as a composition.
        
        DEPRECATED: This method is for backward compatibility with tests.
        New code should use atomize_text/atomize_tensor directly.
        
        Args:
            name: Composition name (e.g., "model.layers.0.weight")
            wkt: LINESTRING ZM WKT (will be parsed back to atoms)
            metadata: Optional metadata dict
        
        Returns:
            atom_id of the composition
        """
        if self.db is None:
            raise ValueError("Database connection required")
        
        # Parse WKT to extract atoms
        points = self.reconstructor.parse_wkt(wkt)
        
        # For each point, get or create primitive atom
        # This is a simplified approach - real implementation would need to
        # look up existing atoms by coordinate or store coordinate->atom_id mapping
        atom_ids = []
        for x, y, z, m in points:
            # For now, just create dummy primitives
            # Real implementation needs coordinate->atom lookup
            atom_id = await self.fractal.get_or_create_primitive(
                f"{x},{y},{z},{m}".encode('utf-8'),
                metadata={'coord': [x, y, z, m]}
            )
            atom_ids.append(atom_id)
        
        # Create composition with name in metadata
        meta = metadata or {}
        meta['name'] = name
        
        composition_id = await self.fractal.get_or_create_composition(
            child_ids=atom_ids,
            canonical_text=name,
            metadata=meta,
            is_stable=True
        )
        
        return composition_id
    
    async def reconstruct_from_name(
        self,
        name: str,
        output_type: str = 'bytes'
    ) -> Any:
        """
        Reconstruct content from composition name.
        
        Args:
            name: Composition name
            output_type: 'bytes', 'text', or 'tensor'
        
        Returns:
            Reconstructed content
        """
        if self.db is None:
            raise ValueError("Database connection required")
        
        from ..utils import query_one
        
        # Query atom by metadata name
        query = """
            SELECT atom_id, composition_ids, canonical_text
            FROM atom
            WHERE metadata->>'name' = %s
            AND composition_ids IS NOT NULL
            LIMIT 1
        """
        
        row = await query_one(self.db, query, (name,))
        
        if row is None:
            raise ValueError(f"Composition '{name}' not found")
        
        atom_id, composition_ids, canonical_text = row
        
        # Reconstruct based on output type
        if output_type == 'text':
            # Use canonical_text if available
            if canonical_text:
                return canonical_text
            # Otherwise reconstruct from children
            return await self._reconstruct_text_from_composition(composition_ids)
        elif output_type == 'bytes':
            return await self._reconstruct_bytes_from_composition(composition_ids)
        elif output_type == 'tensor':
            raise NotImplementedError("Tensor reconstruction requires shape metadata")
        else:
            raise ValueError(f"Unknown output_type: {output_type}")
    
    async def _reconstruct_text_from_composition(self, composition_ids: List[int]) -> str:
        """Reconstruct text by fetching child atoms and concatenating their values."""
        if self.db is None:
            raise ValueError("Database connection required")
        
        from ..utils import query_many
        
        query = """
            SELECT atom_value 
            FROM atom 
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """
        
        rows = await query_many(self.db, query, (composition_ids, composition_ids))
        
        chars = [bytes(row[0]).decode('utf-8') for row in rows]
        return ''.join(chars)
    
    async def _reconstruct_bytes_from_composition(self, composition_ids: List[int]) -> bytes:
        """Reconstruct bytes by fetching child atoms and concatenating their values."""
        if self.db is None:
            raise ValueError("Database connection required")
        
        from ..utils import query_many
        
        query = """
            SELECT atom_value 
            FROM atom 
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """
        
        rows = await query_many(self.db, query, (composition_ids, composition_ids))
        
        byte_chunks = [bytes(row[0]) for row in rows]
        return b''.join(byte_chunks)
    
    async def ingest_model(
        self,
        model_state_dict: dict,
        model_name: str,
        chunk_size: int = 10000
    ) -> dict:
        """
        Ingest entire model into database as compositions.
        
        Args:
            model_state_dict: PyTorch/TensorFlow model weights
            model_name: Model identifier
            chunk_size: Max atoms per composition
        
        Returns:
            Dict mapping layer names to composition atom_ids
        """
        composition_ids = {}
        
        for layer_name, tensor in model_state_dict.items():
            # Convert to numpy if needed
            if hasattr(tensor, 'numpy'):
                tensor = tensor.numpy()
            
            # Atomize tensor
            comp_id = await self.atomize_tensor(tensor, chunk_size)
            
            # Update metadata with model/layer info
            from ..utils import execute_query
            import json
            
            metadata = {
                'model': model_name,
                'layer': layer_name,
                'shape': list(tensor.shape),
                'dtype': str(tensor.dtype),
                'name': f"{model_name}.{layer_name}"
            }
            await execute_query(self.db, """
                UPDATE atom 
                SET metadata = %s::JSONB
                WHERE atom_id = %s
            """, (json.dumps(metadata), comp_id))
            await self.db.commit()
            
            composition_ids[layer_name] = comp_id
        
        return composition_ids
    
    async def reconstruct_model(
        self,
        model_name: str
    ) -> dict:
        """
        Reconstruct entire model from compositions.
        
        Args:
            model_name: Model identifier
        
        Returns:
            Dict mapping layer names to numpy tensors
        """
        from ..utils import query_many
        
        # Query all compositions for this model
        query = """
            SELECT atom_id, metadata
            FROM atom
            WHERE metadata->>'model' = %s
            AND composition_ids IS NOT NULL
            ORDER BY metadata->>'layer'
        """
        
        rows = await query_many(self.db, query, (model_name,))
        
        if not rows:
            raise ValueError(f"No compositions found for model '{model_name}'")
        
        # Reconstruct each layer
        import json
        state_dict = {}
        for atom_id, metadata_json in rows:
            metadata = json.loads(metadata_json) if isinstance(metadata_json, str) else metadata_json
            layer_name = metadata['layer']
            shape = tuple(metadata['shape'])
            
            # Reconstruct tensor from composition
            # For now, simplified - real implementation needs recursive reconstruction
            tensor_bytes = await self._reconstruct_bytes_from_atom(atom_id)
            
            # Convert bytes to numpy
            flat_values = np.frombuffer(tensor_bytes, dtype=np.float32)
            tensor = flat_values.reshape(shape)
            
            state_dict[layer_name] = tensor
        
        return state_dict
    
    async def _reconstruct_bytes_from_atom(self, atom_id: int) -> bytes:
        """Recursively reconstruct bytes from an atom (primitive or composition)."""
        if self.db is None:
            raise ValueError("Database connection required")
        
        from ..utils import query_one
        
        query = """
            SELECT atom_value, composition_ids
            FROM atom
            WHERE atom_id = %s
        """
        
        row = await query_one(self.db, query, (atom_id,))
        
        if row is None:
            raise ValueError(f"Atom {atom_id} not found")
        
        atom_value, composition_ids = row
        
        # If primitive, return value
        if atom_value is not None:
            return bytes(atom_value)
        
        # If composition, recursively reconstruct children
        if composition_ids:
            child_bytes = []
            for child_id in composition_ids:
                child_data = await self._reconstruct_bytes_from_atom(child_id)
                child_bytes.append(child_data)
            return b''.join(child_bytes)
            
            raise ValueError(f"Atom {atom_id} has neither atom_value nor composition_ids")
