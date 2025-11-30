"""
GGUF Model Atomizer - Main orchestrator for GGUF file processing.

Single Responsibility: Coordinate GGUF file loading and atomization workflow.
"""

import logging
from pathlib import Path
from typing import Dict, Any, Optional
from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from src.core.atomization.base_atomizer import BaseAtomizer
from .tensor_atomizer import TensorAtomizer

logger = logging.getLogger(__name__)


class GGUFAtomizer(BaseAtomizer):
    """
    Atomizes GGUF model files into atoms and compositions.
    
    Handles:
    - GGUF file loading
    - Tensor extraction
    - Vocabulary atomization
    - Architecture metadata atomization
    - Orchestration of tensor atomization
    """
    
    def __init__(self, threshold: float = 1e-6, parallel_processing: bool = False):
        """
        Initialize GGUF atomizer.
        
        Args:
            threshold: Sparsity threshold for filtering near-zero weights
            parallel_processing: Enable parallel composition creation
        """
        # Don't call super().__init__() - we override stats as property
        self.threshold = threshold
        self.parallel_processing = parallel_processing
        self.cache = {}  # For backward compatibility
        self.tensor_atomizer = TensorAtomizer(threshold, parallel_processing)
        
        # For backward compatibility with tests
        self.encoder = type('Encoder', (), {'sparse_threshold': threshold})()
    
    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        pool: Optional[AsyncConnectionPool] = None
    ) -> Dict[str, Any]:
        """
        Atomize entire GGUF model file.
        
        Args:
            file_path: Path to GGUF file
            model_name: Model identifier
            conn: Database connection
            pool: Connection pool for parallel operations
            
        Returns:
            Dict with atomization statistics
        """
        logger.info(f"\n{'='*80}")
        logger.info(f"Starting GGUF Model Atomization")
        logger.info(f"  Model: {model_name}")
        logger.info(f"  File: {file_path}")
        logger.info(f"  Size: {file_path.stat().st_size / 1e6:.2f} MB")
        logger.info(f"  Threshold: {self.threshold}")
        logger.info(f"  Parallel: {self.parallel_processing}")
        logger.info(f"{'='*80}\n")
        
        # Load GGUF file
        await self._atomize_gguf_file(conn, pool, file_path, model_name)
        
        # Get final stats
        stats = self.tensor_atomizer.get_stats()
        
        # Track tensor atoms
        stats['tensor_atoms'] = getattr(self, '_tensor_atoms', [])
        stats['tensors_processed'] = len(getattr(self, '_tensor_atoms', []))
        
        logger.info(f"\n{'='*80}")
        logger.info(f"GGUF Atomization Complete")
        logger.info(f"  Total weights processed: {stats['total_processed']:,}")
        logger.info(f"  Unique atoms created: {stats['atoms_created']:,}")
        logger.info(f"  Sparse weights skipped: {stats['sparse_skipped']:,}")
        logger.info(f"  Deduplication ratio: {stats['deduplication_ratio']:.1f}x")
        logger.info(f"  Sparse savings: {stats.get('sparse_percentage', 0):.1f}%")
        logger.info(f"{'='*80}\n")
        
        return stats
    
    async def _atomize_gguf_file(
        self,
        conn: AsyncConnection,
        pool: Optional[AsyncConnectionPool],
        file_path: Path,
        model_name: str
    ):
        """
        Load and process GGUF file.
        
        Args:
            conn: Database connection
            pool: Connection pool
            file_path: Path to GGUF file
            model_name: Model identifier
        """
        try:
            from gguf import GGUFReader
        except ImportError:
            raise ImportError(
                "gguf package required for GGUF atomization. "
                "Install with: pip install gguf"
            )
        
        logger.info("Loading GGUF file...")
        reader = GGUFReader(str(file_path))
        
        # Atomize metadata
        await self._atomize_architecture(conn, reader, model_name)
        await self._atomize_vocabulary(conn, reader, model_name)
        
        # Atomize tensors
        logger.info(f"\nProcessing {len(reader.tensors)} tensors...")
        
        self._tensor_atoms = []
        
        for idx, tensor in enumerate(reader.tensors, 1):
            logger.info(f"\n[Tensor {idx}/{len(reader.tensors)}]")
            
            tensor_atom_id, tensor_stats = await self.tensor_atomizer.atomize_tensor(
                conn=conn,
                pool=pool,
                tensor_name=tensor.name,
                tensor_data=tensor.data,
                model_name=model_name
            )
            
            self._tensor_atoms.append(tensor_atom_id)
    
    async def _atomize_vocabulary(
        self,
        conn: AsyncConnection,
        reader,
        model_name: str
    ):
        """Atomize model vocabulary tokens."""
        logger.info("Atomizing vocabulary...")
        # TODO: Implement vocabulary atomization
        logger.info("✓ Vocabulary atomization (placeholder)")
    
    async def _atomize_architecture(
        self,
        conn: AsyncConnection,
        reader,
        model_name: str
    ):
        """Atomize model architecture metadata."""
        logger.info("Atomizing architecture metadata...")
        # TODO: Implement architecture atomization
        logger.info("✓ Architecture atomization (placeholder)")
    
    # Backward compatibility methods for tests
    async def _atomize_weight(self, conn: AsyncConnection, weight: float) -> int:
        """Atomize single weight (for test compatibility)."""
        return await self.tensor_atomizer.weight_processor.atomize_weight(conn, weight)
    
    @property
    def stats(self) -> Dict[str, Any]:
        """Get stats (for test compatibility)."""
        return self.tensor_atomizer.get_stats()
