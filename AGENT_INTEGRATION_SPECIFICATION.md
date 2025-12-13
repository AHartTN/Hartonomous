# AGENT INTEGRATION SPECIFICATION

**Document Version:** 1.0
**Date:** 2025-12-13
**Status:** Implementation Specification
**Dependencies:** HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md, CORTEX_IMPLEMENTATION_SPECIFICATION.md

---

## Executive Summary

This specification defines the integration layer for autonomous operation of Hartonomous. **CRITICAL PARADIGM**: The database spatial index IS the AI. There are no external AI frameworks, no LLMs, no neural networks. Intelligence emerges from geometric organization:

- **Inference = Spatial traversal** (k-NN queries, radius searches)
- **Reasoning = Geometric relationships** (proximity = semantic similarity)
- **Learning = Cortex recalibration** (atoms move in space)
- **Memory = Persistent geometry** (PostgreSQL + PostGIS)

The Python integration layer is a **database connector**, not an AI orchestrator. It translates high-level operations into spatial SQL queries. The intelligence lives in the database geometry.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│            Application Interface                    │
│              (Python Client API)                    │
│  - Task decomposition into spatial queries         │
│  - Result aggregation and formatting                │
└────────────────┬────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────────┐
│         HartonomousConnector                        │
│         (Python - Database Client)                  │
│  - PostgreSQL connection pooling                    │
│  - Query composition and execution                  │
│  - Transaction management                           │
└────────────────┬────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────────┐
│    THE INTELLIGENCE (PostgreSQL + PostGIS)          │
│                                                     │
│  Inference Operations:                              │
│  - k-NN: ORDER BY geom <-> target LIMIT k           │
│  - Radius: ST_DWithin(geom, target, radius)         │
│  - Trajectory: Fréchet distance pattern matching    │
│  - Hierarchy: ST_3DDistance with Z filtering        │
│  - Salience: M dimension weighting                  │
│                                                     │
│  Learning (Continuous):                             │
│  - Cortex background worker                         │
│  - LMDS geometric projection                        │
│  - Stress-based recalibration                       │
│                                                     │
│  Memory (Persistent):                               │
│  - atom table                            │
│  - GiST spatial index                               │
│  - 4D XYZM semantic manifold                        │
└─────────────────────────────────────────────────────┘
```

---

## Core Principle: Database as Intelligence

### Traditional AI vs. Hartonomous

| Aspect | Traditional AI | Hartonomous |
|--------|---------------|-------------|
| **Intelligence** | Neural network weights (volatile) | Database geometry (persistent) |
| **Inference** | Forward pass through layers | Spatial query (k-NN, radius search) |
| **Learning** | Backpropagation, gradient descent | LMDS geometric recalibration |
| **Memory** | Separate vector database | Same database (PostgreSQL) |
| **Reasoning** | LLM token generation | Geometric traversal |
| **Storage** | Passive, separate system | Active, IS the intelligence |

### Why This Matters

**No model loading:** Database is always ready. No cold starts.
**No embedding API calls:** Coordinates computed once, indexed permanently.
**No vector DB sync:** Semantic and storage are unified.
**No context windows:** Entire dataset spatially indexed.
**No hallucinations:** Queries return actual stored data.
**No training/inference split:** Cortex continuously refines geometry.

---

## Phase 1: Database Connection Infrastructure

### 1.1 Connection Pool Setup

**File:** `connector/pool.py`

```python
import psycopg2
from psycopg2 import pool
from contextlib import contextmanager
from typing import Generator
import os

class HartonomousPool:
    """PostgreSQL connection pool for Hartonomous database."""

    def __init__(
        self,
        minconn: int = 2,
        maxconn: int = 10,
        host: str = None,
        port: int = 5432,
        dbname: str = "hartonomous",
        user: str = None,
        password: str = None
    ):
        """Initialize connection pool.

        Args:
            minconn: Minimum connections in pool
            maxconn: Maximum connections in pool
            host: PostgreSQL host (default: localhost)
            port: PostgreSQL port (default: 5432)
            dbname: Database name (default: hartonomous)
            user: Database user (default: from env PGUSER)
            password: Database password (default: from env PGPASSWORD)
        """
        self.pool = psycopg2.pool.ThreadedConnectionPool(
            minconn,
            maxconn,
            host=host or os.getenv('PGHOST', 'localhost'),
            port=port,
            dbname=dbname,
            user=user or os.getenv('PGUSER'),
            password=password or os.getenv('PGPASSWORD'),
            # Enable PostGIS and fast binary protocol
            options='-c search_path=public,postgis'
        )

    @contextmanager
    def connection(self) -> Generator:
        """Get connection from pool with automatic cleanup.

        Yields:
            psycopg2 connection with autocommit disabled
        """
        conn = self.pool.getconn()
        try:
            yield conn
            conn.commit()
        except Exception:
            conn.rollback()
            raise
        finally:
            self.pool.putconn(conn)

    def close(self):
        """Close all connections in pool."""
        self.pool.closeall()
```

**Rationale:**
- Thread-safe connection pooling for concurrent queries
- Context manager ensures connections always returned to pool
- Automatic transaction management (commit on success, rollback on error)
- Environment variable configuration for deployment flexibility

### 1.2 Core Connector Class

**File:** `connector/core.py`

```python
from typing import List, Tuple, Optional, Dict, Any
from dataclasses import dataclass
import struct
from .pool import HartonomousPool

