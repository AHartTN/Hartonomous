-- ============================================================================
-- Atomize Text (BYTE-LEVEL)
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Decompose text into individual CHARACTER atoms
-- Each character is ?64 bytes (UTF-8 encoding)
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_text(
    p_text TEXT,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS BIGINT[]
LANGUAGE plpgsql
AS $$
DECLARE
    v_char TEXT;
    v_atom_ids BIGINT[];
    v_atom_id BIGINT;
    v_char_metadata JSONB;
BEGIN
    IF p_text IS NULL OR length(p_text) = 0 THEN
        RETURN ARRAY[]::BIGINT[];
    END IF;
    
    v_char_metadata := p_metadata || '{"modality": "character"}'::jsonb;
    
    -- Atomize EACH character individually
    FOR i IN 1..length(p_text) LOOP
        v_char := substring(p_text FROM i FOR 1);
        
        -- Each character is a separate atom
        v_atom_id := atomize_value(
            convert_to(v_char, 'UTF8'),
            v_char,
            v_char_metadata || jsonb_build_object('position', i)
        );
        
        v_atom_ids := array_append(v_atom_ids, v_atom_id);
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;

COMMENT ON FUNCTION atomize_text(TEXT, JSONB) IS 
'Atomize text at CHARACTER level. Each character becomes a separate atom.
Example: "Hello" ? [H_atom, e_atom, l_atom, l_atom, o_atom]';
