CREATE OR REPLACE VIEW hartonomous.v_semantic_neighbors AS
SELECT 
    r.Id as Relation_Id,
    rr.RatingValue as ELO,
    rr.Observations,
    p1.Hilbert as Source_Hilbert,
    p2.Hilbert as Neighbor_Hilbert
FROM hartonomous.Relation r
JOIN hartonomous.RelationSequence rs1 ON r.Id = rs1.RelationId AND rs1.Ordinal = 0
JOIN hartonomous.RelationSequence rs2 ON r.Id = rs2.RelationId AND rs2.Ordinal = 1
JOIN hartonomous.Composition c1 ON rs1.CompositionId = c1.Id
JOIN hartonomous.Physicality p1 ON c1.PhysicalityId = p1.Id
JOIN hartonomous.Composition c2 ON rs2.CompositionId = c2.Id
JOIN hartonomous.Physicality p2 ON c2.PhysicalityId = p2.Id
JOIN hartonomous.RelationRating rr ON r.Id = rr.RelationId
ORDER BY rr.RatingValue DESC;
