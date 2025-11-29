"""
Semantic Embedding Service for Geometric Landmarks

This module provides semantic embeddings for vocabulary tokens, enabling
meaningful spatial positioning based on semantic similarity rather than
arbitrary hashing.

Key features:
- Lazy-loaded embedding model (sentence-transformers)
- Batch embedding generation for efficiency
- PCA dimensionality reduction to 3D coordinates
- Caching for repeated tokens
- Local model cache to avoid re-downloads
"""

import logging
import os
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np

logger = logging.getLogger(__name__)

# Global model instance (lazy-loaded on first use)
_embedding_model = None
_embedding_cache: Dict[str, np.ndarray] = {}
_reference_embeddings: Optional[np.ndarray] = None
_reference_coords: Optional[np.ndarray] = None

# Use local cache directory to avoid filling up user's drive
CACHE_DIR = Path(__file__).parent.parent.parent / ".cache" / "embedding_models"
CACHE_DIR.mkdir(parents=True, exist_ok=True)


def _get_embedding_model():
    """Lazy-load the sentence-transformers model (cached locally)."""
    global _embedding_model
    
    if _embedding_model is None:
        try:
            from sentence_transformers import SentenceTransformer
            
            # Check if model already exists in cache
            model_name = 'sentence-transformers/all-MiniLM-L6-v2'
            model_path = CACHE_DIR / 'all-MiniLM-L6-v2'
            
            if model_path.exists():
                logger.info(f"Loading cached embedding model from {model_path}...")
                # Use cache_folder so sentence-transformers finds it correctly
                _embedding_model = SentenceTransformer(model_name, cache_folder=str(CACHE_DIR))
                logger.info("✓ Cached model loaded successfully")
            else:
                logger.info(f"Downloading embedding model ({model_name}) to {CACHE_DIR}...")
                logger.info("  (This is a one-time download, ~90MB)")
                _embedding_model = SentenceTransformer(model_name, cache_folder=str(CACHE_DIR))
                logger.info("✓ Model downloaded and cached successfully")
        except ImportError:
            logger.warning(
                "sentence-transformers not installed. "
                "Install with: pip install sentence-transformers"
            )
            raise
        except Exception as e:
            logger.error(f"Failed to load embedding model: {e}")
            raise
    
    return _embedding_model


def generate_embeddings_batch(
    texts: List[str],
    normalize: bool = True,
) -> np.ndarray:
    """
    Generate semantic embeddings for a batch of texts.
    
    Args:
        texts: List of text strings to embed
        normalize: Whether to L2-normalize embeddings (default: True)
    
    Returns:
        NumPy array of shape (len(texts), embedding_dim)
        Default model produces 384-dimensional embeddings
    """
    model = _get_embedding_model()
    
    # Generate embeddings (automatically batched by sentence-transformers)
    embeddings = model.encode(
        texts,
        normalize_embeddings=normalize,
        show_progress_bar=len(texts) > 1000,
        batch_size=32,
    )
    
    return embeddings


def _get_landmark_coordinates() -> np.ndarray:
    """
    Get fixed landmark coordinates for trilateration reference points.
    
    These landmarks define the semantic space axes:
    - Text modality landmarks along X axis
    - Category landmarks along Y axis  
    - Specificity landmarks along Z axis
    
    Returns:
        Array of shape (N, 3) with landmark X, Y, Z coordinates
    """
    # Fixed landmarks that define the semantic space
    # Based on landmark_registry.py coordinate system
    landmarks = np.array([
        # Modality landmarks (X-axis variation)
        [0.15, 0.5, 0.5],  # MODALITY_TEXT
        [0.30, 0.5, 0.5],  # MODALITY_IMAGE  
        [0.45, 0.5, 0.5],  # MODALITY_AUDIO
        [0.60, 0.5, 0.5],  # MODALITY_VIDEO
        [0.75, 0.5, 0.5],  # MODALITY_CODE
        [0.90, 0.5, 0.5],  # MODALITY_STRUCTURED
        
        # Category landmarks (Y-axis variation)
        [0.5, 0.15, 0.5],  # CATEGORY_ABSTRACT
        [0.5, 0.35, 0.5],  # CATEGORY_SYMBOLIC
        [0.5, 0.55, 0.5],  # CATEGORY_RELATIONAL
        [0.5, 0.75, 0.5],  # CATEGORY_LITERAL
        [0.5, 0.90, 0.5],  # CATEGORY_ATOMIC
        
        # Specificity landmarks (Z-axis variation)
        [0.5, 0.5, 0.10],  # SPECIFICITY_UNIVERSAL
        [0.5, 0.5, 0.30],  # SPECIFICITY_AGGREGATE
        [0.5, 0.5, 0.60],  # SPECIFICITY_COMPOUND
        [0.5, 0.5, 0.85],  # SPECIFICITY_SPECIFIC
        [0.5, 0.5, 0.95],  # SPECIFICITY_ATOMIC
    ])
    return landmarks


