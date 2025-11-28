"""
Spatial Encoding for AI Model Components as Geometric Landmarks

This module provides functions to project AI model components (tokens, weights,
hyperparameters, etc.) into 4D spatial coordinates (X, Y, Z, M) where:
- X, Y, Z: Euclidean coordinates for spatial queries (Voronoi, k-NN, clustering)
- M: Hilbert curve index for cache-coherent traversal and range queries

The goal: Enable geometric queries over AI models using PostGIS spatial indexing.
"""

import hashlib
from typing import Any, Dict, List, Optional, Tuple

import numpy as np


def hilbert_encode_1d(values: List[int], bits_per_dim: int = 10) -> int:
    """
    Encode multi-dimensional coordinates into 1D Hilbert curve index.

    Args:
        values: List of integer coordinates (e.g., [layer, head, row, col])
        bits_per_dim: Bits per dimension for Hilbert encoding (default 10 = 1024 max per dim)

    Returns:
        Integer Hilbert index

    Note: This is a simplified bit-interleaving approximation. For true Hilbert,
          we'd need the full Hilbert transform, but this provides spatial locality.
    """
    # Normalize values to fit in bits_per_dim
    max_val = (1 << bits_per_dim) - 1
    normalized = [min(int(v), max_val) for v in values]

    # Bit-interleave coordinates (Z-order curve approximation)
    # True Hilbert requires rotation/reflection transformations
    result = 0
    for bit in range(bits_per_dim):
        for dim_idx, val in enumerate(normalized):
            if val & (1 << bit):
                result |= 1 << (bit * len(normalized) + dim_idx)

    return result


def calculate_vocabulary_spatial_key(
    token_id: int,
    token_text: str,
    frequency_rank: Optional[int] = None,
    embedding: Optional[np.ndarray] = None,
    vocab_size: int = 151646,
) -> Tuple[float, float, float, float]:
    """
    Calculate spatial coordinates for vocabulary token atoms.

    Encoding strategy:
    - X: Token ID normalized to [0, 1] (lexicographic position in vocab)
    - Y: Frequency rank normalized to [0, 1] (common tokens cluster together)
    - Z: Semantic hash derived from token text (enables text similarity queries)
    - M: Hilbert index of token_id for cache-efficient token lookup

    If embeddings available, could use PCA dimensions for X,Y,Z instead.

    Args:
        token_id: Integer token ID from tokenizer
        token_text: String representation of token (e.g., "quantum", " the")
        frequency_rank: Optional frequency rank (0=most common)
        embedding: Optional token embedding vector for semantic positioning
        vocab_size: Total vocabulary size for normalization

    Returns:
        (X, Y, Z, M) coordinates as tuple of floats
    """
    # X: Normalized token ID [0, 1]
    x = token_id / vocab_size

    # Y: Frequency rank [0, 1] if available, else hash-based
    if frequency_rank is not None:
        y = frequency_rank / vocab_size
    else:
        # Use hash of token text for deterministic Y coordinate
        hash_val = int.from_bytes(
            hashlib.sha256(token_text.encode()).digest()[:4], "big"
        )
        y = (hash_val % 10000) / 10000.0

    # Z: Semantic hash from token text (enables text similarity)
    # Use first 4 bytes of hash, normalize to [0, 1]
    text_hash = int.from_bytes(hashlib.sha256(token_text.encode()).digest()[4:8], "big")
    z = (text_hash % 10000) / 10000.0

    # M: Hilbert index of token_id for efficient sequential access
    m = hilbert_encode_1d([token_id], bits_per_dim=20)

    return (x, y, z, float(m))


def calculate_architecture_spatial_key(
    config: Dict[str, Any],
) -> Tuple[float, float, float, float]:
    """
    Calculate spatial coordinates for architecture hyperparameter atoms.

    Encoding strategy:
    - X: log(embedding_dim) - Dimensional complexity axis
    - Y: num_layers / 100 - Model depth axis (normalized, assuming max ~100 layers)
    - Z: num_heads / 64 - Attention parallelism axis (normalized, assuming max ~64 heads)
    - M: Hilbert([embedding_dim, num_layers, num_heads]) for config clustering

    Models with similar architectures cluster in (X,Y,Z) space.

    Args:
        config: Dictionary with keys like:
            - embedding_length or embedding_dim
            - block_count or num_layers
            - attention.head_count or num_heads
            - context_length

    Returns:
        (X, Y, Z, M) coordinates as tuple of floats
    """
    # Extract config values with fallbacks
    embed_dim = config.get("embedding_length", config.get("embedding_dim", 4096))
    num_layers = config.get("block_count", config.get("num_layers", 32))
    num_heads = config.get("attention.head_count", config.get("num_heads", 32))
    context_len = config.get(
        "context_length", config.get("max_position_embeddings", 2048)
    )

    # X: Logarithmic embedding dimension (handles large ranges: 512 to 16384)
    # Normalize log scale: log2(512)=9, log2(16384)=14, map to [0,1]
    x = (np.log2(embed_dim) - 9) / 5.0  # Maps 512->0, 16384->1
    x = np.clip(x, 0.0, 1.0)

    # Y: Number of layers, normalized assuming max 100 layers
    y = num_layers / 100.0
    y = np.clip(y, 0.0, 1.0)

    # Z: Number of attention heads, normalized assuming max 128 heads
    z = num_heads / 128.0
    z = np.clip(z, 0.0, 1.0)

    # M: Hilbert index over [embed_dim, num_layers, num_heads]
    # Scale dimensions to ~10 bits each for Hilbert encoding
    dim_scaled = min(int(embed_dim / 16), 1023)  # 16384 -> 1024
    layers_scaled = min(num_layers * 10, 1023)  # 100 layers -> 1000
    heads_scaled = min(num_heads * 8, 1023)  # 128 heads -> 1024

    m = hilbert_encode_1d([dim_scaled, layers_scaled, heads_scaled], bits_per_dim=10)

    return (x, y, z, float(m))


