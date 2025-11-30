"""
Utility Modules for Hartonomous Services

Centralized helper functions to eliminate code duplication.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from .async_executor import (
    AsyncExecutorContext,
    get_optimal_workers,
    run_cpu_batch,
    run_cpu_batch_simple,
    run_cpu_bound,
    run_parallel,
    run_with_optimal_workers,
)
from .chunking import (
    calculate_optimal_chunk_size,
    chunk_adaptive,
    chunk_array,
    chunk_array_iter,
    chunk_list,
    chunk_list_iter,
    chunk_tensor_by_shape,
    create_batches,
    create_batches_with_remainder,
    distribute_work,
    get_chunk_count,
)
from .db_query_utils import (
    execute_query,
    query_batch,
    query_dict_many,
    query_dict_one,
    query_exists,
    query_many,
    query_one,
    query_one_returning,
    query_scalar,
)

__all__ = [
    # Async execution
    "run_cpu_bound",
    "run_parallel",
    "run_cpu_batch",
    "run_cpu_batch_simple",
    "run_with_optimal_workers",
    "get_optimal_workers",
    "AsyncExecutorContext",
    # Chunking
    "chunk_list",
    "chunk_list_iter",
    "chunk_adaptive",
    "chunk_array",
    "chunk_array_iter",
    "chunk_tensor_by_shape",
    "create_batches",
    "create_batches_with_remainder",
    "calculate_optimal_chunk_size",
    "get_chunk_count",
    "distribute_work",
    # Database queries
    "query_one",
    "query_many",
    "query_scalar",
    "execute_query",
    "query_one_returning",
    "query_exists",
    "query_batch",
    "query_dict_one",
    "query_dict_many",
]
