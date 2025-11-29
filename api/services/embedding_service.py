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
"""

import logging
from typing import Dict, List, Optional, Tuple

import numpy as np

logger = logging.getLogger(__name__)

# Global model instance (lazy-loaded on first use)
_embedding_model = None
_pca_model = None
_embedding_cache: Dict[str, np.ndarray] = {}


def _get_embedding_model():
    """Lazy-load the sentence-transformers model."""
    global _embedding_model
    
    if _embedding_model is None:
        try:
            from sentence_transformers import SentenceTransformer
            
            logger.info("Loading semantic embedding model (all-MiniLM-L6-v2)...")
            _embedding_model = SentenceTransformer('all-MiniLM-L6-v2')
            logger.info("✓ Embedding model loaded successfully")
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


def _get_pca_model(embeddings: np.ndarray, n_components: int = 3):
    """Get or create PCA model for dimensionality reduction."""
    global _pca_model
    
    if _pca_model is None:
        from sklearn.decomposition import PCA
        
        logger.info(f"Fitting PCA model to reduce {embeddings.shape[1]}D → {n_components}D...")
        _pca_model = PCA(n_components=n_components)
        _pca_model.fit(embeddings)
        
        explained_var = _pca_model.explained_variance_ratio_.sum()
        logger.info(f"✓ PCA fitted, explained variance: {explained_var:.1%}")
    
    return _pca_model


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


def project_to_3d(
    embeddings: np.ndarray,
    fit_pca: bool = False,
) -> np.ndarray:
    """
    Project high-dimensional embeddings to 3D coordinates using PCA.
    
    Args:
        embeddings: NumPy array of shape (n_samples, embedding_dim)
        fit_pca: Whether to fit a new PCA model (True for first batch)
    
    Returns:
        NumPy array of shape (n_samples, 3) with X, Y, Z coordinates
        Each coordinate is normalized to approximately [0, 1] range
    """
    global _pca_model
    
    if fit_pca or _pca_model is None:
        _pca_model = _get_pca_model(embeddings, n_components=3)
    
    # Transform to 3D
    coords_3d = _pca_model.transform(embeddings)
    
    # Normalize to [0, 1] range using min-max scaling
    # (allows tokens to be positioned in unit cube)
    min_vals = coords_3d.min(axis=0)
    max_vals = coords_3d.max(axis=0)
    
    # Avoid division by zero
    ranges = max_vals - min_vals
    ranges[ranges == 0] = 1.0
    
    normalized = (coords_3d - min_vals) / ranges
    
    return normalized


def generate_semantic_coordinates(
    tokens: List[str],
    fit_pca: bool = True,
) -> List[Tuple[float, float, float]]:
    """
    Generate semantic 3D coordinates for vocabulary tokens.
    
    This is the main entry point for vocabulary atomization.
    
    Args:
        tokens: List of token strings
        fit_pca: Whether to fit PCA on this batch (True for first call)
    
    Returns:
        List of (X, Y, Z) tuples where each coordinate is in [0, 1]
        Semantically similar tokens will have similar coordinates
    
    Example:
        >>> tokens = ["cat", "dog", "automobile", "vehicle"]
        >>> coords = generate_semantic_coordinates(tokens)
        >>> # "cat" and "dog" will be close together
        >>> # "automobile" and "vehicle" will be close together
        >>> # animals and vehicles will be far apart
    """
    logger.info(f"Generating semantic embeddings for {len(tokens):,} tokens...")
    
    # Generate high-dimensional embeddings
    embeddings = generate_embeddings_batch(tokens, normalize=True)
    logger.info(f"✓ Generated {embeddings.shape[1]}D embeddings")
    
    # Project to 3D coordinates
    coords_3d = project_to_3d(embeddings, fit_pca=fit_pca)
    logger.info(f"✓ Projected to 3D coordinates")
    
    # Convert to list of tuples
    return [(float(x), float(y), float(z)) for x, y, z in coords_3d]


def get_embedding_stats() -> Dict[str, any]:
    """Get statistics about the embedding service."""
    return {
        "model_loaded": _embedding_model is not None,
        "pca_fitted": _pca_model is not None,
        "cache_size": len(_embedding_cache),
        "model_name": "all-MiniLM-L6-v2" if _embedding_model else None,
        "embedding_dim": 384 if _embedding_model else None,
    }


def clear_cache():
    """Clear the embedding cache to free memory."""
    global _embedding_cache
    _embedding_cache.clear()
    logger.info("Embedding cache cleared")
