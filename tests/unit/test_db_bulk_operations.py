"""
Unit tests for database bulk operations utility.

Tests the BulkCopyOperation class and convenience functions for
proper encapsulation, error handling, and performance characteristics.
"""

import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from api.services.db_bulk_operations import (
    BulkCopyOperation,
    bulk_insert_atoms,
    bulk_insert_compositions,
    bulk_retrieve_by_hashes,
)


class TestBulkCopyOperation:
    """Test BulkCopyOperation class for proper separation of concerns."""

    @pytest.fixture
    def mock_conn(self):
        """Create mock database connection."""
        conn = AsyncMock()
        cursor = AsyncMock()
        copy_ctx = AsyncMock()

        # Properly mock async context managers
        cursor_cm = AsyncMock()
        cursor_cm.__aenter__ = AsyncMock(return_value=cursor)
        cursor_cm.__aexit__ = AsyncMock(return_value=None)
        conn.cursor = MagicMock(return_value=cursor_cm)

        copy_cm = AsyncMock()
        copy_cm.__aenter__ = AsyncMock(return_value=copy_ctx)
        copy_cm.__aexit__ = AsyncMock(return_value=None)
        cursor.copy = MagicMock(return_value=copy_cm)

        return conn, cursor, copy_ctx

    @pytest.mark.asyncio
    async def test_binary_copy_basic(self, mock_conn):
        """Test basic binary COPY operation."""
        conn, cursor, copy_ctx = mock_conn

        operation = BulkCopyOperation(
            conn,
            "COPY test_table (col1, col2) FROM STDIN",
            chunk_size=2,
            show_progress=False,
        )

        rows = [(1, "a"), (2, "b"), (3, "c"), (4, "d")]
        elapsed = await operation.execute_binary(rows, "Test insert")

        # Verify COPY was called with correct statement
        cursor.copy.assert_called_once_with("COPY test_table (col1, col2) FROM STDIN")

        # Verify all rows were written
        assert copy_ctx.write_row.call_count == 4
        assert elapsed > 0

        # Verify stats
        stats = operation.get_stats()
        assert stats["total_rows"] == 4
        assert stats["chunk_size"] == 2

    @pytest.mark.asyncio
    async def test_text_copy_with_formatter(self, mock_conn):
        """Test text COPY operation with TSV formatter."""
        conn, cursor, copy_ctx = mock_conn

        operation = BulkCopyOperation(
            conn,
            "COPY test_table (id, name) FROM STDIN",
            chunk_size=2,
            show_progress=False,
        )

        def format_tsv(batch):
            return "".join(f"{r[0]}\t{r[1]}\n" for r in batch)

        rows = [(1, "a"), (2, "b"), (3, "c")]
        elapsed = await operation.execute_text(rows, format_tsv, "Test insert")

        # Verify write was called for each chunk (2 chunks: 0-2, 2-3)
        assert copy_ctx.write.call_count == 2

        # Verify formatted content
        call_args = [call[0][0] for call in copy_ctx.write.call_args_list]
        assert "1\ta\n2\tb\n" in call_args[0]
        assert "3\tc\n" in call_args[1]

    @pytest.mark.asyncio
    async def test_chunking_behavior(self, mock_conn):
        """Test that chunking works correctly."""
        conn, cursor, copy_ctx = mock_conn

        operation = BulkCopyOperation(
            conn, "COPY test_table (val) FROM STDIN", chunk_size=3, show_progress=False
        )

        # 10 rows with chunk_size=3 should result in 4 chunks
        rows = [(i,) for i in range(10)]
        await operation.execute_binary(rows, "Test")

        # Verify all rows written
        assert copy_ctx.write_row.call_count == 10

    def test_stats_before_execution(self):
        """Test that stats are empty before execution."""
        conn = AsyncMock()
        operation = BulkCopyOperation(conn, "COPY test FROM STDIN")

        stats = operation.get_stats()
        assert stats == {}


