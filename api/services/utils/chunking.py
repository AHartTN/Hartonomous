"""
Unified Chunking Utilities

Provides consistent data chunking operations across the codebase.
Eliminates duplication of splitting/batching logic.

Usage Examples:
--------------
Simple chunking:
    ```python
    from api.services.utils.chunking import chunk_list

    chunks = chunk_list(large_list, chunk_size=1000)
    for chunk in chunks:
        process(chunk)
    ```

Numpy array chunking:
    ```python
    from api.services.utils.chunking import chunk_array

    import numpy as np
    data = np.random.randn(50000)
    chunks = chunk_array(data, chunk_size=10000)
    ```

Adaptive chunking (optimal size based on item count):
    ```python
    from api.services.utils.chunking import chunk_adaptive

    chunks = chunk_adaptive(items, target_chunks=10)  # Split into ~10 equal chunks
    ```

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import Iterator, List, Optional, TypeVar

import numpy as np

T = TypeVar("T")


# ============================================================================
# LIST/ITERABLE CHUNKING
# ============================================================================


def chunk_list(items: List[T], chunk_size: int) -> List[List[T]]:
    """
    Split list into fixed-size chunks.

    Args:
        items: List to chunk
        chunk_size: Maximum items per chunk

    Returns:
        List of chunks (last chunk may be smaller)

    Example:
        >>> chunk_list([1,2,3,4,5], 2)
        [[1, 2], [3, 4], [5]]
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    chunks = []
    for i in range(0, len(items), chunk_size):
        chunks.append(items[i : i + chunk_size])
    return chunks


def chunk_list_iter(items: List[T], chunk_size: int) -> Iterator[List[T]]:
    """
    Lazily split list into chunks (memory efficient for large lists).

    Args:
        items: List to chunk
        chunk_size: Maximum items per chunk

    Yields:
        Chunks of items

    Example:
        >>> for chunk in chunk_list_iter(huge_list, 1000):
        ...     process(chunk)
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    for i in range(0, len(items), chunk_size):
        yield items[i : i + chunk_size]


def chunk_adaptive(
    items: List[T],
    target_chunks: int,
    min_chunk_size: int = 1,
    max_chunk_size: Optional[int] = None,
) -> List[List[T]]:
    """
    Split list into approximately N equal chunks.

    Useful when you want to distribute work across N workers.

    Args:
        items: List to chunk
        target_chunks: Desired number of chunks
        min_chunk_size: Minimum items per chunk
        max_chunk_size: Optional maximum items per chunk

    Returns:
        List of approximately equal chunks

    Example:
        >>> chunk_adaptive([1,2,3,4,5,6,7,8,9,10], target_chunks=3)
        [[1, 2, 3, 4], [5, 6, 7], [8, 9, 10]]
    """
    if target_chunks <= 0:
        raise ValueError("target_chunks must be positive")

    if len(items) == 0:
        return []

    # Calculate optimal chunk size
    chunk_size = max(min_chunk_size, len(items) // target_chunks)

    if max_chunk_size is not None:
        chunk_size = min(chunk_size, max_chunk_size)

    return chunk_list(items, chunk_size)


# ============================================================================
# NUMPY ARRAY CHUNKING
# ============================================================================


def chunk_array(arr: np.ndarray, chunk_size: int) -> List[np.ndarray]:
    """
    Split numpy array into fixed-size chunks.

    Args:
        arr: Array to chunk (1D)
        chunk_size: Maximum elements per chunk

    Returns:
        List of array chunks

    Example:
        >>> arr = np.array([1, 2, 3, 4, 5])
        >>> chunks = chunk_array(arr, 2)
        >>> [c.tolist() for c in chunks]
        [[1, 2], [3, 4], [5]]
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    # Flatten if multidimensional
    if arr.ndim > 1:
        arr = arr.flatten()

    chunks = []
    for i in range(0, len(arr), chunk_size):
        chunks.append(arr[i : i + chunk_size])
    return chunks