@dataclass
class Atom:
    """Represents a single atom in 4D semantic space."""
    atom_hash: bytes  # 32-byte BLAKE3 hash (SDI)
    x: float          # Semantic coordinate
    y: float          # Semantic coordinate
    z: float          # Hierarchy level
    m: float          # Salience/frequency
    atom_class: int   # 0=Constant, 1=Composition
    modality: int     # Data type identifier
    metadata: Optional[Dict[str, Any]] = None

class HartonomousConnector:
    """Database connector for Hartonomous spatial queries.

    This is NOT an AI orchestrator. This is a database client that
    translates operations into spatial SQL queries. The intelligence
    lives in the PostgreSQL spatial index.
    """

    def __init__(self, pool: HartonomousPool):
        """Initialize connector with connection pool.

        Args:
            pool: HartonomousPool instance for database access
        """
        self.pool = pool

    def _execute_query(
        self,
        query: str,
        params: Tuple = None,
        fetch: str = 'all'
    ) -> List[Tuple]:
        """Execute SQL query with connection from pool.

        Args:
            query: SQL query string (use %s placeholders)
            params: Query parameters (prevents SQL injection)
            fetch: 'all', 'one', or 'none'

        Returns:
            Query results as list of tuples
        """
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, params)
                if fetch == 'all':
                    return cur.fetchall()
                elif fetch == 'one':
                    return cur.fetchone()
                else:
                    return []

    def insert_atom(self, atom: Atom) -> bool:
        """Insert single atom into database.

        Args:
            atom: Atom instance with all required fields

        Returns:
            True if inserted, False if duplicate (hash collision)
        """
        query = """
        INSERT INTO atom (
            atom_hash, geom, atom_class, modality, metadata
        ) VALUES (
            %s,
            ST_MakePoint(%s, %s, %s, %s)::geometry(POINTZM,4326),
            %s,
            %s,
            %s
        ) ON CONFLICT (atom_hash) DO NOTHING
        RETURNING atom_hash;
        """
        result = self._execute_query(
            query,
            (
                atom.atom_hash,
                atom.x, atom.y, atom.z, atom.m,
                atom.atom_class,
                atom.modality,
                atom.metadata
            ),
            fetch='one'
        )
        return result is not None

    def bulk_insert_atoms(self, atoms: List[Atom]) -> int:
        """Bulk insert atoms using COPY protocol.

        Args:
            atoms: List of Atom instances

        Returns:
            Number of atoms inserted (excluding duplicates)
        """
        # This would use psycopg2.extras.execute_values for efficiency
        # or io.StringIO with cursor.copy_from for maximum throughput
        # Implementation detail: format atoms as CSV/TSV for COPY
        # PostgreSQL COPY protocol is 10-100x faster than INSERT
        pass  # See SHADER_IMPLEMENTATION_SPECIFICATION.md for COPY details