class TestConvenienceFunctions:
    """Test convenience functions for common patterns."""

    @pytest.fixture
    def mock_conn(self):
        """Create mock database connection with result fetching."""
        conn = AsyncMock()
        cursor = AsyncMock()
        copy_ctx = AsyncMock()

        # Properly mock async context managers
        cursor_cm = AsyncMock()
        cursor_cm.__aenter__ = AsyncMock(return_value=cursor)
        cursor_cm.__aexit__ = AsyncMock(return_value=None)
        conn.cursor = MagicMock(return_value=cursor_cm)

        copy_cm = AsyncMock()
        copy_cm.__aenter__ = AsyncMock(return_value=copy_ctx)
        copy_cm.__aexit__ = AsyncMock(return_value=None)
        cursor.copy = MagicMock(return_value=copy_cm)

        # Mock fetchall for retrieve operations
        cursor.fetchall.return_value = [(b"hash1", 101), (b"hash2", 102)]

        return conn, cursor, copy_ctx

    @pytest.mark.asyncio
    async def test_bulk_insert_atoms_without_spatial(self, mock_conn):
        """Test atom insertion without spatial keys."""
        conn, cursor, copy_ctx = mock_conn

        rows = [
            (b"hash1", "text1", '{"key": "val1"}'),
            (b"hash2", "text2", '{"key": "val2"}'),
        ]

        elapsed, results = await bulk_insert_atoms(
            conn, rows, include_spatial=False, show_progress=False
        )

        # Verify correct COPY statement (no spatial_key)
        cursor.copy.assert_called()
        copy_stmt = cursor.copy.call_args[0][0]
        assert "spatial_key" not in copy_stmt
        assert "content_hash, canonical_text, metadata" in copy_stmt

        # Verify results retrieved
        assert len(results) == 2
        assert results[0] == (b"hash1", 101)

    @pytest.mark.asyncio
    async def test_bulk_insert_atoms_with_spatial(self, mock_conn):
        """Test atom insertion with spatial keys."""
        conn, cursor, copy_ctx = mock_conn

        rows = [
            (b"hash1", "text1", '{"key": "val1"}', "POINT(1 2)"),
        ]

        elapsed, results = await bulk_insert_atoms(
            conn, rows, include_spatial=True, show_progress=False
        )

        # Verify correct COPY statement (includes spatial_key)
        copy_stmt = cursor.copy.call_args[0][0]
        assert "spatial_key" in copy_stmt

    @pytest.mark.asyncio
    async def test_bulk_insert_compositions(self, mock_conn):
        """Test composition insertion with TSV formatting."""
        conn, cursor, copy_ctx = mock_conn

        parent_id = 100
        component_ids = [1, 2, 3]
        sequence_indices = [0, 1, 2]

        elapsed = await bulk_insert_compositions(
            conn,
            parent_id,
            component_ids,
            sequence_indices,
            chunk_size=2,
            show_progress=False,
        )

        # Verify COPY statement
        copy_stmt = cursor.copy.call_args[0][0]
        assert "atom_composition" in copy_stmt

        # Verify TSV write was called
        assert copy_ctx.write.call_count > 0

        # Verify TSV format contains parent_id
        written_data = copy_ctx.write.call_args_list[0][0][0]
        assert "100\t" in written_data

    @pytest.mark.asyncio
    async def test_bulk_retrieve_by_hashes_batching(self, mock_conn):
        """Test hash retrieval with proper batching."""
        conn, cursor, copy_ctx = mock_conn

        # Create more hashes than batch_size to test batching
        hashes = [f"hash{i}".encode() for i in range(150)]

        results = await bulk_retrieve_by_hashes(conn, hashes, batch_size=50)

        # Should execute 3 queries (150 hashes / 50 batch_size)
        assert cursor.execute.call_count == 3

        # Verify results aggregated correctly
        assert len(results) == 6  # 3 batches × 2 results per batch (from mock)


