"""
Database Query Utilities

Consolidates repetitive async database query patterns to reduce boilerplate.
Eliminates duplication of cursor management, fetchone/fetchall, and error handling.

Usage:
    from api.services.utils import query_one, query_many, execute_query

    # Fetch single row
    row = await query_one(conn, "SELECT * FROM atom WHERE id = %s", (atom_id,))

    # Fetch multiple rows
    rows = await query_many(conn, "SELECT * FROM atom WHERE parent_id = %s", (parent_id,))

    # Execute update/insert
    await execute_query(conn, "UPDATE atom SET metadata = %s WHERE id = %s", (metadata, atom_id))
"""

from typing import Any, List, Optional, Tuple

from psycopg import AsyncConnection


async def query_one(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> Optional[Tuple]:
    """
    Execute query and fetch one row.

    Consolidates the pattern:
        async with conn.cursor() as cursor:
            await cursor.execute(query, params)
            row = await cursor.fetchone()

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Returns:
        Single row tuple or None if not found

    Example:
        row = await query_one(conn, "SELECT id, name FROM atom WHERE id = %s", (123,))
        if row:
            atom_id, name = row
    """
    async with conn.cursor() as cursor:
        await cursor.execute(query, params)
        return await cursor.fetchone()


async def query_many(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> List[Tuple]:
    """
    Execute query and fetch all rows.

    Consolidates the pattern:
        async with conn.cursor() as cursor:
            await cursor.execute(query, params)
            rows = await cursor.fetchall()

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Returns:
        List of row tuples (empty list if no results)

    Example:
        rows = await query_many(conn, "SELECT id, name FROM atom WHERE parent_id = %s", (parent_id,))
        for atom_id, name in rows:
            print(f"Atom {atom_id}: {name}")
    """
    async with conn.cursor() as cursor:
        await cursor.execute(query, params)
        return await cursor.fetchall()


async def query_scalar(
    conn: AsyncConnection,
    query: str,
    params: Optional[Tuple] = None,
    default: Any = None,
) -> Any:
    """
    Execute query and fetch single scalar value.

    Useful for COUNT(*), MAX(), or single-column queries.

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)
        default: Default value if no result (None by default)

    Returns:
        Single scalar value or default if not found

    Example:
        count = await query_scalar(conn, "SELECT COUNT(*) FROM atom WHERE parent_id = %s", (parent_id,))
        max_id = await query_scalar(conn, "SELECT MAX(id) FROM atom", default=0)
    """
    row = await query_one(conn, query, params)
    return row[0] if row else default


async def execute_query(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> None:
    """
    Execute query without fetching results (INSERT, UPDATE, DELETE).

    Consolidates the pattern:
        async with conn.cursor() as cursor:
            await cursor.execute(query, params)

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Example:
        await execute_query(conn, "UPDATE atom SET metadata = %s WHERE id = %s", (metadata, atom_id))
        await execute_query(conn, "DELETE FROM composition WHERE parent_id = %s", (parent_id,))
    """
    async with conn.cursor() as cursor:
        await cursor.execute(query, params)


async def query_one_returning(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> Optional[Any]:
    """
    Execute INSERT/UPDATE with RETURNING clause and get single value.

    Common pattern for getting auto-generated IDs.

    Args:
        conn: Database connection
        query: SQL query with RETURNING clause
        params: Query parameters (optional)

    Returns:
        Single returned value or None

    Example:
        atom_id = await query_one_returning(
            conn,
            "INSERT INTO atom (content_hash, canonical_text) VALUES (%s, %s) RETURNING id",
            (hash_val, text)
        )
    """
    row = await query_one(conn, query, params)
    return row[0] if row else None


async def query_exists(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> bool:
    """
    Check if query returns any rows.

    More efficient than fetching all rows just to check existence.

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Returns:
        True if query returns at least one row, False otherwise

    Example:
        exists = await query_exists(conn, "SELECT 1 FROM atom WHERE content_hash = %s", (hash_val,))
        if not exists:
            # Insert new atom
    """
    row = await query_one(conn, query, params)
    return row is not None


async def query_batch(
    conn: AsyncConnection, query: str, param_batches: List[Tuple]
) -> None:
    """
    Execute same query with multiple parameter sets.

    More efficient than individual execute calls.

    Args:
        conn: Database connection
        query: SQL query string
        param_batches: List of parameter tuples

    Example:
        await query_batch(
            conn,
            "UPDATE atom SET metadata = %s WHERE id = %s",
            [(metadata1, id1), (metadata2, id2), (metadata3, id3)]
        )
    """
    async with conn.cursor() as cursor:
        for params in param_batches:
            await cursor.execute(query, params)


async def query_dict_one(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> Optional[dict]:
    """
    Execute query and fetch one row as dictionary.

    Requires column names in query or uses cursor description.

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Returns:
        Single row as dict or None if not found

    Example:
        atom = await query_dict_one(conn, "SELECT id, name, metadata FROM atom WHERE id = %s", (123,))
        if atom:
            print(f"Atom {atom['id']}: {atom['name']}")
    """
    async with conn.cursor() as cursor:
        await cursor.execute(query, params)
        row = await cursor.fetchone()

        if row is None:
            return None

        columns = [desc[0] for desc in cursor.description]
        return dict(zip(columns, row))


async def query_dict_many(
    conn: AsyncConnection, query: str, params: Optional[Tuple] = None
) -> List[dict]:
    """
    Execute query and fetch all rows as dictionaries.

    Args:
        conn: Database connection
        query: SQL query string
        params: Query parameters (optional)

    Returns:
        List of row dicts (empty list if no results)

    Example:
        atoms = await query_dict_many(conn, "SELECT id, name FROM atom WHERE parent_id = %s", (parent_id,))
        for atom in atoms:
            print(f"Atom {atom['id']}: {atom['name']}")
    """
    async with conn.cursor() as cursor:
        await cursor.execute(query, params)
        rows = await cursor.fetchall()

        if not rows:
            return []

        columns = [desc[0] for desc in cursor.description]
        return [dict(zip(columns, row)) for row in rows]
