"""
Compression layer for atom sequences and reference management.

Handles deduplication, reference counting, and storage optimization
across the atom table. Works with PostgreSQL's native compression.
"""

import hashlib
import numpy as np
from typing import Optional, List, Tuple, Set
from dataclasses import dataclass


@dataclass
class AtomReference:
    """Reference to a stored atom."""
    atom_id: int
    hash_value: bytes
    size: int
    reference_count: int


class AtomDeduplicator:
    """
    Handles atom deduplication and reference management.
    
    Ensures identical atoms are stored only once, with references
    tracking all usages. Critical for compression efficiency.
    """
    
    def __init__(self):
        self.hash_cache: dict[bytes, int] = {}  # hash -> atom_id
    
    def compute_hash(self, data: bytes) -> bytes:
        """
        Compute deterministic hash for atom data.
        
        Uses SHA256 for collision resistance - critical since
        we're deduplicating across potentially billions of atoms.
        """
        return hashlib.sha256(data).digest()
    
    def check_duplicate(self, data: bytes) -> Optional[int]:
        """
        Check if atom already exists.
        
        Returns atom_id if found, None if new.
        """
        hash_val = self.compute_hash(data)
        return self.hash_cache.get(hash_val)
    
    def register_atom(self, atom_id: int, data: bytes):
        """Register new atom in deduplication cache."""
        hash_val = self.compute_hash(data)
        self.hash_cache[hash_val] = atom_id
    
    def clear_cache(self):
        """Clear deduplication cache (for memory management)."""
        self.hash_cache.clear()


class CompressionAnalyzer:
    """
    Analyzes data patterns to determine optimal compression strategy.
    
    Provides metrics for understanding compression effectiveness
    and identifying optimization opportunities.
    """
    
    @staticmethod
    def analyze_sparsity(data: np.ndarray, threshold: float = 1e-6) -> dict:
        """Analyze sparsity patterns in data."""
        near_zero = np.abs(data) < threshold
        return {
            'sparsity_ratio': np.sum(near_zero) / data.size if data.size > 0 else 0,
            'zero_count': np.sum(near_zero),
            'nonzero_count': data.size - np.sum(near_zero),
            'density': 1.0 - (np.sum(near_zero) / data.size if data.size > 0 else 0)
        }
    
    @staticmethod
    def analyze_repetition(data: np.ndarray) -> dict:
        """Analyze value repetition for RLE potential."""
        flat = data.flatten()
        if len(flat) <= 1:
            return {'unique_ratio': 1.0, 'max_run': 1, 'avg_run': 1.0}
        
        unique_count = len(np.unique(flat))
        
        # Find run lengths
        changes = np.concatenate(([True], flat[1:] != flat[:-1], [True]))
        runs = np.diff(np.where(changes)[0])
        
        return {
            'unique_ratio': unique_count / len(flat),
            'unique_values': unique_count,
            'max_run': int(np.max(runs)) if len(runs) > 0 else 1,
            'avg_run': float(np.mean(runs)) if len(runs) > 0 else 1.0,
            'run_count': len(runs)
        }
    
    @staticmethod
    def analyze_gradients(data: np.ndarray) -> dict:
        """Analyze value gradients for delta encoding potential."""
        flat = data.flatten()
        if len(flat) <= 1:
            return {'gradient_ratio': 0, 'max_delta': 0, 'avg_delta': 0}
        
        deltas = np.abs(np.diff(flat))
        value_range = np.max(flat) - np.min(flat) if len(flat) > 0 else 0
        
        return {
            'gradient_ratio': float(np.mean(deltas) / value_range) if value_range > 0 else 0,
            'max_delta': float(np.max(deltas)) if len(deltas) > 0 else 0,
            'avg_delta': float(np.mean(deltas)) if len(deltas) > 0 else 0,
            'smooth_ratio': float(np.sum(deltas < value_range * 0.1) / len(deltas)) if len(deltas) > 0 else 0
        }
    
    @staticmethod
    def comprehensive_analysis(data: np.ndarray) -> dict:
        """Full compression potential analysis."""
        return {
            'size': data.size,
            'dtype': str(data.dtype),
            'shape': data.shape,
            'value_range': (float(np.min(data)), float(np.max(data))) if data.size > 0 else (0, 0),
            'sparsity': CompressionAnalyzer.analyze_sparsity(data),
            'repetition': CompressionAnalyzer.analyze_repetition(data),
            'gradients': CompressionAnalyzer.analyze_gradients(data)
        }


class ReferenceManager:
    """
    Manages atom reference counting and cleanup.
    
    Tracks how many times each atom is referenced to enable
    garbage collection of unused atoms.
    """
    
    def __init__(self):
        self.references: dict[int, int] = {}  # atom_id -> count
    
    def add_reference(self, atom_id: int) -> int:
        """Increment reference count for atom."""
        current = self.references.get(atom_id, 0)
        self.references[atom_id] = current + 1
        return current + 1
    
    def remove_reference(self, atom_id: int) -> int:
        """Decrement reference count for atom."""
        current = self.references.get(atom_id, 0)
        if current > 0:
            new_count = current - 1
            if new_count == 0:
                del self.references[atom_id]
            else:
                self.references[atom_id] = new_count
            return new_count
        return 0
    
    def get_unreferenced(self) -> List[int]:
        """Get list of atoms with zero references (candidates for deletion)."""
        return [atom_id for atom_id, count in self.references.items() if count == 0]
    
    def get_reference_count(self, atom_id: int) -> int:
        """Get current reference count for atom."""
        return self.references.get(atom_id, 0)


class CompressionMetrics:
    """
    Tracks compression effectiveness metrics.
    
    Provides insights for optimization and monitoring.
    """
    
    def __init__(self):
        self.total_atoms = 0
        self.unique_atoms = 0
        self.total_bytes_original = 0
        self.total_bytes_stored = 0
        self.deduplication_hits = 0
        self.encoding_savings = 0
    
    def record_atom(self, original_size: int, stored_size: int, is_duplicate: bool):
        """Record metrics for a stored atom."""
        self.total_atoms += 1
        if not is_duplicate:
            self.unique_atoms += 1
        else:
            self.deduplication_hits += 1
        
        self.total_bytes_original += original_size
        self.total_bytes_stored += stored_size
        self.encoding_savings += (original_size - stored_size)
    
    def get_metrics(self) -> dict:
        """Calculate compression metrics."""
        dedup_ratio = (self.total_atoms - self.unique_atoms) / self.total_atoms if self.total_atoms > 0 else 0
        compression_ratio = self.total_bytes_stored / self.total_bytes_original if self.total_bytes_original > 0 else 1.0
        
        return {
            'total_atoms': self.total_atoms,
            'unique_atoms': self.unique_atoms,
            'deduplication_ratio': dedup_ratio,
            'deduplication_hits': self.deduplication_hits,
            'compression_ratio': compression_ratio,
            'bytes_original': self.total_bytes_original,
            'bytes_stored': self.total_bytes_stored,
            'bytes_saved': self.total_bytes_original - self.total_bytes_stored,
            'encoding_savings': self.encoding_savings
        }
    
    def reset(self):
        """Reset all metrics."""
        self.__init__()