class TestErrorHandling:
    """Test error handling and edge cases."""

    @pytest.mark.asyncio
    async def test_empty_rows(self):
        """Test handling of empty row list."""
        conn = AsyncMock()
        cursor = AsyncMock()
        copy_ctx = AsyncMock()

        # Properly mock async context managers
        cursor_cm = AsyncMock()
        cursor_cm.__aenter__ = AsyncMock(return_value=cursor)
        cursor_cm.__aexit__ = AsyncMock(return_value=None)
        conn.cursor = MagicMock(return_value=cursor_cm)

        copy_cm = AsyncMock()
        copy_cm.__aenter__ = AsyncMock(return_value=copy_ctx)
        copy_cm.__aexit__ = AsyncMock(return_value=None)
        cursor.copy = MagicMock(return_value=copy_cm)

        operation = BulkCopyOperation(conn, "COPY test FROM STDIN", show_progress=False)

        # Should handle empty list gracefully
        elapsed = await operation.execute_binary([], "Empty")
        assert elapsed >= 0
        assert copy_ctx.write_row.call_count == 0

    @pytest.mark.asyncio
    async def test_single_row(self):
        """Test handling of single row."""
        conn = AsyncMock()
        cursor = AsyncMock()
        copy_ctx = AsyncMock()

        # Properly mock async context managers
        cursor_cm = AsyncMock()
        cursor_cm.__aenter__ = AsyncMock(return_value=cursor)
        cursor_cm.__aexit__ = AsyncMock(return_value=None)
        conn.cursor = MagicMock(return_value=cursor_cm)

        copy_cm = AsyncMock()
        copy_cm.__aenter__ = AsyncMock(return_value=copy_ctx)
        copy_cm.__aexit__ = AsyncMock(return_value=None)
        cursor.copy = MagicMock(return_value=copy_cm)

        operation = BulkCopyOperation(conn, "COPY test FROM STDIN", show_progress=False)

        elapsed = await operation.execute_binary([(1,)], "Single")
        assert elapsed >= 0
        assert copy_ctx.write_row.call_count == 1


class TestPerformanceCharacteristics:
    """Test performance-related behavior."""

    def test_stats_calculation(self):
        """Test that stats calculate correct rates."""
        conn = AsyncMock()
        operation = BulkCopyOperation(conn, "COPY test FROM STDIN")

        # Simulate execution
        operation.total_rows = 10000
        operation.start_time = 0.0

        with patch("time.time", return_value=1.0):  # 1 second elapsed
            stats = operation.get_stats()

        assert stats["total_rows"] == 10000
        assert stats["elapsed_seconds"] == 1.0
        assert stats["rows_per_second"] == 10000.0

    @pytest.mark.asyncio
    async def test_progress_bar_toggle(self):
        """Test that progress bar can be disabled."""
        conn = AsyncMock()
        cursor = AsyncMock()
        copy_ctx = AsyncMock()

        # Properly mock async context managers
        cursor_cm = AsyncMock()
        cursor_cm.__aenter__ = AsyncMock(return_value=cursor)
        cursor_cm.__aexit__ = AsyncMock(return_value=None)
        conn.cursor = MagicMock(return_value=cursor_cm)

        copy_cm = AsyncMock()
        copy_cm.__aenter__ = AsyncMock(return_value=copy_ctx)
        copy_cm.__aexit__ = AsyncMock(return_value=None)
        cursor.copy = MagicMock(return_value=copy_cm)

        # With progress
        op_with_progress = BulkCopyOperation(
            conn, "COPY test FROM STDIN", show_progress=True
        )

        # Without progress
        op_without_progress = BulkCopyOperation(
            conn, "COPY test FROM STDIN", show_progress=False
        )

        # Both should work
        rows = [(1,), (2,)]
        await op_with_progress.execute_binary(rows, "Test")
        await op_without_progress.execute_binary(rows, "Test")
