-- ============================================================================
-- Export to ONNX Format (PL/Python)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Purpose: Export atom weights as ONNX model for external inference
-- ============================================================================

CREATE OR REPLACE FUNCTION export_to_onnx(
    p_atom_ids BIGINT[],
    p_output_path TEXT DEFAULT '/tmp/hartonomous_model.onnx'
)
RETURNS TEXT
LANGUAGE plpython3u
AS $$
import numpy as np
import onnx
from onnx import helper, TensorProto

# Fetch atom weights (relations)
plan = plpy.prepare("""
    SELECT 
        ar.source_atom_id,
        ar.target_atom_id,
        ar.weight
    FROM atom_relation ar
    WHERE ar.source_atom_id = ANY($1)
      AND ar.target_atom_id = ANY($1)
    ORDER BY ar.source_atom_id, ar.target_atom_id
""", ["bigint[]"])

result = plpy.execute(plan, [p_atom_ids])

if len(result) == 0:
    return "ERROR: No relations found"

# Build weight matrix
n = len(p_atom_ids)
atom_id_to_idx = {aid: i for i, aid in enumerate(p_atom_ids)}
weights = np.zeros((n, n), dtype=np.float32)

for row in result:
    src_idx = atom_id_to_idx.get(row['source_atom_id'])
    tgt_idx = atom_id_to_idx.get(row['target_atom_id'])
    if src_idx is not None and tgt_idx is not None:
        weights[src_idx][tgt_idx] = row['weight']

# Create ONNX graph
input_node = helper.make_tensor_value_info('input', TensorProto.FLOAT, [1, n])
output_node = helper.make_tensor_value_info('output', TensorProto.FLOAT, [1, n])

# Weight tensor
weight_tensor = helper.make_tensor(
    name='weights',
    data_type=TensorProto.FLOAT,
    dims=weights.shape,
    vals=weights.flatten().tolist()
)

# MatMul node
matmul_node = helper.make_node(
    'MatMul',
    inputs=['input', 'weights'],
    outputs=['output']
)

# Build graph
graph_def = helper.make_graph(
    [matmul_node],
    'hartonomous_model',
    [input_node],
    [output_node],
    [weight_tensor]
)

# Build model
model_def = helper.make_model(graph_def, producer_name='hartonomous')

# Save
onnx.save(model_def, p_output_path)

return f"SUCCESS: Exported {n}x{n} weight matrix to {p_output_path}"
$$;

COMMENT ON FUNCTION export_to_onnx(BIGINT[], TEXT) IS 
'Export atom relation weights as ONNX model.
Converts atom_relation graph ? weight matrix ? ONNX format.
Use for: deploying models to edge devices, model compression, external inference.';
