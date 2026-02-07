-- Debug "whale"
SET search_path TO hartonomous, public;

WITH target AS (
    SELECT composition_id, reconstructed_text 
    FROM v_composition_text 
    WHERE reconstructed_text = 'whale' OR reconstructed_text = 'Whale'
)
SELECT 
    t.reconstructed_text,
    rs1.compositionid AS source_id,
    rs2.compositionid AS target_id,
    v2.reconstructed_text AS target_text,
    rr.observations,
    rr.ratingvalue
FROM target t
JOIN relationsequence rs1 ON rs1.compositionid = t.composition_id
JOIN relationsequence rs2 ON rs2.relationid = rs1.relationid AND rs2.compositionid != rs1.compositionid
LEFT JOIN v_composition_text v2 ON v2.composition_id = rs2.compositionid
LEFT JOIN relationrating rr ON rr.relationid = rs1.relationid;
