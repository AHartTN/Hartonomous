-- GPU-Accelerated Attention Computation
CREATE OR REPLACE FUNCTION compute_attention_gpu(
    p_query_atom_id BIGINT,
    p_context_atom_ids BIGINT[],
    p_k INTEGER DEFAULT 10
)
RETURNS TABLE(
    atom_id BIGINT,
    attention_weight REAL,
    canonical_text TEXT
)
LANGUAGE plpython3u
AS $$
    import cupy as cp
    import numpy as np
    
    query_plan = plpy.prepare("""
        SELECT ST_X(spatial_key) as x, ST_Y(spatial_key) as y, ST_Z(spatial_key) as z
        FROM atom WHERE atom_id = $1
    """, ["bigint"])
    query_row = plpy.execute(query_plan, [p_query_atom_id])[0]
    query_pos = cp.array([query_row['x'], query_row['y'], query_row['z']], dtype=cp.float32)
    
    context_plan = plpy.prepare("""
        SELECT atom_id, canonical_text,
               ST_X(spatial_key) as x, ST_Y(spatial_key) as y, ST_Z(spatial_key) as z
        FROM atom WHERE atom_id = ANY($1)
    """, ["bigint[]"])
    context_rows = plpy.execute(context_plan, [p_context_atom_ids])
    
    atom_ids = []
    texts = []
    positions = []
    
    for row in context_rows:
        atom_ids.append(row['atom_id'])
        texts.append(row['canonical_text'])
        positions.append([row['x'], row['y'], row['z']])
    
    context_pos_gpu = cp.array(positions, dtype=cp.float32)
    
    diff = context_pos_gpu - query_pos
    distances = cp.sqrt(cp.sum(diff ** 2, axis=1))
    
    similarities = 1.0 / (1.0 + distances)
    attention_weights = cp.exp(similarities) / cp.sum(cp.exp(similarities))
    
    top_k_indices = cp.argsort(attention_weights)[-p_k:][::-1]
    
    top_k_indices_cpu = top_k_indices.get()
    attention_weights_cpu = attention_weights.get()
    
    results = []
    for idx in top_k_indices_cpu:
        results.append({
            'atom_id': atom_ids[idx],
            'attention_weight': float(attention_weights_cpu[idx]),
            'canonical_text': texts[idx]
        })
    
    return results
$$;

COMMENT ON FUNCTION compute_attention_gpu(BIGINT, BIGINT[], INTEGER) IS
'GPU-accelerated attention computation via CuPy.';
