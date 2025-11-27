#!/usr/bin/env python3
import asyncio, asyncpg, os

async def test():
    user = os.getenv('USER', 'ahart')
    conn = await asyncpg.connect(host='/var/run/postgresql', database='hartonomous', user=user)
    
    version = await conn.fetchval('SELECT version()')
    print(f"✅ Connected as {user}: {version[:60]}...")
    
    atom_count = await conn.fetchval('SELECT COUNT(*) FROM atom')
    print(f"✅ Atoms in database: {atom_count}")
    
    await conn.close()
    return True

asyncio.run(test())
