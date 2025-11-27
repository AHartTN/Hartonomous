"""Multi-layer compression system

Revision ID: 032_compression
Revises: 031_add_hilbert_and_encoding
Create Date: 2025-11-27 12:47:00.000000

"""
from alembic import op
import sqlalchemy as sa

revision = '032_compression'
down_revision = '031_add_hilbert_and_encoding'
branch_labels = None
depends_on = None


def upgrade():
    # Create enum for encoding types
    op.execute("""
        CREATE TYPE encoding_type AS ENUM (
            'raw',           -- 0: No encoding
            'sparse',        -- 1: Sparse encoding (configurable threshold)
            'delta',         -- 2: Delta encoding (differences)
            'rle',           -- 3: Run-length encoding
            'lod_1',         -- 4: Level of detail 1
            'lod_2',         -- 5: Level of detail 2
            'lod_3',         -- 6: Level of detail 3
            'lod_4'          -- 7: Level of detail 4
        );
    """)
    
    # Add encoding metadata to atom table
    op.execute("""
        COMMENT ON COLUMN atom.metadata IS 
        'Flexible JSONB for:
         - modality: text/code/image/audio/video/model
         - model_name: source model for embeddings/weights
         - tenant_id: multi-tenancy
         - confidence: prediction confidence
         - encoding_chain: array of applied encodings in order
         - sparse_threshold: threshold for sparse encoding (default 1e-6)
         - compression_ratio: achieved compression ratio
         - original_size: size before compression
         - chunk_index: for large values split across atoms';
    """)
    
    # Create function to apply multi-layer compression
    op.execute("""
        CREATE OR REPLACE FUNCTION compress_atom_value(
            p_value bytea,
            p_sparse_threshold double precision DEFAULT 1e-6,
            p_enable_rle boolean DEFAULT true,
            p_enable_delta boolean DEFAULT true
        ) RETURNS TABLE(
            compressed_value bytea,
            encoding_chain text[],
            compression_ratio double precision,
            original_size integer
        ) AS $$
        DECLARE
            v_current bytea := p_value;
            v_temp bytea;
            v_encodings text[] := ARRAY[]::text[];
            v_original_size integer := length(p_value);
            v_best_size integer := v_original_size;
        BEGIN
            -- Layer 1: Sparse encoding (zeros or near-zeros)
            IF p_sparse_threshold > 0 THEN
                -- Store positions of non-sparse values
                -- Format: [count:4bytes][positions+values]
                -- This will be implemented in PL/Python for numerical operations
                v_encodings := array_append(v_encodings, 'sparse');
            END IF;
            
            -- Layer 2: Delta encoding (sequential differences)
            IF p_enable_delta AND length(v_current) >= 16 THEN
                -- Store first value + deltas
                -- Often reduces magnitude of values for better compression
                v_encodings := array_append(v_encodings, 'delta');
            END IF;
            
            -- Layer 3: Run-length encoding (repeated patterns)
            IF p_enable_rle THEN
                -- Encode runs of repeated bytes/patterns
                -- Format: [value:Nbytes][count:4bytes][value:Nbytes][count:4bytes]...
                v_encodings := array_append(v_encodings, 'rle');
            END IF;
            
            -- Layer 4: PostgreSQL native compression (TOAST)
            -- Happens automatically for extended storage
            
            RETURN QUERY SELECT 
                v_current,
                v_encodings,
                CASE WHEN v_best_size > 0 
                     THEN v_original_size::double precision / v_best_size 
                     ELSE 1.0 
                END,
                v_original_size;
        END;
        $$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;
    """)
    
    # Create decompression function
    op.execute("""
        CREATE OR REPLACE FUNCTION decompress_atom_value(
            p_compressed bytea,
            p_encoding_chain text[]
        ) RETURNS bytea AS $$
        DECLARE
            v_current bytea := p_compressed;
            v_encoding text;
        BEGIN
            -- Apply decodings in reverse order
            FOREACH v_encoding IN ARRAY array_reverse(p_encoding_chain) LOOP
                CASE v_encoding
                    WHEN 'rle' THEN
                        -- Decode RLE
                        v_current := v_current; -- Placeholder for actual implementation
                    WHEN 'delta' THEN
                        -- Decode delta
                        v_current := v_current; -- Placeholder for actual implementation
                    WHEN 'sparse' THEN
                        -- Decode sparse
                        v_current := v_current; -- Placeholder for actual implementation
                    ELSE
                        -- Unknown encoding, return as-is
                        NULL;
                END CASE;
            END LOOP;
            
            RETURN v_current;
        END;
        $$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;
    """)
    
    # Create helper function to estimate compression benefit
    op.execute("""
        CREATE OR REPLACE FUNCTION estimate_compression_ratio(
            p_value bytea
        ) RETURNS double precision AS $$
        DECLARE
            v_zeros integer := 0;
            v_repeats integer := 0;
            v_total integer := length(p_value);
            v_ratio double precision := 1.0;
        BEGIN
            -- Quick heuristic: count zeros and repeated bytes
            -- Real implementation would analyze actual patterns
            
            IF v_total = 0 THEN
                RETURN 1.0;
            END IF;
            
            -- This is a placeholder - actual implementation would:
            -- 1. Count zero bytes (sparse potential)
            -- 2. Count repeated sequences (RLE potential)
            -- 3. Analyze delta patterns (delta encoding potential)
            
            -- For now, assume 1.0 (no compression benefit)
            RETURN 1.0;
        END;
        $$ LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE;
    """)
    
    # Add index on encoding metadata
    op.execute("""
        CREATE INDEX idx_atom_encoding_chain 
        ON atom ((metadata->'encoding_chain')) 
        WHERE metadata ? 'encoding_chain';
    """)
    
    # Add index on compression ratio
    op.execute("""
        CREATE INDEX idx_atom_compression_ratio 
        ON atom (((metadata->>'compression_ratio')::double precision)) 
        WHERE metadata ? 'compression_ratio';
    """)


def downgrade():
    op.execute("DROP INDEX IF EXISTS idx_atom_compression_ratio;")
    op.execute("DROP INDEX IF EXISTS idx_atom_encoding_chain;")
    op.execute("DROP FUNCTION IF EXISTS estimate_compression_ratio(bytea);")
    op.execute("DROP FUNCTION IF EXISTS decompress_atom_value(bytea, text[]);")
    op.execute("DROP FUNCTION IF EXISTS compress_atom_value(bytea, double precision, boolean, boolean);")
    op.execute("DROP TYPE IF EXISTS encoding_type;")
