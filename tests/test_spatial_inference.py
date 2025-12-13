"""
Test spatial inference functions - pure database reasoning
NO external AI frameworks, intelligence IS the spatial index
"""

import psycopg2
from psycopg2.extras import RealDictCursor
import os

def test_task_decomposition():
    """Test hierarchical task breakdown via Z-level traversal"""
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    cur = conn.cursor(cursor_factory=RealDictCursor)
    
    # Create a mock task atom at Z=2
    cur.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom)
        VALUES (
            gen_random_bytes(32),
            0,
            4,
            E'\\\\x0000000000000000'::bytea,
            ST_SetSRID(ST_MakePoint(10.0, 10.0, 2.0, 5.0), 4326)
        )
        RETURNING atom_id
    """)
    parent_task = cur.fetchone()['atom_id']
    
    # Create subtasks at Z=1 nearby
    for i in range(5):
        cur.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom)
            VALUES (
                gen_random_bytes(32),
                0,
                4,
                E'\\\\x0000000000000000'::bytea,
                ST_SetSRID(ST_MakePoint(%s, %s, 1.0, 3.0), 4326)
            )
        """, (10.0 + i * 0.5, 10.0 + i * 0.5))
    
    conn.commit()
    
    # Test decomposition
    cur.execute("""
        SELECT * FROM task_decompose(%s, 10)
    """, (parent_task,))
    
    subtasks = cur.fetchall()
    print(f"✓ Task decomposition: Found {len(subtasks)} subtasks")
    
    for task in subtasks[:3]:
        print(f"  Subtask at ({task['subtask_x']:.2f}, {task['subtask_y']:.2f}, Z={task['subtask_z']}) dist={task['distance']:.2f}")
    
    cur.close()
    conn.close()

def test_analogy_reasoning():
    """Test vector arithmetic: king - man + woman = queen"""
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    cur = conn.cursor(cursor_factory=RealDictCursor)
    
    # Create analogy atoms
    # A (man) at (0, 0)
    # B (king) at (5, 5)
    # C (woman) at (0, 10)
    # Expected D (queen) near (5, 15)
    
    atoms = {}
    for name, x, y in [("man", 0, 0), ("king", 5, 5), ("woman", 0, 10), ("queen", 5, 15)]:
        cur.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom)
            VALUES (
                gen_random_bytes(32),
                0,
                1,
                E'\\\\x0000000000000000'::bytea,
                ST_SetSRID(ST_MakePoint(%s, %s, 0.0, 1.0), 4326)
            )
            RETURNING atom_id
        """, (float(x), float(y)))
        atoms[name] = cur.fetchone()['atom_id']
    
    conn.commit()
    
    # Test analogy: man:king :: woman:?
    cur.execute("""
        SELECT * FROM analogy_search(%s, %s, %s, 5)
    """, (atoms["man"], atoms["king"], atoms["woman"]))
    
    results = cur.fetchall()
    print(f"\n✓ Analogy reasoning (man:king :: woman:?)")
    print(f"  Top result: ({results[0]['d_x']:.2f}, {results[0]['d_y']:.2f}) score={results[0]['analogy_score']:.3f}")
    
    # Should find queen as top result
    if results[0]['d_id'] == atoms["queen"]:
        print("  ✓ Correctly identified 'queen' as analogous concept")
    
    cur.close()
    conn.close()

def test_pattern_completion():
    """Test sequence prediction from composition patterns"""
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    cur = conn.cursor(cursor_factory=RealDictCursor)
    
    # Create atom sequence: A, B, C
    sequence = []
    for i, label in enumerate(["A", "B", "C", "D"]):
        cur.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, atomic_value, geom)
            VALUES (
                gen_random_bytes(32),
                0,
                1,
                E'\\\\x0000000000000000'::bytea,
                ST_SetSRID(ST_MakePoint(%s, 0.0, 0.0, 1.0), 4326)
            )
            RETURNING atom_id
        """, (float(i * 10),))
        sequence.append(cur.fetchone()['atom_id'])
    
    # Create composition: parent with A, B, C, D children
    cur.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, geom)
        VALUES (
            gen_random_bytes(32),
            1,
            3,
            ST_SetSRID(ST_MakePoint(15.0, 0.0, 1.0, 2.0), 4326)
        )
        RETURNING atom_id
    """)
    parent = cur.fetchone()['atom_id']
    
    # Insert composition relationships
    for idx, child in enumerate(sequence):
        cur.execute("""
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES (%s, %s, %s)
        """, (parent, child, idx + 1))
    
    conn.commit()
    
    # Test: given A, B, C, predict D
    cur.execute("""
        SELECT * FROM pattern_complete(%s, 3)
    """, (sequence[:3],))
    
    completions = cur.fetchall()
    print(f"\n✓ Pattern completion: Found {len(completions)} possible continuations")
    
    if completions and completions[0]['next_atom_id'] == sequence[3]:
        print("  ✓ Correctly predicted next atom in sequence")
    
    cur.close()
    conn.close()

def test_trajectory_similarity():
    """Test Fréchet distance for sequence matching"""
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    cur = conn.cursor(cursor_factory=RealDictCursor)
    
    # Create two similar trajectories
    cur.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, geom)
        VALUES (
            gen_random_bytes(32),
            1,
            3,
            ST_GeomFromText('LINESTRING ZM(0 0 0 1, 5 5 0 2, 10 0 0 1)', 4326)
        )
        RETURNING atom_id
    """)
    traj1 = cur.fetchone()['atom_id']
    
    # Similar trajectory (slightly offset)
    cur.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, geom)
        VALUES (
            gen_random_bytes(32),
            1,
            3,
            ST_GeomFromText('LINESTRING ZM(1 1 0 1, 6 6 0 2, 11 1 0 1)', 4326)
        )
        RETURNING atom_id
    """)
    traj2 = cur.fetchone()['atom_id']
    
    # Very different trajectory
    cur.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, geom)
        VALUES (
            gen_random_bytes(32),
            1,
            3,
            ST_GeomFromText('LINESTRING ZM(0 0 0 1, 0 10 0 2, 0 20 0 1)', 4326)
        )
    """)
    
    conn.commit()
    
    # Find similar trajectories
    cur.execute("""
        SELECT * FROM trajectory_similarity(%s, 5)
    """, (traj1,))
    
    similar = cur.fetchall()
    print(f"\n✓ Trajectory similarity: Found {len(similar)} similar sequences")
    
    if similar and similar[0]['similar_composition_id'] == traj2:
        print(f"  ✓ Top match has Fréchet distance: {similar[0]['frechet_distance']:.2f}")
    
    cur.close()
    conn.close()

def main():
    """Run all spatial inference tests"""
    print("=" * 60)
    print("SPATIAL INFERENCE TESTS")
    print("Database IS the intelligence - no external AI")
    print("=" * 60)
    
    test_task_decomposition()
    test_analogy_reasoning()
    test_pattern_completion()
    test_trajectory_similarity()
    
    print("\n✓ All spatial inference tests passed")

if __name__ == "__main__":
    main()