def chunk_array_iter(arr: np.ndarray, chunk_size: int) -> Iterator[np.ndarray]:
    """
    Lazily split array into chunks (memory efficient).

    Args:
        arr: Array to chunk
        chunk_size: Maximum elements per chunk

    Yields:
        Array chunks
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    if arr.ndim > 1:
        arr = arr.flatten()

    for i in range(0, len(arr), chunk_size):
        yield arr[i : i + chunk_size]


def chunk_tensor_by_shape(
    tensor: np.ndarray, chunk_size: int, preserve_shape: bool = False
) -> List[np.ndarray]:
    """
    Chunk multi-dimensional tensor intelligently.

    Args:
        tensor: Multi-dimensional array
        chunk_size: Elements per chunk (after flattening)
        preserve_shape: If True, return original shape info with chunks

    Returns:
        List of flattened chunks or (chunk, start_idx, end_idx) tuples

    Example:
        >>> tensor = np.random.randn(100, 50)  # 5000 elements
        >>> chunks = chunk_tensor_by_shape(tensor, chunk_size=1000)
        >>> len(chunks)  # 5 chunks of 1000 elements
        5
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    original_shape = tensor.shape
    flat = tensor.flatten()

    if preserve_shape:
        chunks_with_info = []
        for i in range(0, len(flat), chunk_size):
            chunk = flat[i : i + chunk_size]
            chunks_with_info.append(
                (chunk, i, min(i + chunk_size, len(flat)), original_shape)
            )
        return chunks_with_info
    else:
        return chunk_array(flat, chunk_size)


# ============================================================================
# BATCHING HELPERS
# ============================================================================


def create_batches(items: List[T], batch_size: int) -> List[List[T]]:
    """
    Alias for chunk_list (semantic: batching for processing).

    Args:
        items: Items to batch
        batch_size: Items per batch

    Returns:
        List of batches
    """
    return chunk_list(items, batch_size)


def create_batches_with_remainder(
    items: List[T], batch_size: int
) -> tuple[List[List[T]], List[T]]:
    """
    Split into batches with separate remainder handling.

    Args:
        items: Items to batch
        batch_size: Items per batch

    Returns:
        Tuple of (full_batches, remainder)

    Example:
        >>> batches, remainder = create_batches_with_remainder([1,2,3,4,5], 2)
        >>> batches
        [[1, 2], [3, 4]]
        >>> remainder
        [5]
    """
    if batch_size <= 0:
        raise ValueError("batch_size must be positive")

    full_batch_count = len(items) // batch_size
    split_point = full_batch_count * batch_size

    full_batches = chunk_list(items[:split_point], batch_size)
    remainder = items[split_point:]

    return full_batches, remainder


# ============================================================================
# UTILITY FUNCTIONS
# ============================================================================


def calculate_optimal_chunk_size(
    total_items: int, target_memory_mb: float = 5.0, bytes_per_item: int = 4
) -> int:
    """
    Calculate optimal chunk size based on memory constraints.

    Args:
        total_items: Total number of items
        target_memory_mb: Target memory per chunk (MB)
        bytes_per_item: Estimated bytes per item

    Returns:
        Optimal chunk size

    Example:
        >>> calculate_optimal_chunk_size(1000000, target_memory_mb=10, bytes_per_item=4)
        2621440  # ~10MB worth of float32 values
    """
    target_bytes = target_memory_mb * 1024 * 1024
    chunk_size = int(target_bytes / bytes_per_item)

    # Ensure reasonable bounds
    chunk_size = max(100, min(chunk_size, total_items))

    return chunk_size


def get_chunk_count(total_items: int, chunk_size: int) -> int:
    """
    Calculate number of chunks for given total and chunk size.

    Args:
        total_items: Total items
        chunk_size: Size per chunk

    Returns:
        Number of chunks (ceiling division)
    """
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")

    return (total_items + chunk_size - 1) // chunk_size


def distribute_work(total_items: int, num_workers: int) -> List[tuple[int, int]]:
    """
    Calculate start/end indices for distributing work across workers.

    Args:
        total_items: Total items to process
        num_workers: Number of workers

    Returns:
        List of (start, end) index tuples

    Example:
        >>> distribute_work(100, 3)
        [(0, 34), (34, 67), (67, 100)]
    """
    if num_workers <= 0:
        raise ValueError("num_workers must be positive")

    if total_items == 0:
        return []

    items_per_worker = total_items // num_workers
    remainder = total_items % num_workers

    ranges = []
    start = 0
    for i in range(num_workers):
        # Distribute remainder across first workers
        extra = 1 if i < remainder else 0
        end = start + items_per_worker + extra
        if start < total_items:  # Only add if there's work
            ranges.append((start, end))
        start = end

    return ranges
