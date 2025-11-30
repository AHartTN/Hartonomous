"""
Unified Async Executor Utilities

Provides consistent async execution patterns for CPU-bound and I/O-bound operations.
Eliminates duplication of ThreadPoolExecutor/ProcessPoolExecutor setup across codebase.

Usage Examples:
--------------
CPU-bound single operation:
    ```python
    from api.services.utils.async_executor import run_cpu_bound

    result = await run_cpu_bound(expensive_function, arg1, arg2, kwarg=value)
    ```

CPU-bound batch operations:
    ```python
    from api.services.utils.async_executor import run_cpu_batch

    tasks = [(func, args, kwargs), (func2, args2, kwargs2)]
    results = await run_cpu_batch(tasks, max_workers=8)
    ```

True parallel with ProcessPool:
    ```python
    from api.services.utils.async_executor import run_parallel

    # Bypasses GIL for heavy CPU work
    results = await run_parallel(compute_hashes, data_chunks, max_workers=4)
    ```

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import asyncio
import logging
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor
from functools import partial
from typing import Any, Callable, List, Optional, Tuple

logger = logging.getLogger(__name__)


# ============================================================================
# SINGLE OPERATION HELPERS
# ============================================================================


async def run_cpu_bound(
    func: Callable, *args, executor: Optional[ThreadPoolExecutor] = None, **kwargs
) -> Any:
    """
    Run CPU-bound function in thread executor.

    Use for: Hashing, coordinate computation, WKT building, etc.

    Args:
        func: Function to execute
        *args: Positional arguments
        executor: Optional executor (creates default if None)
        **kwargs: Keyword arguments

    Returns:
        Function result
    """
    loop = asyncio.get_event_loop()

    if kwargs:
        # Wrap with partial for kwargs
        func_with_kwargs = partial(func, **kwargs)
        return await loop.run_in_executor(executor, func_with_kwargs, *args)
    else:
        return await loop.run_in_executor(executor, func, *args)


async def run_parallel(
    func: Callable, items: List[Any], max_workers: Optional[int] = None
) -> List[Any]:
    """
    Run function on multiple items using ProcessPoolExecutor.

    Bypasses GIL for true parallel CPU execution.
    Use for: Heavy computation, large batch hashing.

    Args:
        func: Function to map over items
        items: List of items to process
        max_workers: Number of processes (defaults to CPU count)

    Returns:
        List of results in same order as items
    """
    loop = asyncio.get_event_loop()

    with ProcessPoolExecutor(max_workers=max_workers) as executor:
        tasks = [loop.run_in_executor(executor, func, item) for item in items]
        return await asyncio.gather(*tasks)


# ============================================================================
# BATCH OPERATION HELPERS
# ============================================================================


async def run_cpu_batch(
    tasks: List[Tuple[Callable, Tuple, dict]],
    max_workers: int = 8,
    executor: Optional[ThreadPoolExecutor] = None,
) -> List[Any]:
    """
    Run multiple CPU-bound tasks concurrently with ThreadPoolExecutor.

    Use for: Concurrent chunk processing, parallel coordinate calculations.

    Args:
        tasks: List of (function, args, kwargs) tuples
        max_workers: Maximum concurrent tasks
        executor: Optional executor instance

    Returns:
        List of results in same order as tasks
    """
    loop = asyncio.get_event_loop()

    should_close = False
    if executor is None:
        executor = ThreadPoolExecutor(max_workers=max_workers)
        should_close = True

    try:
        async_tasks = []
        for func, args, kwargs in tasks:
            if kwargs:
                func_with_kwargs = partial(func, **kwargs)
                async_tasks.append(
                    loop.run_in_executor(executor, func_with_kwargs, *args)
                )
            else:
                async_tasks.append(loop.run_in_executor(executor, func, *args))

        return await asyncio.gather(*async_tasks)
    finally:
        if should_close:
            executor.shutdown(wait=False)


async def run_cpu_batch_simple(
    func: Callable, items: List[Any], max_workers: int = 8
) -> List[Any]:
    """
    Run same function on multiple items concurrently (simplified).

    Use for: Processing chunks, generating coordinates for atom list.

    Args:
        func: Function to apply to each item
        items: List of items to process
        max_workers: Maximum concurrent tasks

    Returns:
        List of results in same order as items
    """
    loop = asyncio.get_event_loop()

    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        tasks = [loop.run_in_executor(executor, func, item) for item in items]
        return await asyncio.gather(*tasks)


# ============================================================================
# CONTEXT MANAGER FOR REUSABLE EXECUTOR
# ============================================================================


class AsyncExecutorContext:
    """
    Context manager for reusable ThreadPoolExecutor.

    Use when processing many items and want to reuse executor.

    Example:
        ```python
        async with AsyncExecutorContext(max_workers=8) as ctx:
            result1 = await ctx.run(func1, arg1)
            result2 = await ctx.run(func2, arg2)
            results = await ctx.run_batch([(func3, (arg3,), {}), ...])
        ```
    """

    def __init__(self, max_workers: int = 8):
        self.max_workers = max_workers
        self.executor: Optional[ThreadPoolExecutor] = None

    async def __aenter__(self):
        self.executor = ThreadPoolExecutor(max_workers=self.max_workers)
        return self

    async def __aexit__(self, *_):
        if self.executor:
            self.executor.shutdown(wait=False)

    async def run(self, func: Callable, *args, **kwargs) -> Any:
        """Run single function in executor."""
        return await run_cpu_bound(func, *args, executor=self.executor, **kwargs)

    async def run_batch(self, tasks: List[Tuple[Callable, Tuple, dict]]) -> List[Any]:
        """Run batch of functions in executor."""
        return await run_cpu_batch(
            tasks, max_workers=self.max_workers, executor=self.executor
        )

    async def run_simple(self, func: Callable, items: List[Any]) -> List[Any]:
        """Run same function on multiple items."""
        loop = asyncio.get_event_loop()
        tasks = [loop.run_in_executor(self.executor, func, item) for item in items]
        return await asyncio.gather(*tasks)


# ============================================================================
# CONVENIENCE HELPERS
# ============================================================================


def get_optimal_workers(item_count: int, max_workers: int = 8) -> int:
    """
    Calculate optimal worker count based on item count.

    Args:
        item_count: Number of items to process
        max_workers: Maximum workers to use

    Returns:
        Optimal worker count (min of item_count and max_workers)
    """
    return min(item_count, max_workers)


async def run_with_optimal_workers(
    func: Callable, items: List[Any], max_workers: int = 8
) -> List[Any]:
    """
    Run function with automatically calculated optimal worker count.

    Args:
        func: Function to map over items
        items: List of items to process
        max_workers: Maximum workers

    Returns:
        List of results
    """
    optimal = get_optimal_workers(len(items), max_workers)
    return await run_cpu_batch_simple(func, items, max_workers=optimal)
