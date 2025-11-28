import psycopg
import json
from api.config import settings

conn = psycopg.connect(
    host=settings.pghost,
    port=settings.pgport,
    dbname=settings.pgdatabase,
    user=settings.pguser,
    password=settings.pgpassword
)
cur = conn.cursor()

cur.execute("""
    SELECT COUNT(*) FROM atom;
""")
count = cur.fetchone()[0]
print(f'Total atoms in database: {count:,}')

cur.execute("""
    SELECT last_value FROM atom_atom_id_seq;
""")
seq_val = cur.fetchone()[0]
print(f'Atom ID sequence current value: {seq_val:,}')

cur.execute("""
    SELECT COUNT(*) FROM atom_composition;
""")
comp_count = cur.fetchone()[0]
print(f'Total compositions: {comp_count:,}')

cur.execute("""
    SELECT atom_id, encode(content_hash, 'hex') as hash, metadata->>'modality' as modality
    FROM atom 
    ORDER BY atom_id DESC
    LIMIT 20;
""")

rows = cur.fetchall()
print(f'\nMost recent 20 atoms:')
for row in rows:
    print(f'  ID: {row[0]:,}, Hash: {row[1][:16]}..., Modality: {row[2]}')

cur.close()
conn.close()