```

---

## Phase 2: Inference Operations (Spatial Queries)

### 2.1 k-Nearest Neighbors (Core Inference)

**Semantic meaning:** "Find concepts most similar to this concept"

**Implementation:**

```python
class HartonomousConnector:
    # ... (previous methods)

    def find_similar(
        self,
        atom_hash: bytes,
        k: int = 10,
        hierarchy_filter: Optional[Tuple[float, float]] = None,
        salience_threshold: Optional[float] = None
    ) -> List[Atom]:
        """Find k most semantically similar atoms via spatial proximity.

        THIS IS INFERENCE. No LLM calls. No embeddings API.
        The spatial index returns semantically related concepts.

        Args:
            atom_hash: Hash of query atom (must exist in database)
            k: Number of similar atoms to return
            hierarchy_filter: Optional (z_min, z_max) to filter by abstraction level
            salience_threshold: Optional minimum M value (filter by importance)

        Returns:
            List of k nearest Atom instances, ordered by distance
        """
        # Build query dynamically based on filters
        query = """
        WITH target AS (
            SELECT geom FROM atom WHERE atom_hash = %s
        )
        SELECT
            ua.atom_hash,
            ST_X(ua.geom) as x,
            ST_Y(ua.geom) as y,
            ST_Z(ua.geom) as z,
            ST_M(ua.geom) as m,
            ua.atom_class,
            ua.modality,
            ua.metadata,
            ua.geom <-> target.geom as distance
        FROM atom ua, target
        WHERE ua.atom_hash != %s  -- Exclude query atom itself
        """

        params = [atom_hash, atom_hash]

        # Add hierarchy filter if specified
        if hierarchy_filter:
            query += " AND ST_Z(ua.geom) BETWEEN %s AND %s"
            params.extend(hierarchy_filter)

        # Add salience filter if specified
        if salience_threshold:
            query += " AND ST_M(ua.geom) >= %s"
            params.append(salience_threshold)

        # Order by spatial distance (THIS IS THE INFERENCE)
        query += """
        ORDER BY ua.geom <-> target.geom
        LIMIT %s;
        """
        params.append(k)

        rows = self._execute_query(query, tuple(params))

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]

    def find_similar_by_coords(
        self,
        x: float, y: float, z: float, m: float,
        k: int = 10
    ) -> List[Atom]:
        """Find k nearest atoms to arbitrary point in 4D space.

        Use case: Query with synthetic coordinates (e.g., interpolation,
        hypothetical concept locations).

        Args:
            x, y, z, m: Coordinates in 4D semantic space
            k: Number of nearest atoms to return

        Returns:
            List of k nearest Atom instances
        """
        query = """
        SELECT
            atom_hash,
            ST_X(geom) as x,
            ST_Y(geom) as y,
            ST_Z(geom) as z,
            ST_M(geom) as m,
            atom_class,
            modality,
            metadata
        FROM atom
        ORDER BY geom <-> ST_MakePoint(%s, %s, %s, %s)::geometry(POINTZM,4326)
        LIMIT %s;
        """

        rows = self._execute_query(query, (x, y, z, m, k))

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
```

**Why this is intelligence:**
- GiST index organizes atoms by semantic meaning (via LMDS)
- Spatial proximity = semantic similarity (learned by Cortex)
- k-NN query = "what concepts are related to this concept?"
- No neural network forward pass required
- Sub-millisecond latency via O(log N) R-Tree traversal

### 2.2 Radius Search (Semantic Neighborhood)

**Semantic meaning:** "Find all concepts within semantic distance D"

**Implementation:**

```python
class HartonomousConnector:
    # ... (previous methods)

    def find_within_radius(
        self,
        atom_hash: bytes,
        radius: float,
        max_results: Optional[int] = None
    ) -> List[Atom]:
        """Find all atoms within semantic radius.

        THIS IS REASONING. The radius defines a "semantic neighborhood" -
        all concepts closely related to the query concept.

        Args:
            atom_hash: Hash of center atom
            radius: Maximum Euclidean distance in 4D space
            max_results: Optional limit on results (for very large neighborhoods)

        Returns:
            List of atoms within radius, unordered
        """
        query = """
        WITH target AS (
            SELECT geom FROM atom WHERE atom_hash = %s
        )
        SELECT
            ua.atom_hash,
            ST_X(ua.geom) as x,
            ST_Y(ua.geom) as y,
            ST_Z(ua.geom) as z,
            ST_M(ua.geom) as m,
            ua.atom_class,
            ua.modality,
            ua.metadata
        FROM atom ua, target
        WHERE ua.atom_hash != %s
          AND ST_3DDistance(ua.geom, target.geom) <= %s
        """

        params = [atom_hash, atom_hash, radius]

        if max_results:
            query += " LIMIT %s"
            params.append(max_results)

        query += ";"

        rows = self._execute_query(query, tuple(params))

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
```

**Use cases:**
- Semantic clustering: "Find all atoms in this concept cluster"
- Contextual retrieval: "Get neighborhood around this idea"
- Anomaly detection: "No nearby atoms = novel/outlier concept"

### 2.3 Trajectory Matching (Pattern Recognition)

**Semantic meaning:** "Find sequences similar to this sequence"

**Implementation:**

```python
class HartonomousConnector:
    # ... (previous methods)

    def find_similar_trajectories(
        self,
        query_sequence: List[bytes],  # List of atom_hashes in order
        k: int = 10,
        frechet_threshold: Optional[float] = None
    ) -> List[Tuple[List[bytes], float]]:
        """Find compositions with similar sequential patterns.

        THIS IS PATTERN RECOGNITION. Uses Fréchet distance to match
        trajectories in 4D space (sequences of atoms = paths through space).

        Args:
            query_sequence: Ordered list of atom hashes forming trajectory
            k: Number of similar trajectories to return
            frechet_threshold: Optional maximum Fréchet distance

        Returns:
            List of (trajectory_atom_hashes, frechet_distance) tuples
        """
        # First, construct query trajectory geometry
        query = """
        WITH query_traj AS (
            SELECT ST_MakeLine(
                ARRAY(
                    SELECT geom
                    FROM atom
                    WHERE atom_hash = ANY(%s)
                    ORDER BY array_position(%s, atom_hash)
                )
            ) AS geom
        ),
        candidate_comps AS (
            -- Find all Compositions (atom_class = 1)
            SELECT
                atom_hash,
                geom
            FROM atom
            WHERE atom_class = 1  -- Compositions only
        )
        SELECT
            cc.atom_hash,
            ST_FrechetDistance(cc.geom, qt.geom) as frechet_dist
        FROM candidate_comps cc, query_traj qt
        """

        params = [query_sequence, query_sequence]

        if frechet_threshold:
            query += " WHERE ST_FrechetDistance(cc.geom, qt.geom) <= %s"
            params.append(frechet_threshold)

        query += """
        ORDER BY frechet_dist
        LIMIT %s;
        """
        params.append(k)

        rows = self._execute_query(query, tuple(params))

        # For each matching composition, reconstruct sequence
        results = []
        for row in rows:
            comp_hash = row[0]
            frechet_dist = row[1]

            # Retrieve composition's constituent atoms
            seq_query = """
            SELECT
                (ST_DumpPoints(geom)).path[1] as seq_idx,
                ST_MakePoint(
                    ST_X((ST_DumpPoints(geom)).geom),
                    ST_Y((ST_DumpPoints(geom)).geom),
                    ST_Z((ST_DumpPoints(geom)).geom),
                    ST_M((ST_DumpPoints(geom)).geom)
                )::geometry(POINTZM,4326) as pt
            FROM atom
            WHERE atom_hash = %s
            ORDER BY seq_idx;
            """
            seq_rows = self._execute_query(seq_query, (comp_hash,))

            # Find closest atom to each point (trajectory may not exactly match stored atoms)
            sequence = []
            for seq_row in seq_rows:
                pt = seq_row[1]
                # Find nearest atom to this trajectory point
                nearest_query = """
                SELECT atom_hash
                FROM atom
                ORDER BY geom <-> %s
                LIMIT 1;
                """
                nearest = self._execute_query(nearest_query, (pt,), fetch='one')
                sequence.append(nearest[0])

            results.append((sequence, frechet_dist))

        return results
