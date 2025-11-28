"""Test landmark projection and spatial positioning."""
import pytest
from src.core.spatial.landmark_projection import (
    compute_position,
    infer_specificity,
    compute_distance,
    get_nearest_category,
    MODALITY_LANDMARKS,
    CATEGORY_LANDMARKS,
    SPECIFICITY_LANDMARKS
)

class TestLandmarkProjection:
    """Test landmark projection functions."""
    
    def test_compute_position(self):
        """Test computing spatial position."""
        x, y, z, hilbert = compute_position(
            modality='code',
            category='function',
            specificity='concrete'
        )
        
        # Should return valid coordinates
        assert 0 <= x <= 1
        assert 0 <= y <= 1
        assert 0 <= z <= 1
        assert hilbert >= 0
    
    def test_deterministic_positioning(self):
        """Test that same input gives same position."""
        pos1 = compute_position('code', 'function', 'concrete', 'my_func')
        pos2 = compute_position('code', 'function', 'concrete', 'my_func')
        
        assert pos1 == pos2  # Deterministic
    
    def test_unique_identifiers_differ(self):
        """Test that different identifiers give slightly different positions."""
        pos1 = compute_position('code', 'function', 'concrete', 'func1')
        pos2 = compute_position('code', 'function', 'concrete', 'func2')
        
        # Should be different due to identifier hash perturbation
        assert pos1 != pos2
        
        # But should be close (same modality/category/specificity)
        dist = compute_distance(pos1[:3], pos2[:3])
        assert dist < 0.1  # Within same semantic cluster
    
    def test_infer_specificity(self):
        """Test specificity inference."""
        assert infer_specificity('interface', is_abstract=True) == 'abstract'
        assert infer_specificity('class') == 'concrete'
        assert infer_specificity('variable') == 'instance'
        assert infer_specificity('literal', has_value=True) == 'literal'
    
    def test_compute_distance(self):
        """Test Euclidean distance."""
        pos1 = (0.0, 0.0, 0.0)
        pos2 = (1.0, 0.0, 0.0)
        
        distance = compute_distance(pos1, pos2)
        assert distance == pytest.approx(1.0)
        
        pos3 = (1.0, 1.0, 1.0)
        distance2 = compute_distance(pos1, pos3)
        assert distance2 == pytest.approx(1.732, rel=0.01)  # sqrt(3)
    
    def test_get_nearest_category(self):
        """Test finding nearest category."""
        # Test exact match
        category = get_nearest_category(CATEGORY_LANDMARKS['function'])
        assert category == 'function'
        
        # Test approximate match
        category2 = get_nearest_category(0.31)  # Close to 'method' at 0.3
        assert category2 in ['method', 'function']
    
    def test_landmarks_exist(self):
        """Test that landmark dictionaries are populated."""
        assert len(MODALITY_LANDMARKS) > 0
        assert len(CATEGORY_LANDMARKS) > 0
        assert len(SPECIFICITY_LANDMARKS) > 0
        
        # Test key landmarks exist
        assert 'code' in MODALITY_LANDMARKS
        assert 'function' in CATEGORY_LANDMARKS
        assert 'concrete' in SPECIFICITY_LANDMARKS
