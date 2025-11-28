import asyncio
from psycopg_pool import AsyncConnectionPool
from api.config import settings

# Windows async fix
asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

async def check():
    pool = AsyncConnectionPool(
        conninfo=f"host={settings.pghost} port={settings.pgport} "
                 f"dbname={settings.pgdatabase} user={settings.pguser} "
                 f"password={settings.pgpassword}",
        min_size=1,
        max_size=1,
    )
    async with pool.connection() as conn:
        async with conn.cursor() as cur:
            await cur.execute("""
                SELECT atom_id, encode(content_hash, 'hex') as hash, name, 
                       metadata 
                FROM atom 
                WHERE atom_id = 2;
            """)
            row = await cur.fetchone()
            if row:
                print(f'Atom ID 2:')
                print(f'  ID: {row[0]}')
                print(f'  Hash: {row[1]}')
                print(f'  Name: {row[2]}')
                print(f'  Metadata: {row[3]}')
            else:
                print('Atom ID 2 not found')

asyncio.run(check())