```

**Why this is pattern recognition:**
- Compositions are trajectories (LINESTRING ZM in 4D space)
- Fréchet distance measures trajectory similarity
- No sequence model (RNN/Transformer) needed
- Pattern matching is geometric, not learned weights

---

## Phase 3: Autonomous Operation

### 3.1 Task Decomposition (Query Planning)

**Principle:** Complex operations decompose into sequences of spatial queries.

**Example: "Find concepts related to X but not to Y"**

```python
class HartonomousConnector:
    # ... (previous methods)

    def find_related_but_not(
        self,
        target_hash: bytes,
        exclude_hash: bytes,
        k: int = 10
    ) -> List[Atom]:
        """Find atoms similar to target but dissimilar to exclude.

        Autonomous query composition: No external planner needed.

        Args:
            target_hash: Atom to find neighbors of
            exclude_hash: Atom to exclude neighbors of
            k: Number of results

        Returns:
            Atoms close to target, far from exclude
        """
        query = """
        WITH target_geom AS (
            SELECT geom FROM atom WHERE atom_hash = %s
        ),
        exclude_geom AS (
            SELECT geom FROM atom WHERE atom_hash = %s
        )
        SELECT
            ua.atom_hash,
            ST_X(ua.geom) as x,
            ST_Y(ua.geom) as y,
            ST_Z(ua.geom) as z,
            ST_M(ua.geom) as m,
            ua.atom_class,
            ua.modality,
            ua.metadata,
            ua.geom <-> tg.geom as dist_to_target,
            ua.geom <-> eg.geom as dist_to_exclude
        FROM atom ua, target_geom tg, exclude_geom eg
        WHERE ua.atom_hash NOT IN (%s, %s)
        ORDER BY
            (ua.geom <-> tg.geom) - (ua.geom <-> eg.geom)  -- Minimize target distance, maximize exclude distance
        LIMIT %s;
        """

        rows = self._execute_query(
            query,
            (target_hash, exclude_hash, target_hash, exclude_hash, k)
        )

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]

    def hierarchical_search(
        self,
        atom_hash: bytes,
        traverse_direction: str = 'up',  # 'up' for abstraction, 'down' for detail
        max_levels: int = 3,
        k_per_level: int = 5
    ) -> Dict[int, List[Atom]]:
        """Traverse hierarchy (Z dimension) to find related concepts at different abstraction levels.

        Autonomous multi-level reasoning: No reasoning engine needed.

        Args:
            atom_hash: Starting atom
            traverse_direction: 'up' (increase Z) or 'down' (decrease Z)
            max_levels: How many Z levels to traverse
            k_per_level: Number of atoms per level

        Returns:
            Dict mapping Z level to list of atoms at that level
        """
        # Get starting atom's Z coordinate
        start_z_query = """
        SELECT ST_Z(geom) FROM atom WHERE atom_hash = %s;
        """
        start_z = self._execute_query(start_z_query, (atom_hash,), fetch='one')[0]

        results = {}

        for level in range(1, max_levels + 1):
            if traverse_direction == 'up':
                target_z = start_z + level
            else:
                target_z = start_z - level

            # Find nearest atoms at this Z level
            level_query = """
            WITH target AS (
                SELECT geom FROM atom WHERE atom_hash = %s
            )
            SELECT
                ua.atom_hash,
                ST_X(ua.geom) as x,
                ST_Y(ua.geom) as y,
                ST_Z(ua.geom) as z,
                ST_M(ua.geom) as m,
                ua.atom_class,
                ua.modality,
                ua.metadata
            FROM atom ua, target
            WHERE ABS(ST_Z(ua.geom) - %s) < 0.5  -- Within 0.5 of target Z level
            ORDER BY ST_Distance(
                ST_MakePoint(ST_X(ua.geom), ST_Y(ua.geom)),
                ST_MakePoint(ST_X(target.geom), ST_Y(target.geom))
            )  -- 2D distance (ignore Z for ordering)
            LIMIT %s;
            """

            rows = self._execute_query(level_query, (atom_hash, target_z, k_per_level))

            results[int(target_z)] = [
                Atom(
                    atom_hash=row[0],
                    x=row[1], y=row[2], z=row[3], m=row[4],
                    atom_class=row[5],
                    modality=row[6],
                    metadata=row[7]
                )
                for row in rows
            ]

        return results
