"""Test character-level decomposition with CPE hierarchy

Demonstrates proper granularity:
Z=0: Individual characters (constants)
Z=1: Words (compositions of characters)
Z=2: Sentences (compositions of words)
Z=3: Paragraphs (compositions of sentences)
"""

import sys
sys.path.append('connector')

import psycopg2
from semantic_projector import SemanticProjector
from blake3 import blake3

# Connect
conn = psycopg2.connect(host="127.0.0.1", database="hartonomous", user="postgres")
conn.autocommit = True
cursor = conn.cursor()

print("Clearing database...")
cursor.execute("TRUNCATE atom, atom_compositions CASCADE")

projector = SemanticProjector(scale=100.0)

# Test word: "Hello"
word = "Hello"
print(f'\nDecomposing word: "{word}"')

# Z=0: Character constants
char_atoms = []
for char in word:
    # Each character = constant atom
    atom_id = blake3(b'\x01\x02\x00' + char.encode()).digest()
    x, y, z, m = projector.project_token(char)
    x, y, z, m = float(x), float(y), float(z), float(m)
    
    cursor.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index)
        VALUES (%s, 0, 1, 'char', %s, ST_MakePoint(%s, %s, 0.0, %s), 0)
        ON CONFLICT DO NOTHING
    """, (atom_id, char.encode(), x, y, m))
    
    char_atoms.append((atom_id, x, y, 0.0, m))

# Count unique vs total
cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
unique_chars = cursor.fetchone()[0]
print(f"  Total characters: {len(word)}")
print(f"  Unique characters (atoms stored): {unique_chars}")
print(f"  Character 'l' appears: {word.count('l')}× but stored: 1×")

# Z=1: Word composition (LINESTRING through character sequence)
word_comp_id = blake3(b'\x01\x01\x01' + word.encode()).digest()

# Build LINESTRING through characters in sequence order
linestring_coords = []
for atom_id, x, y, z, m in char_atoms:
    linestring_coords.append(f"{x} {y} 1.0 {m}")

linestring_wkt = f"LINESTRING ZM ({', '.join(linestring_coords)})"

cursor.execute("""
    INSERT INTO atom (atom_id, atom_class, modality, geom, hilbert_index)
    VALUES (%s, 1, 99, ST_GeomFromText(%s, 0), 0)
""", (word_comp_id, linestring_wkt))

# Link to constituent characters
for idx, (atom_id, _, _, _, _) in enumerate(char_atoms):
    cursor.execute("""
        INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
        VALUES (%s, %s, %s)
        ON CONFLICT DO NOTHING
    """, (word_comp_id, atom_id, idx))

print(f'\n  Created word composition "{word}" at Z=1')
print(f"    Composition ID: {word_comp_id.hex()[:16]}...")
print(f"    References {len(char_atoms)} character positions")
print(f"    Geometry: LINESTRING with {len(char_atoms)} vertices")

# Verify composition structure
cursor.execute("""
    SELECT 
        a.atom_id,
        a.atom_class,
        ST_ZMin(a.geom) as z_level,
        convert_from(a.atomic_value, 'UTF8') as value,
        ST_NumPoints(a.geom) as num_points
    FROM atom a
    LEFT JOIN atom_compositions ac ON ac.parent_atom_id = a.atom_id
    ORDER BY a.atom_class, a.atom_id
""")

print("\n  Atom hierarchy:")
for row in cursor.fetchall():
    atom_id, atom_class, z, value, num_points = row
    if atom_class == 0:
        print(f"    Z={z:.0f} Constant: '{value}' (char)")
    else:
        print(f"    Z={z:.0f} Composition: {atom_id.hex()[:16]}... ({num_points} vertices)")

# Query: Find compositions containing character 'l'
cursor.execute("""
    SELECT DISTINCT
        a.atom_id,
        ST_AsText(a.geom)
    FROM atom a
    JOIN atom_compositions ac ON ac.parent_atom_id = a.atom_id
    JOIN atom c ON c.atom_id = ac.component_atom_id
    WHERE c.atomic_value = %s
""", (b'l',))

print("\n  Compositions containing 'l':")
for row in cursor.fetchall():
    comp_id, geom = row
    print(f"    {comp_id.hex()[:16]}... → {geom[:60]}...")

# Storage efficiency
cursor.execute("""
    SELECT 
        atom_class,
        COUNT(*) as atoms,
        SUM(pg_column_size(atom_id) + pg_column_size(geom)) as bytes
    FROM atom
    GROUP BY atom_class
""")

print("\n  Storage efficiency:")
for row in cursor.fetchall():
    atom_class, atoms, bytes_used = row
    class_name = "Constants" if atom_class == 0 else "Compositions"
    print(f"    {class_name}: {atoms} atoms, {bytes_used} bytes")

conn.close()
print("\n✓ Character decomposition complete")
print("\nKey insight: 'Hello' has 5 characters but only 4 unique atoms.")
print("The letter 'l' appears twice in the sequence but is stored once,")
print("referenced multiple times via atom_compositions table.")
