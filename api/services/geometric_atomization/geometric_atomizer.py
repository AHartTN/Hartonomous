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
        self.db = db_connection
    
    def atomize_text(self, text: str) -> str:
        """
        Atomize text into LINESTRING trajectory.
        
        Args:
            text: Input text (e.g., "Hello World")
        
        Returns:
            LINESTRING ZM WKT
        """
        # Convert text to bytes (one atom per character)
        atom_values = [char.encode('utf-8') for char in text]
        
        # Build trajectory
        wkt = self.builder.build_from_atoms(atom_values, self.locator)
        return wkt
    
    def atomize_tensor(
        self, 
        tensor: np.ndarray,
        chunk_size: int = 10000
    ) -> List[str]:
        """
        Atomize tensor into LINESTRING trajectories.
        
        Large tensors may be chunked to avoid PostGIS limits.
        
        Args:
            tensor: Numpy tensor (e.g., model weights)
            chunk_size: Max atoms per trajectory
        
        Returns:
            List of LINESTRING ZM WKT strings (one per chunk)
        """
        # Flatten tensor
        flat_values = tensor.flatten()
        
        # Convert to bytes (assume float32)
        atom_values = [val.tobytes() for val in flat_values]
        
        # Locate atoms
        coordinates = self.locator.locate_multiple(atom_values)
        
        # Build trajectories (chunked if needed)
        if len(coordinates) <= chunk_size:
            # Single trajectory
            return [self.builder.build_wkt(coordinates)]
        else:
            # Multiple chunks
            return self.builder.chunk_trajectory(coordinates, chunk_size)
    
    async def store_trajectory(
        self,
        name: str,
        wkt: str,
        metadata: dict = None
    ) -> int:
        """
        Store trajectory in database.
        
        Args:
            name: Composition name (e.g., "model.layers.0.weight")
            wkt: LINESTRING ZM WKT
            metadata: Optional metadata dict
        
        Returns:
            Composition ID
        """
        if self.db is None:
            raise ValueError("Database connection required")
        
        # Call create_composition SQL function
        query = """
            SELECT create_composition(
                $1::TEXT,                    -- name
                ST_GeomFromText($2),         -- trajectory WKT
                $3::JSONB                    -- metadata
            )
        """
        
        import json
        metadata_json = json.dumps(metadata or {})
        
        async with self.db.cursor() as cursor:
            await cursor.execute(query, (name, wkt, metadata_json))
            result = await cursor.fetchone()
            composition_id = result[0]
        
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
        
        # Query trajectory WKT by name
        query = """
            SELECT ST_AsText(spatial_key)
            FROM atom_composition
            WHERE name = $1
            LIMIT 1
        """
        
        async with self.db.cursor() as cursor:
            await cursor.execute(query, (name,))
            row = await cursor.fetchone()
            
            if row is None:
                raise ValueError(f"Composition '{name}' not found")
            
            wkt = row[0]
        
        # Reconstruct based on output type
        if output_type == 'bytes':
            return await self.reconstructor.reconstruct_sequence(wkt)
        elif output_type == 'text':
            return await self.reconstructor.reconstruct_text(wkt)
        elif output_type == 'tensor':
            raise NotImplementedError("Tensor reconstruction requires shape metadata")
        else:
            raise ValueError(f"Unknown output_type: {output_type}")
    
    async def ingest_model(
        self,
        model_state_dict: dict,
        model_name: str,
        chunk_size: int = 10000
    ) -> dict:
        """
        Ingest entire model into database as trajectories.
        
        This is the KEY operation - replaces broken Hilbert-based ingestion.
        
        Args:
            model_state_dict: PyTorch/TensorFlow model weights
            model_name: Model identifier
            chunk_size: Max atoms per trajectory
        
        Returns:
            Dict mapping layer names to composition IDs
        """
        composition_ids = {}
        
        for layer_name, tensor in model_state_dict.items():
            # Convert to numpy if needed
            if hasattr(tensor, 'numpy'):
                tensor = tensor.numpy()
            
            # Atomize tensor
            wkts = self.atomize_tensor(tensor, chunk_size)
            
            # Store each chunk
            for i, wkt in enumerate(wkts):
                chunk_name = f"{model_name}.{layer_name}"
                if len(wkts) > 1:
                    chunk_name += f".chunk{i}"
                
                metadata = {
                    'model': model_name,
                    'layer': layer_name,
                    'shape': list(tensor.shape),
                    'dtype': str(tensor.dtype),
                    'chunk_index': i,
                    'total_chunks': len(wkts)
                }
                
                comp_id = await self.store_trajectory(chunk_name, wkt, metadata)
                composition_ids[chunk_name] = comp_id
        
        return composition_ids
    
    async def reconstruct_model(
        self,
        model_name: str
    ) -> dict:
        """
        Reconstruct entire model from trajectories.
        
        Args:
            model_name: Model identifier
        
        Returns:
            Dict mapping layer names to numpy tensors
        """
        # Query all compositions for this model
        query = """
            SELECT name, ST_AsText(spatial_key), metadata
            FROM atom_composition
            WHERE metadata->>'model' = $1
            ORDER BY name
        """
        
        async with self.db.cursor() as cursor:
            await cursor.execute(query, (model_name,))
            rows = await cursor.fetchall()
        
        if not rows:
            raise ValueError(f"No compositions found for model '{model_name}'")
        
        # Group chunks by layer
        import json
        from collections import defaultdict
        
        layer_chunks = defaultdict(list)
        for name, wkt, metadata_json in rows:
            metadata = json.loads(metadata_json)
            layer_name = metadata['layer']
            layer_chunks[layer_name].append({
                'wkt': wkt,
                'metadata': metadata
            })
        
        # Reconstruct each layer
        state_dict = {}
        for layer_name, chunks in layer_chunks.items():
            # Sort chunks by index
            chunks_sorted = sorted(chunks, key=lambda c: c['metadata']['chunk_index'])
            
            # Reconstruct atoms from all chunks
            all_atoms = []
            for chunk in chunks_sorted:
                atoms = await self.reconstructor.reconstruct_sequence(chunk['wkt'])
                all_atoms.extend(atoms)
            
            # Convert to tensor
            shape = tuple(chunks[0]['metadata']['shape'])
            dtype_str = chunks[0]['metadata']['dtype']
            
            # Convert bytes to numpy
            flat_values = np.frombuffer(b''.join(all_atoms), dtype=np.float32)
            tensor = flat_values.reshape(shape)
            
            state_dict[layer_name] = tensor
        
        return state_dict