```

**Autonomous reasoning:**
- No task planner needed - operations compose naturally
- Hierarchy traversal = abstraction/refinement reasoning
- Multi-step queries = multi-step reasoning
- Database query optimizer = execution planner

### 3.2 Continuous Learning (Cortex Integration)

**Principle:** Connector observes Cortex progress, no control needed.

```python
class HartonomousConnector:
    # ... (previous methods)

    def get_cortex_status(self) -> Dict[str, Any]:
        """Query Cortex background worker status.

        Cortex runs autonomously. This is read-only monitoring.

        Returns:
            Dict with keys:
                - is_running: bool
                - last_recalibration: timestamp
                - current_stress: float (0.0-1.0)
                - atoms_processed: int
                - landmarks_count: int
        """
        query = """
        SELECT
            pg_backend_pid() as pid,
            (SELECT COUNT(*) FROM pg_stat_activity WHERE query LIKE '%cortex%') > 0 as is_running,
            (SELECT value FROM cortex_metadata WHERE key = 'last_recalibration') as last_recal,
            (SELECT value FROM cortex_metadata WHERE key = 'current_stress') as stress,
            (SELECT value FROM cortex_metadata WHERE key = 'atoms_processed') as processed,
            (SELECT value FROM cortex_metadata WHERE key = 'landmarks_count') as landmarks;
        """

        row = self._execute_query(query, fetch='one')

        return {
            'is_running': row[1],
            'last_recalibration': row[2],
            'current_stress': float(row[3]) if row[3] else None,
            'atoms_processed': int(row[4]) if row[4] else 0,
            'landmarks_count': int(row[5]) if row[5] else 0
        }

    def trigger_recalibration(self, force: bool = False) -> bool:
        """Request Cortex recalibration (if needed).

        Cortex decides whether to actually recalibrate based on stress.
        This is a hint, not a command.

        Args:
            force: If True, bypass stress threshold check

        Returns:
            True if recalibration triggered, False if skipped
        """
        if force:
            query = "SELECT cortex_force_recalibration();"
        else:
            query = "SELECT cortex_request_recalibration();"

        result = self._execute_query(query, fetch='one')
        return result[0] if result else False
```

**Learning autonomy:**
- Cortex runs independently in background
- No training scripts needed
- No checkpoints/epochs
- Geometry improves continuously as data arrives
- Connector can observe, not control

---

## Phase 4: Production Deployment

### 4.1 High-Level API

**File:** `connector/api.py`

```python
from typing import List, Optional, Dict, Any
from .core import HartonomousConnector, Atom
from .pool import HartonomousPool

class Hartonomous:
    """High-level API for Hartonomous database-as-intelligence.

    This class provides user-friendly interface to spatial inference.
    Under the hood: everything is SQL queries. No AI frameworks.
    """

    def __init__(self, db_config: Optional[Dict[str, Any]] = None):
        """Initialize Hartonomous connection.

        Args:
            db_config: Optional dict with keys:
                - host: PostgreSQL host
                - port: PostgreSQL port
                - dbname: Database name
                - user: Username
                - password: Password
                - minconn: Minimum connections
                - maxconn: Maximum connections
        """
        config = db_config or {}
        self.pool = HartonomousPool(**config)
        self.connector = HartonomousConnector(self.pool)

    def query(self, concept: bytes, k: int = 10) -> List[Atom]:
        """Find concepts related to query concept.

        Args:
            concept: Atom hash (SDI) of query concept
            k: Number of related concepts to return

        Returns:
            List of semantically related atoms
        """
        return self.connector.find_similar(concept, k=k)

    def search(
        self,
        x: float, y: float,
        z: Optional[float] = None,
        m: Optional[float] = None,
        k: int = 10
    ) -> List[Atom]:
        """Search by arbitrary coordinates in semantic space.

        Args:
            x, y: Required semantic coordinates
            z: Optional hierarchy level (default: 0.0)
            m: Optional salience (default: 0.0)
            k: Number of results

        Returns:
            List of atoms near specified coordinates
        """
        return self.connector.find_similar_by_coords(
            x, y,
            z if z is not None else 0.0,
            m if m is not None else 0.0,
            k=k
        )

    def neighborhood(
        self,
        concept: bytes,
        radius: float
    ) -> List[Atom]:
        """Get all concepts in semantic neighborhood.

        Args:
            concept: Center atom hash
            radius: Semantic distance radius

        Returns:
            All atoms within radius
        """
        return self.connector.find_within_radius(concept, radius)

    def pattern(
        self,
        sequence: List[bytes],
        k: int = 10
    ) -> List[List[bytes]]:
        """Find sequences similar to query pattern.

        Args:
            sequence: List of atom hashes forming trajectory
            k: Number of similar patterns to return

        Returns:
            List of similar sequences (each a list of atom hashes)
        """
        results = self.connector.find_similar_trajectories(sequence, k=k)
        return [traj for traj, _ in results]

    def abstract(
        self,
        concept: bytes,
        levels: int = 1
    ) -> List[Atom]:
        """Move up hierarchy to more abstract concepts.

        Args:
            concept: Starting atom hash
            levels: How many abstraction levels to ascend

        Returns:
            Atoms at higher Z coordinates (more abstract)
        """
        results = self.connector.hierarchical_search(
            concept,
            traverse_direction='up',
            max_levels=levels,
            k_per_level=10
        )
        # Flatten results across levels
        return [atom for level_atoms in results.values() for atom in level_atoms]

    def refine(
        self,
        concept: bytes,
        levels: int = 1
    ) -> List[Atom]:
        """Move down hierarchy to more specific concepts.

        Args:
            concept: Starting atom hash
            levels: How many detail levels to descend

        Returns:
            Atoms at lower Z coordinates (more specific)
        """
        results = self.connector.hierarchical_search(
            concept,
            traverse_direction='down',
            max_levels=levels,
            k_per_level=10
        )
        return [atom for level_atoms in results.values() for atom in level_atoms]

    def insert(self, atom: Atom) -> bool:
        """Insert atom into database.

        Args:
            atom: Atom instance to insert

        Returns:
            True if inserted, False if duplicate
        """
        return self.connector.insert_atom(atom)

    def status(self) -> Dict[str, Any]:
        """Get system status.

        Returns:
            Dict with database stats and Cortex status
        """
        return self.connector.get_cortex_status()

    def close(self):
        """Close database connections."""
        self.pool.close()