def calculate_weight_spatial_key(
    layer_idx: int,
    head_idx: int,
    row_idx: int,
    col_idx: int,
    weight_value: float,
    model_config: Dict[str, Any],
) -> Tuple[float, float, float, float]:
    """
    Calculate spatial coordinates for tensor weight atoms with position encoding.

    Encoding strategy:
    - X: Layer depth normalized [0, 1] (early layers vs late layers)
    - Y: Attention head normalized [0, 1] (which parallel computation path)
    - Z: Weight value normalized to [-1, 1] or [0, 1] (magnitude)
    - M: Hilbert([layer, head, row, col]) for cache-coherent weight traversal

    This enables queries like:
    - "Find weights in layer 5" (X-axis range query)
    - "Find weights near value 0.5" (Z-axis range query)
    - "Find spatially adjacent weights" (Euclidean distance in XYZ)

    Args:
        layer_idx: Which layer (0 to num_layers-1)
        head_idx: Which attention head (0 to num_heads-1)
        row_idx: Row position in weight matrix
        col_idx: Column position in weight matrix
        weight_value: The actual weight value (float)
        model_config: Dict with num_layers, num_heads for normalization

    Returns:
        (X, Y, Z, M) coordinates as tuple of floats
    """
    num_layers = model_config.get("block_count", model_config.get("num_layers", 40))
    num_heads = model_config.get(
        "attention.head_count", model_config.get("num_heads", 32)
    )

    # X: Layer depth [0, 1]
    x = layer_idx / max(num_layers - 1, 1)

    # Y: Attention head [0, 1]
    y = head_idx / max(num_heads - 1, 1) if num_heads > 1 else 0.5

    # Z: Weight value normalized
    # For quantized models, values often in range like [-127, 127] or [0, 255]
    # We'll normalize to [0, 1] assuming symmetric range
    # Could improve with statistics from full tensor distribution
    z = (weight_value + 128) / 256.0  # Assumes 8-bit quantization range
    z = np.clip(z, 0.0, 1.0)

    # M: Hilbert index over position [layer, head, row, col]
    # Scale positions to fit in Hilbert bits
    # For large tensors (e.g., 8192x8192), we downsample to ~10 bits per dim
    row_scaled = min(row_idx // 8, 1023)  # Downsample large matrices
    col_scaled = min(col_idx // 8, 1023)

    m = hilbert_encode_1d(
        [layer_idx, head_idx, row_scaled, col_scaled], bits_per_dim=10
    )

    return (x, y, z, float(m))


def calculate_merge_spatial_key(
    merge_id: int,
    priority: int,
    left_token_id: int,
    right_token_id: int,
    total_merges: int = 100000,
) -> Tuple[float, float, float, float]:
    """
    Calculate spatial coordinates for BPE merge rule atoms.

    Encoding strategy:
    - X: Result token ID normalized (where in vocab the merge produces)
    - Y: Merge priority normalized [0, 1] (early merges vs late merges in BPE)
    - Z: Average of component token IDs (semantic clustering of merge inputs)
    - M: Hilbert([merge_id, priority]) for merge sequence traversal

    Args:
        merge_id: Index of this merge rule
        priority: Priority/order in BPE algorithm (lower = earlier)
        left_token_id: Token ID of left component
        right_token_id: Token ID of right component
        total_merges: Total number of merge rules for normalization

    Returns:
        (X, Y, Z, M) coordinates as tuple of floats
    """
    # X: Merge ID position [0, 1]
    x = merge_id / max(total_merges, 1)

    # Y: Priority [0, 1] (early merges have low priority numbers)
    y = priority / max(total_merges, 1)

    # Z: Average of input token IDs (semantic locality)
    z = (left_token_id + right_token_id) / (2.0 * 151646)  # Normalize by vocab size
    z = np.clip(z, 0.0, 1.0)

    # M: Hilbert over [merge_id, priority]
    merge_scaled = min(merge_id, 1023)
    priority_scaled = min(priority, 1023)
    m = hilbert_encode_1d([merge_scaled, priority_scaled], bits_per_dim=10)

    return (x, y, z, float(m))


def spatial_key_to_wkt(coords: Tuple[float, float, float, float]) -> str:
    """
    Convert (X, Y, Z, M) coordinates to PostGIS WKT (Well-Known Text) format.

    Args:
        coords: Tuple of (x, y, z, m) coordinates

    Returns:
        WKT string like "POINT ZM (0.5 0.3 0.8 12345)"
    """
    x, y, z, m = coords
    return f"POINT ZM ({x} {y} {z} {m})"


def wkt_to_spatial_key(wkt: str) -> Tuple[float, float, float, float]:
    """
    Parse PostGIS WKT format back to (X, Y, Z, M) tuple.

    Args:
        wkt: WKT string like "POINT ZM (0.5 0.3 0.8 12345)"

    Returns:
        Tuple of (x, y, z, m) coordinates
    """
    # Remove "POINT ZM (" prefix and ")" suffix
    coords_str = wkt.replace("POINT ZM (", "").replace(")", "")
    x, y, z, m = map(float, coords_str.split())
    return (x, y, z, m)
