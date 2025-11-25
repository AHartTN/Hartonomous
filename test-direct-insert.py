"""
Direct database test - create atoms manually to test Neo4j sync
"""
import asyncio
from psycopg_pool import AsyncConnectionPool
import hashlib

async def test_atom_creation():
    # Connection string
    conn_str = "postgresql://hartonomous:Revolutionary-AI-2025!Geometry@localhost:5432/hartonomous?sslmode=prefer"
    
    # Create connection pool
    pool = AsyncConnectionPool(conninfo=conn_str, min_size=1, max_size=5, open=False)
    await pool.open()
    
    print("? Connected to PostgreSQL")
    
    try:
        async with pool.connection() as conn:
            async with conn.cursor() as cur:
                # Create a test atom
                text = "Test Atom"
                content_hash = hashlib.sha256(text.encode()).digest()
                
                await cur.execute("""
                    INSERT INTO atom (content_hash, canonical_text, metadata)
                    VALUES (%s, %s, %s)
                    RETURNING atom_id;
                """, (content_hash, text, '{}'))
                
                atom_id = (await cur.fetchone())[0]
                print(f"? Created atom {atom_id}: '{text}'")
                
                # Check if it was inserted
                await cur.execute("SELECT COUNT(*) FROM atom;")
                count = (await cur.fetchone())[0]
                print(f"? Total atoms in database: {count}")
                
                # Manually trigger NOTIFY (since trigger might not fire)
                import json
                payload = json.dumps({
                    "atom_id": atom_id,
                    "content_hash": content_hash.hex()
                })
                await cur.execute("NOTIFY atom_created, %s;", (payload,))
                print(f"? Sent NOTIFY atom_created")
                
    finally:
        await pool.close()
        print("? Connection closed")

if __name__ == "__main__":
    asyncio.run(test_atom_creation())