```

### 4.2 Usage Example

**File:** `examples/basic_inference.py`

```python
from connector.api import Hartonomous, Atom
import hashlib

# Initialize connection (uses environment variables by default)
hart = Hartonomous()

# Example 1: Query by existing concept
concept_hash = bytes.fromhex("abcd1234...")  # SDI of some stored atom
related = hart.query(concept_hash, k=5)

print(f"Concepts related to {concept_hash.hex()[:8]}:")
for atom in related:
    print(f"  - {atom.atom_hash.hex()[:8]} at ({atom.x:.2f}, {atom.y:.2f}, {atom.z:.2f})")

# Example 2: Search by coordinates (e.g., interpolated location)
nearby = hart.search(x=0.5, y=0.3, z=2.0, k=10)
print(f"\nConcepts near (0.5, 0.3, 2.0):")
for atom in nearby:
    print(f"  - {atom.atom_hash.hex()[:8]}")

# Example 3: Semantic neighborhood
neighborhood = hart.neighborhood(concept_hash, radius=0.1)
print(f"\nSemantic neighborhood (r=0.1): {len(neighborhood)} atoms")

# Example 4: Pattern matching
sequence = [
    bytes.fromhex("aaaa1111..."),
    bytes.fromhex("bbbb2222..."),
    bytes.fromhex("cccc3333...")
]
similar_patterns = hart.pattern(sequence, k=3)
print(f"\nPatterns similar to query: {len(similar_patterns)}")

# Example 5: Hierarchical reasoning
abstractions = hart.abstract(concept_hash, levels=2)
print(f"\nMore abstract concepts: {len(abstractions)}")

specifics = hart.refine(concept_hash, levels=1)
print(f"More specific concepts: {len(specifics)}")

# Example 6: System health
status = hart.status()
print(f"\nSystem status:")
print(f"  Cortex running: {status['is_running']}")
print(f"  Current stress: {status['current_stress']:.3f}")
print(f"  Atoms processed: {status['atoms_processed']}")

# Cleanup
hart.close()
```

**Output interpretation:**
- `query()` → Inference (semantic similarity)
- `search()` → Spatial query (arbitrary point)
- `neighborhood()` → Clustering/context
- `pattern()` → Sequence recognition
- `abstract()/refine()` → Multi-level reasoning

**No LLMs. No embeddings. Pure geometry.**

---

## Phase 5: Advanced Operations

### 5.1 Composition Building

**Create new compositions from existing atoms:**

```python
class HartonomousConnector:
    # ... (previous methods)

    def create_composition(
        self,
        constituent_hashes: List[bytes],
        modality: int,
        metadata: Optional[Dict] = None
    ) -> bytes:
        """Create Composition (trajectory) from sequence of atoms.

        Args:
            constituent_hashes: Ordered list of atom hashes
            modality: Modality identifier for composition
            metadata: Optional metadata dict

        Returns:
            Hash of newly created composition
        """
        # Build LINESTRING from constituent atoms
        query = """
        WITH constituent_points AS (
            SELECT
                geom,
                array_position(%s::bytea[], atom_hash) as seq_order
            FROM atom
            WHERE atom_hash = ANY(%s)
        ),
        trajectory AS (
            SELECT ST_MakeLine(geom ORDER BY seq_order) as geom
            FROM constituent_points
        ),
        comp_hash AS (
            SELECT gen_random_bytes(32) as hash  -- Temporary; use SDI in production
        )
        INSERT INTO atom (
            atom_hash,
            geom,
            atom_class,
            modality,
            metadata
        )
        SELECT
            ch.hash,
            t.geom,
            1,  -- Composition
            %s,
            %s
        FROM trajectory t, comp_hash ch
        RETURNING atom_hash;
        """

        result = self._execute_query(
            query,
            (constituent_hashes, constituent_hashes, modality, metadata),
            fetch='one'
        )

        return result[0]
```

**Why this matters:**
- Compositions = learned patterns (e.g., phrases, visual patterns)
- Trajectories in space = sequential relationships
- Inference on compositions = pattern matching

### 5.2 Analogy Queries

**Geometric analogy: "A is to B as C is to ?"**

```python
class HartonomousConnector:
    # ... (previous methods)

    def analogy(
        self,
        a: bytes, b: bytes, c: bytes,
        k: int = 5
    ) -> List[Atom]:
        """Solve analogy: A:B :: C:?

        Geometric interpretation: Vector from A to B applied to C.

        Args:
            a, b, c: Atom hashes for analogy
            k: Number of candidate answers

        Returns:
            Atoms that complete the analogy
        """
        query = """
        WITH
        geom_a AS (SELECT geom FROM atom WHERE atom_hash = %s),
        geom_b AS (SELECT geom FROM atom WHERE atom_hash = %s),
        geom_c AS (SELECT geom FROM atom WHERE atom_hash = %s),
        delta AS (
            SELECT
                ST_X(b.geom) - ST_X(a.geom) as dx,
                ST_Y(b.geom) - ST_Y(a.geom) as dy,
                ST_Z(b.geom) - ST_Z(a.geom) as dz,
                ST_M(b.geom) - ST_M(a.geom) as dm
            FROM geom_a a, geom_b b
        ),
        target_point AS (
            SELECT ST_MakePoint(
                ST_X(c.geom) + d.dx,
                ST_Y(c.geom) + d.dy,
                ST_Z(c.geom) + d.dz,
                ST_M(c.geom) + d.dm
            )::geometry(POINTZM,4326) as geom
            FROM geom_c c, delta d
        )
        SELECT
            ua.atom_hash,
            ST_X(ua.geom) as x,
            ST_Y(ua.geom) as y,
            ST_Z(ua.geom) as z,
            ST_M(ua.geom) as m,
            ua.atom_class,
            ua.modality,
            ua.metadata
        FROM atom ua, target_point tp
        ORDER BY ua.geom <-> tp.geom
        LIMIT %s;
        """

        rows = self._execute_query(query, (a, b, c, k))

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
```

**Example:**
- "king" - "man" + "woman" ≈ "queen"
- Geometric vector arithmetic in semantic space
- No word2vec model needed - geometry IS the model

---

## Performance Optimization

### Query Caching

```python
from functools import lru_cache

