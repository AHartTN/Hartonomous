"""Test Hilbert encoding against database."""
import asyncio
import asyncpg
import sys
import os

# Add api to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'api'))

from config import settings


async def test_roundtrip():
    """Test Hilbert encoding roundtrip with database."""
    conn_str = settings.get_connection_string()
    conn = await asyncpg.connect(conn_str)
    
    try:
        # Test case: position (102, 435, 5) in 768x768x32 tensor
        rows, cols, layers = 768, 768, 32
        i, j, layer = 102, 435, 5
        
        # Normalization
        x_norm = i / max(rows - 1, 1)
        y_norm = j / max(cols - 1, 1)  
        z_norm = layer / max(layers - 1, 1)
        
        print(f'Position: ({i}, {j}, {layer})')
        print(f'Normalization: x={x_norm:.10f}, y={y_norm:.10f}, z={z_norm:.10f}')
        
        # Encode with database
        result = await conn.fetchrow(
            'SELECT hilbert_encode_3d($1::float8, $2::float8, $3::float8, 21)',
            x_norm, y_norm, z_norm
        )
        hilbert = result[0]
        print(f'Hilbert index: {hilbert}')
        
        # Decode with database
        result = await conn.fetchrow(
            'SELECT * FROM hilbert_decode_3d($1::bigint, 21)',
            hilbert
        )
        x_dec, y_dec, z_dec = result
        print(f'Decoded normalized: x={x_dec:.10f}, y={y_dec:.10f}, z={z_dec:.10f}')
        
        # Convert back to positions - try different methods
        print('\nDecoding attempts:')
        i_int = int(x_dec * (rows - 1))
        j_int = int(y_dec * (cols - 1))
        layer_int = int(z_dec * (layers - 1))
        print(f'  Method 1 (int): ({i_int}, {j_int}, {layer_int}) - Match: {(i_int, j_int, layer_int) == (i, j, layer)}')
        
        i_round = round(x_dec * (rows - 1))
        j_round = round(y_dec * (cols - 1))
        layer_round = round(z_dec * (layers - 1))
        print(f'  Method 2 (round): ({i_round}, {j_round}, {layer_round}) - Match: {(i_round, j_round, layer_round) == (i, j, layer)}')
        
        i_add = int(x_dec * (rows - 1) + 0.5)
        j_add = int(y_dec * (cols - 1) + 0.5)
        layer_add = int(z_dec * (layers - 1) + 0.5)
        print(f'  Method 3 (add 0.5): ({i_add}, {j_add}, {layer_add}) - Match: {(i_add, j_add, layer_add) == (i, j, layer)}')
        
        print('\n' + '='*60)
        print('Testing boundary cases...')
        print('='*60)
        
        # Test edges
        test_cases = [
            (0, 0, 0, 'Origin'),
            (rows-1, cols-1, layers-1, 'Max'),
            (0, cols-1, 0, 'Corner 1'),
            (rows-1, 0, layers-1, 'Corner 2'),
        ]
        
        for test_i, test_j, test_layer, label in test_cases:
            x_n = test_i / max(rows - 1, 1)
            y_n = test_j / max(cols - 1, 1)
            z_n = test_layer / max(layers - 1, 1)
            
            result = await conn.fetchrow(
                'SELECT hilbert_encode_3d($1::float8, $2::float8, $3::float8, 21)',
                x_n, y_n, z_n
            )
            h = result[0]
            
            result = await conn.fetchrow(
                'SELECT * FROM hilbert_decode_3d($1::bigint, 21)',
                h
            )
            x_d, y_d, z_d = result
            
            i_r = round(x_d * (rows - 1))
            j_r = round(y_d * (cols - 1))
            layer_r = round(z_d * (layers - 1))
            
            match = (i_r, j_r, layer_r) == (test_i, test_j, test_layer)
            print(f'{label}: ({test_i}, {test_j}, {test_layer}) -> Hilbert={h} -> ({i_r}, {j_r}, {layer_r}) - {"✓" if match else "✗"}')
        
    finally:
        await conn.close()


if __name__ == '__main__':
    asyncio.run(test_roundtrip())