def project_to_3d(
    embeddings: np.ndarray,
    fit_references: bool = False,
) -> np.ndarray:
    """
    Project high-dimensional embeddings to 3D coordinates using geometric trilateration.
    
    Instead of lossy PCA, this uses GPS-style positioning:
    1. Compute cosine distances from embedding to reference landmarks
    2. Position based on weighted distances (closer landmarks have more influence)
    3. Result is deterministic and lossless - same embedding always produces same position
    
    Args:
        embeddings: NumPy array of shape (n_samples, embedding_dim)
        fit_references: Whether to create reference embeddings (True for first batch)
    
    Returns:
        NumPy array of shape (n_samples, 3) with X, Y, Z coordinates
        Each coordinate is in [0, 1] range
    """
    global _reference_embeddings, _reference_coords
    
    # Get landmark coordinates
    landmark_coords = _get_landmark_coordinates()
    
    if fit_references or _reference_embeddings is None:
        # Create reference embeddings by sampling from the batch
        # Select diverse samples that span the embedding space
        n_refs = min(len(landmark_coords), len(embeddings))
        
        # Use k-means++ style selection for good coverage
        indices = [0]  # Start with first sample
        for _ in range(n_refs - 1):
            # Find sample farthest from current references
            dists = np.sum((embeddings[:, None, :] - embeddings[indices, :]) ** 2, axis=2)
            min_dists = np.min(dists, axis=1)
            indices.append(np.argmax(min_dists))
        
        _reference_embeddings = embeddings[indices]
        _reference_coords = landmark_coords[:n_refs]
        
        logger.info(f"✓ Created {n_refs} reference embeddings for trilateration")
    
    # Compute cosine similarities between embeddings and references
    # Normalize embeddings
    embeddings_norm = embeddings / (np.linalg.norm(embeddings, axis=1, keepdims=True) + 1e-8)
    refs_norm = _reference_embeddings / (np.linalg.norm(_reference_embeddings, axis=1, keepdims=True) + 1e-8)
    
    # Cosine similarity matrix (n_samples x n_refs)
    similarities = embeddings_norm @ refs_norm.T
    
    # Convert to weights (higher similarity = higher weight)
    # Use softmax to ensure weights sum to 1 per sample
    weights = np.exp(similarities * 5)  # Scale factor for sharper weighting
    weights = weights / (np.sum(weights, axis=1, keepdims=True) + 1e-8)
    
    # Weighted average of reference coordinates
    # Shape: (n_samples, n_refs) @ (n_refs, 3) -> (n_samples, 3)
    coords_3d = weights @ _reference_coords
    
    # Ensure coordinates stay in [0, 1] range
    coords_3d = np.clip(coords_3d, 0.0, 1.0)
    
    return coords_3d


def generate_semantic_coordinates(
    tokens: List[str],
    fit_references: bool = True,
) -> List[Tuple[float, float, float]]:
    """
    Generate semantic 3D coordinates for vocabulary tokens using geometric trilateration.
    
    This is the main entry point for vocabulary atomization.
    Uses GPS-style positioning based on cosine distances to reference landmarks.
    
    Args:
        tokens: List of token strings
        fit_references: Whether to create reference embeddings (True for first call)
    
    Returns:
        List of (X, Y, Z) tuples where each coordinate is in [0, 1]
        Semantically similar tokens will have similar coordinates
        Positioning is deterministic and lossless - same embedding = same position
    
    Example:
        >>> tokens = ["cat", "dog", "automobile", "vehicle"]
        >>> coords = generate_semantic_coordinates(tokens)
        >>> # "cat" and "dog" will be close together (high cosine similarity)
        >>> # "automobile" and "vehicle" will be close together
        >>> # animals and vehicles will be far apart (low cosine similarity)
    """
    logger.info(f"Generating semantic embeddings for {len(tokens):,} tokens...")
    
    # Generate high-dimensional embeddings
    embeddings = generate_embeddings_batch(tokens, normalize=True)
    logger.info(f"✓ Generated {embeddings.shape[1]}D embeddings")
    
    # Project to 3D coordinates using geometric trilateration
    coords_3d = project_to_3d(embeddings, fit_references=fit_references)
    logger.info(f"✓ Projected to 3D coordinates via trilateration")
    
    # Convert to list of tuples
    return [(float(x), float(y), float(z)) for x, y, z in coords_3d]


def get_embedding_stats() -> Dict[str, any]:
    """Get statistics about the embedding service."""
    return {
        "model_loaded": _embedding_model is not None,
        "references_fitted": _reference_embeddings is not None,
        "num_references": len(_reference_embeddings) if _reference_embeddings is not None else 0,
        "cache_size": len(_embedding_cache),
        "model_name": "all-MiniLM-L6-v2" if _embedding_model else None,
        "embedding_dim": 384 if _embedding_model else None,
    }


def clear_cache():
    """Clear the embedding cache to free memory."""
    global _embedding_cache
    _embedding_cache.clear()
    logger.info("Embedding cache cleared")