class HartonomousConnector:
    # ... (previous methods)

    @lru_cache(maxsize=1000)
    def find_similar_cached(
        self,
        atom_hash: bytes,
        k: int = 10
    ) -> tuple:  # Returns tuple for hashability
        """Cached k-NN query for frequently accessed atoms."""
        results = self.find_similar(atom_hash, k)
        # Convert to tuple for caching
        return tuple((r.atom_hash, r.x, r.y, r.z, r.m) for r in results)
```

**Rationale:**
- Hot atoms (frequently queried) benefit from application-level cache
- PostgreSQL already has query cache, but Python-level adds another layer
- LRU eviction prevents memory bloat

### Prepared Statements

```python
class HartonomousConnector:
    def __init__(self, pool: HartonomousPool):
        self.pool = pool
        self._prepare_statements()

    def _prepare_statements(self):
        """Prepare frequently-used queries for performance."""
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                # Prepare k-NN query
                cur.execute("""
                PREPARE find_similar_stmt (bytea, int) AS
                WITH target AS (
                    SELECT geom FROM atom WHERE atom_hash = $1
                )
                SELECT
                    ua.atom_hash, ST_X(ua.geom), ST_Y(ua.geom),
                    ST_Z(ua.geom), ST_M(ua.geom), ua.atom_class,
                    ua.modality, ua.metadata
                FROM atom ua, target
                WHERE ua.atom_hash != $1
                ORDER BY ua.geom <-> target.geom
                LIMIT $2;
                """)

                # Prepare radius search
                cur.execute("""
                PREPARE radius_search_stmt (bytea, float8) AS
                WITH target AS (
                    SELECT geom FROM atom WHERE atom_hash = $1
                )
                SELECT
                    ua.atom_hash, ST_X(ua.geom), ST_Y(ua.geom),
                    ST_Z(ua.geom), ST_M(ua.geom), ua.atom_class,
                    ua.modality, ua.metadata
                FROM atom ua, target
                WHERE ua.atom_hash != $1
                  AND ST_3DDistance(ua.geom, target.geom) <= $2;
                """)

    def find_similar(self, atom_hash: bytes, k: int = 10) -> List[Atom]:
        """Use prepared statement for k-NN query."""
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute("EXECUTE find_similar_stmt (%s, %s)", (atom_hash, k))
                rows = cur.fetchall()

        return [
            Atom(
                atom_hash=row[0],
                x=row[1], y=row[2], z=row[3], m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
```

---

## Testing

### Unit Tests

**File:** `tests/test_connector.py`

```python
import unittest
from connector.api import Hartonomous, Atom
from connector.pool import HartonomousPool

class TestHartonomousConnector(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        """Set up test database connection."""
        cls.hart = Hartonomous({
            'dbname': 'hartonomous_test',
            'host': 'localhost',
            'user': 'test_user',
            'password': 'test_pass'
        })

    @classmethod
    def tearDownClass(cls):
        """Clean up connections."""
        cls.hart.close()

    def test_insert_and_query(self):
        """Test basic insert and k-NN query."""
        # Create test atom
        test_atom = Atom(
            atom_hash=b'\x00' * 32,
            x=0.5, y=0.5, z=0.0, m=1.0,
            atom_class=0,
            modality=1,
            metadata={'test': True}
        )

        # Insert
        inserted = self.hart.insert(test_atom)
        self.assertTrue(inserted)

        # Query neighbors
        neighbors = self.hart.query(test_atom.atom_hash, k=5)
        self.assertLessEqual(len(neighbors), 5)

    def test_spatial_search(self):
        """Test coordinate-based search."""
        results = self.hart.search(x=0.0, y=0.0, z=0.0, m=0.0, k=10)
        self.assertLessEqual(len(results), 10)

        # Verify results are sorted by distance
        if len(results) > 1:
            for i in range(len(results) - 1):
                dist1 = (results[i].x**2 + results[i].y**2)**0.5
                dist2 = (results[i+1].x**2 + results[i+1].y**2)**0.5
                self.assertLessEqual(dist1, dist2)

    def test_hierarchy_traversal(self):
        """Test abstraction/refinement."""
        # Create atom at Z=1.0
        mid_atom = Atom(
            atom_hash=b'\x01' * 32,
            x=0.0, y=0.0, z=1.0, m=0.5,
            atom_class=0,
            modality=1
        )
        self.hart.insert(mid_atom)

        # Search for more abstract (higher Z)
        abstract = self.hart.abstract(mid_atom.atom_hash, levels=1)
        if abstract:
            self.assertGreater(abstract[0].z, mid_atom.z)

        # Search for more specific (lower Z)
        specific = self.hart.refine(mid_atom.atom_hash, levels=1)
        if specific:
            self.assertLess(specific[0].z, mid_atom.z)

    def test_system_status(self):
        """Test status reporting."""
        status = self.hart.status()
        self.assertIn('is_running', status)
        self.assertIn('current_stress', status)

if __name__ == '__main__':
    unittest.main()
```

---

## Deployment Checklist

### Prerequisites

- [ ] PostgreSQL 16+ installed and running
- [ ] PostGIS 3.4+ extension enabled
- [ ] Database schema created (see HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md Phase 1)
- [ ] Cortex extension compiled and installed (see CORTEX_IMPLEMENTATION_SPECIFICATION.md)
- [ ] Python 3.11+ with psycopg2-binary installed

### Installation Steps

1. **Install Python dependencies:**
   ```bash
   pip install psycopg2-binary
   ```

2. **Set environment variables:**
   ```bash
   export PGHOST=localhost
   export PGPORT=5432
   export PGDATABASE=hartonomous
   export PGUSER=hartonomous_user
   export PGPASSWORD=your_password
   ```

3. **Test connection:**
   ```python
   from connector.api import Hartonomous

   hart = Hartonomous()
   status = hart.status()
   print(f"Connected: {status['is_running']}")
   hart.close()
   ```

4. **Run tests:**
   ```bash
   python -m unittest tests.test_connector
   ```

### Production Configuration

**PostgreSQL tuning for spatial workload:**

```sql
-- Increase shared buffers (25% of RAM)
ALTER SYSTEM SET shared_buffers = '8GB';

-- Increase work_mem for sorting/joins
ALTER SYSTEM SET work_mem = '256MB';

-- Increase maintenance_work_mem for index building
ALTER SYSTEM SET maintenance_work_mem = '1GB';

-- Enable parallel query execution
ALTER SYSTEM SET max_parallel_workers_per_gather = 4;
ALTER SYSTEM SET max_parallel_workers = 8;

-- Optimize for spatial queries
ALTER SYSTEM SET random_page_cost = 1.1;  -- For SSD
ALTER SYSTEM SET effective_cache_size = '24GB';  -- 75% of RAM

-- Reload configuration
SELECT pg_reload_conf();
```

**Connection pooling (external):**

Consider using PgBouncer for production:

```ini
# pgbouncer.ini
[databases]
hartonomous = host=localhost port=5432 dbname=hartonomous

[pgbouncer]
listen_addr = 127.0.0.1
listen_port = 6432
auth_type = md5
auth_file = /etc/pgbouncer/userlist.txt
pool_mode = session
max_client_conn = 1000
default_pool_size = 50
```

---

## Troubleshooting

### Slow Queries

**Symptom:** `find_similar()` taking >100ms

**Diagnosis:**

```python
# Enable query logging in PostgreSQL
conn.cursor().execute("SET log_min_duration_statement = 10;")  # Log queries >10ms

# Check if GiST index is being used
conn.cursor().execute("""
EXPLAIN ANALYZE
SELECT * FROM atom
ORDER BY geom <-> ST_MakePoint(0,0,0,0)::geometry(POINTZM,4326)
LIMIT 10;
""")
```

**Solution:**
- Verify index exists: `\d+ atom` in psql
- Run `VACUUM ANALYZE atom;`
- Increase `shared_buffers` if cache hit ratio is low

### Connection Pool Exhaustion

**Symptom:** `PoolError: connection pool exhausted`

**Solution:**

```python
# Increase pool size
hart = Hartonomous({
    'maxconn': 50  # Default is 10
})

# Or use connection pooler like PgBouncer
```

### High Memory Usage

**Symptom:** Python process using >10GB RAM

**Cause:** Probably caching too many results

**Solution:**

```python
# Reduce cache size
@lru_cache(maxsize=100)  # Default was 1000
def find_similar_cached(...):
    ...

# Or disable caching entirely
```

---

## Summary

This specification defines the integration layer for Hartonomous as a **database connector**, not an AI orchestrator. Key principles:

1. **Intelligence is in the database:** PostGIS spatial index, LMDS geometry, Cortex refinement
2. **Inference is spatial query:** k-NN, radius search, trajectory matching
3. **Learning is automatic:** Cortex background worker, no training scripts
4. **No external AI:** No LLMs, no LangGraph, no vector databases
5. **Python is a client:** Translates operations to SQL, manages connections

**The revolutionary paradigm:** Storage IS intelligence. Queries ARE inference. Geometry IS understanding.

---

## References

- **Master Plan:** HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md
- **Cortex Spec:** CORTEX_IMPLEMENTATION_SPECIFICATION.md
- **Shader Spec:** SHADER_IMPLEMENTATION_SPECIFICATION.md
- **PostgreSQL Docs:** https://www.postgresql.org/docs/current/
- **PostGIS Docs:** https://postgis.net/docs/
- **psycopg2 Docs:** https://www.psycopg.org/docs/

---

**Version:** 1.0
**Date:** 2025-12-13
**Status:** Complete - Ready for Implementation

---

**Hartonomous: The database IS the AI.**
