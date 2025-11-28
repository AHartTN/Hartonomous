-- ============================================================================
-- Batch Atomize Text
-- Author: Anthony Hart
-- Copyright (c) 2025 Anthony Hart. All Rights Reserved.
-- 
-- Atomize multiple text strings in one call, reusing character atoms
-- Much faster than calling atomize_text() repeatedly
-- ============================================================================

CREATE OR REPLACE FUNCTION atomize_text_batch(
    p_texts TEXT[],
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS TABLE(
    text_index INT,
    char_atom_ids BIGINT[]
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_text TEXT;
    v_char TEXT;
    v_atom_id BIGINT;
    v_atom_ids BIGINT[];
    v_char_metadata JSONB;
    v_idx INT;
BEGIN
    v_char_metadata := p_metadata || '{"modality": "character"}'::jsonb;
    
    -- Process each text string
    FOR v_idx IN 1..array_length(p_texts, 1) LOOP
        v_text := p_texts[v_idx];
        v_atom_ids := ARRAY[]::BIGINT[];
        
        IF v_text IS NOT NULL AND length(v_text) > 0 THEN
            -- Atomize each character
            FOR i IN 1..length(v_text) LOOP
                v_char := substring(v_text FROM i FOR 1);
                
                -- atomize_value will deduplicate automatically
                v_atom_id := atomize_value(
                    convert_to(v_char, 'UTF8'),
                    v_char,
                    v_char_metadata
                );
                
                v_atom_ids := array_append(v_atom_ids, v_atom_id);
            END LOOP;
        END IF;
        
        text_index := v_idx;
        char_atom_ids := v_atom_ids;
        RETURN NEXT;
    END LOOP;
END;
$$;

COMMENT ON FUNCTION atomize_text_batch(TEXT[], JSONB) IS 
'Batch atomize multiple text strings. Returns array of character atom IDs for each input text.
Much faster than calling atomize_text() in a loop due to reduced context switches.';
