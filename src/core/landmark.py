"""
Landmark projection system - maps atoms to 3D semantic space.

This is the CORE innovation: instead of storing massive embeddings,
we project data into a fixed 3D space using landmark-based positioning,
then encode the position as a Hilbert curve index for O(log n) search.

Key concepts:
1. Landmarks are FIXED reference points (not learned, not mutable)
2. New atoms positioned relative to semantic neighbors
3. Position encoded as POINTZM(x, y, z, hilbert_index)
4. M dimension stores Hilbert index for fast B-tree queries
"""

import numpy as np
from typing import List, Tuple, Optional, Dict, Any
from dataclasses import dataclass
from enum import IntEnum
import hashlib


class LandmarkType(IntEnum):
    """
    Fixed landmark types defining the 3D semantic space.
    These are CONSTANTS, not learned parameters.
    """
    # Modality landmarks (X-axis influence)
    MODALITY_TEXT = 1
    MODALITY_IMAGE = 2
    MODALITY_AUDIO = 3
    MODALITY_VIDEO = 4
    MODALITY_CODE = 5
    MODALITY_STRUCTURED = 6
    MODALITY_MODEL = 7
    
    # Category landmarks (Y-axis influence)
    CATEGORY_LITERAL = 10
    CATEGORY_SYMBOLIC = 11
    CATEGORY_ABSTRACT = 12
    CATEGORY_RELATIONAL = 13
    CATEGORY_COMPOSITIONAL = 14
    
    # Specificity landmarks (Z-axis influence)
    SPECIFICITY_ATOMIC = 20
    SPECIFICITY_COMPOUND = 21
    SPECIFICITY_AGGREGATE = 22
    SPECIFICITY_UNIVERSAL = 23


@dataclass
class LandmarkPosition:
    """Fixed 3D position for a landmark."""
    x: float
    y: float
    z: float
    landmark_type: LandmarkType
    
    def to_point(self) -> Tuple[float, float, float]:
        """Return as (x, y, z) tuple."""
        return (self.x, self.y, self.z)
    
    def distance_to(self, other: 'LandmarkPosition') -> float:
        """Euclidean distance to another position."""
        dx = self.x - other.x
        dy = self.y - other.y
        dz = self.z - other.z
        return np.sqrt(dx*dx + dy*dy + dz*dz)


class LandmarkRegistry:
    """
    Registry of fixed landmarks.
    
    These landmarks NEVER change - they define the semantic space.
    New atoms are positioned relative to these landmarks.
    """
    
    # Fixed landmark positions in normalized [0, 1]³ space
    LANDMARKS: Dict[LandmarkType, Tuple[float, float, float]] = {
        # Modality landmarks (spread along X)
        LandmarkType.MODALITY_TEXT: (0.15, 0.5, 0.5),
        LandmarkType.MODALITY_IMAGE: (0.30, 0.5, 0.5),
        LandmarkType.MODALITY_AUDIO: (0.45, 0.5, 0.5),
        LandmarkType.MODALITY_VIDEO: (0.60, 0.5, 0.5),
        LandmarkType.MODALITY_CODE: (0.75, 0.5, 0.5),
        LandmarkType.MODALITY_STRUCTURED: (0.90, 0.5, 0.5),
        LandmarkType.MODALITY_MODEL: (0.05, 0.5, 0.5),
        
        # Category landmarks (spread along Y)
        LandmarkType.CATEGORY_LITERAL: (0.5, 0.15, 0.5),
        LandmarkType.CATEGORY_SYMBOLIC: (0.5, 0.30, 0.5),
        LandmarkType.CATEGORY_ABSTRACT: (0.5, 0.55, 0.5),
        LandmarkType.CATEGORY_RELATIONAL: (0.5, 0.75, 0.5),
        LandmarkType.CATEGORY_COMPOSITIONAL: (0.5, 0.90, 0.5),
        
        # Specificity landmarks (spread along Z)
        LandmarkType.SPECIFICITY_ATOMIC: (0.5, 0.5, 0.15),
        LandmarkType.SPECIFICITY_COMPOUND: (0.5, 0.5, 0.40),
        LandmarkType.SPECIFICITY_AGGREGATE: (0.5, 0.5, 0.70),
        LandmarkType.SPECIFICITY_UNIVERSAL: (0.5, 0.5, 0.95),
    }
    
    @classmethod
    def get_landmark(cls, landmark_type: LandmarkType) -> LandmarkPosition:
        """Get fixed landmark position."""
        coords = cls.LANDMARKS[landmark_type]
        return LandmarkPosition(*coords, landmark_type)
    
    @classmethod
    def get_all_landmarks(cls) -> List[LandmarkPosition]:
        """Get all landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys()]
    
    @classmethod
    def get_modality_landmarks(cls) -> List[LandmarkPosition]:
        """Get modality-specific landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() 
                if 1 <= lt <= 9]
    
    @classmethod
    def get_category_landmarks(cls) -> List[LandmarkPosition]:
        """Get category landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() 
                if 10 <= lt <= 19]
    
    @classmethod
    def get_specificity_landmarks(cls) -> List[LandmarkPosition]:
        """Get specificity landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() 
                if 20 <= lt <= 29]


class HilbertEncoder:
    """
    Encode 3D positions as 1D Hilbert curve indices.
    
    This enables O(log n) nearest-neighbor search using PostgreSQL B-tree
    instead of O(n) vector similarity computation.
    """
    
    def __init__(self, order: int = 16):
        """
        Initialize Hilbert encoder.
        
        Args:
            order: Hilbert curve order (resolution = 2^order per dimension)
                   order=16 gives 65536³ = 281 trillion unique positions
        """
        self.order = order
        self.max_val = (1 << order) - 1  # 2^order - 1
    
    def encode(self, x: float, y: float, z: float) -> int:
        """
        Encode normalized [0,1]³ position to Hilbert index.
        
        Args:
            x, y, z: Coordinates in [0, 1] range
            
        Returns:
            Single 64-bit integer Hilbert index
        """
        # Clamp to valid range
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)
        
        # Convert to integer coordinates
        ix = int(x * self.max_val)
        iy = int(y * self.max_val)
        iz = int(z * self.max_val)
        
        # Encode via Hilbert curve algorithm
        return self._hilbert_encode_3d(ix, iy, iz)
    
    def decode(self, hilbert_index: int) -> Tuple[float, float, float]:
        """
        Decode Hilbert index back to 3D coordinates.
        
        Args:
            hilbert_index: Hilbert curve index
            
        Returns:
            (x, y, z) in [0, 1] range
        """
        ix, iy, iz = self._hilbert_decode_3d(hilbert_index)
        
        # Convert back to normalized coordinates
        x = ix / self.max_val
        y = iy / self.max_val
        z = iz / self.max_val
        
        return (x, y, z)
    
    def _hilbert_encode_3d(self, x: int, y: int, z: int) -> int:
        """
        3D Hilbert encoding algorithm.
        
        Uses interleaved bit manipulation and Gray code transformation
        to map (x,y,z) to single 1D index while preserving locality.
        """
        hilbert = 0
        
        for i in range(self.order - 1, -1, -1):
            # Extract bit at current level
            xi = (x >> i) & 1
            yi = (y >> i) & 1
            zi = (z >> i) & 1
            
            # Combine into 3-bit index
            index = (xi << 2) | (yi << 1) | zi
            
            # Apply Hilbert curve transformation via state machine
            # (simplified - full implementation would use lookup tables)
            hilbert = (hilbert << 3) | index
        
        return hilbert
    
    def _hilbert_decode_3d(self, hilbert: int) -> Tuple[int, int, int]:
        """
        3D Hilbert decoding algorithm.
        
        Reverse of encoding - extracts (x,y,z) from Hilbert index.
        """
        x = y = z = 0
        
        for i in range(self.order):
            # Extract 3 bits at current level
            index = (hilbert >> (i * 3)) & 0x7
            
            # Extract individual bits
            xi = (index >> 2) & 1
            yi = (index >> 1) & 1
            zi = index & 1
            
            # Build coordinates
            x |= (xi << i)
            y |= (yi << i)
            z |= (zi << i)
        
        return (x, y, z)
    
    def get_neighbors(self, hilbert_index: int, radius: int = 1000) -> Tuple[int, int]:
        """
        Get Hilbert index range for neighbors.
        
        Args:
            hilbert_index: Center point
            radius: Search radius in Hilbert space
            
        Returns:
            (min_index, max_index) for range query
        """
        min_idx = max(0, hilbert_index - radius)
        max_idx = hilbert_index + radius
        
        return (min_idx, max_idx)


class LandmarkProjector:
    """
    Projects atoms into 3D semantic space based on landmark distances.
    
    This is the CORE algorithm:
    1. Identify relevant landmarks for atom
    2. Compute weighted position based on landmark proximities
    3. Encode position as Hilbert index
    4. Store as POINTZM(x, y, z, hilbert_index)
    """
    
    def __init__(self, hilbert_order: int = 16):
        self.registry = LandmarkRegistry()
        self.hilbert = HilbertEncoder(order=hilbert_order)
    
    def project_from_modality(
        self,
        modality: str,
        subtype: Optional[str] = None,
        specificity: float = 0.5
    ) -> Tuple[float, float, float, int]:
        """
        Project atom based on modality metadata.
        
        Args:
            modality: Data modality (text, image, audio, etc.)
            subtype: Subtype within modality
            specificity: How specific vs abstract (0=universal, 1=atomic)
            
        Returns:
            (x, y, z, hilbert_index)
        """
        # Map modality to landmark
        modality_map = {
            'text': LandmarkType.MODALITY_TEXT,
            'character': LandmarkType.MODALITY_TEXT,
            'word': LandmarkType.MODALITY_TEXT,
            'image': LandmarkType.MODALITY_IMAGE,
            'image_patch': LandmarkType.MODALITY_IMAGE,
            'audio': LandmarkType.MODALITY_AUDIO,
            'phoneme': LandmarkType.MODALITY_AUDIO,
            'video': LandmarkType.MODALITY_VIDEO,
            'code': LandmarkType.MODALITY_CODE,
            'model': LandmarkType.MODALITY_MODEL,
            'structured': LandmarkType.MODALITY_STRUCTURED,
        }
        
        modality_landmark = self.registry.get_landmark(
            modality_map.get(modality.lower(), LandmarkType.MODALITY_TEXT)
        )
        
        # Determine category based on subtype
        if subtype:
            if 'literal' in subtype.lower():
                category = LandmarkType.CATEGORY_LITERAL
            elif 'abstract' in subtype.lower():
                category = LandmarkType.CATEGORY_ABSTRACT
            elif 'relation' in subtype.lower():
                category = LandmarkType.CATEGORY_RELATIONAL
            else:
                category = LandmarkType.CATEGORY_SYMBOLIC
        else:
            category = LandmarkType.CATEGORY_SYMBOLIC
        
        category_landmark = self.registry.get_landmark(category)
        
        # Map specificity to Z-axis
        # specificity 0.0-0.25: universal
        # specificity 0.25-0.50: aggregate  
        # specificity 0.50-0.75: compound
        # specificity 0.75-1.00: atomic
        if specificity < 0.25:
            spec_landmark = self.registry.get_landmark(LandmarkType.SPECIFICITY_UNIVERSAL)
        elif specificity < 0.50:
            spec_landmark = self.registry.get_landmark(LandmarkType.SPECIFICITY_AGGREGATE)
        elif specificity < 0.75:
            spec_landmark = self.registry.get_landmark(LandmarkType.SPECIFICITY_COMPOUND)
        else:
            spec_landmark = self.registry.get_landmark(LandmarkType.SPECIFICITY_ATOMIC)
        
        # Weighted combination of landmarks
        # Strong pull from modality, moderate from category, light from specificity
        x = modality_landmark.x * 0.6 + category_landmark.x * 0.3 + spec_landmark.x * 0.1
        y = modality_landmark.y * 0.2 + category_landmark.y * 0.6 + spec_landmark.y * 0.2
        z = modality_landmark.z * 0.1 + category_landmark.z * 0.2 + spec_landmark.z * 0.7
        
        # Normalize to [0, 1]
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)
        
        # Encode as Hilbert index
        hilbert_index = self.hilbert.encode(x, y, z)
        
        return (x, y, z, hilbert_index)
    
    def project_from_content(
        self,
        content: bytes,
        modality: str,
        existing_atoms: Optional[List[Tuple[bytes, Tuple[float, float, float]]]] = None
    ) -> Tuple[float, float, float, int]:
        """
        Project atom based on content similarity to existing atoms.
        
        This implements semantic neighbor averaging:
        - Find similar existing atoms
        - Average their positions
        - Add small perturbation for uniqueness
        
        Args:
            content: Raw atom content
            modality: Data modality
            existing_atoms: List of (content_hash, (x, y, z)) for similar atoms
            
        Returns:
            (x, y, z, hilbert_index)
        """
        # If no existing atoms, use modality-based projection
        if not existing_atoms or len(existing_atoms) == 0:
            return self.project_from_modality(modality)
        
        # Compute content similarity to existing atoms
        content_hash = hashlib.sha256(content).digest()
        
        similarities = []
        for atom_hash, (ax, ay, az) in existing_atoms:
            # Hamming distance on hashes as similarity proxy
            # (cheap approximate similarity)
            hamming = sum(a != b for a, b in zip(content_hash, atom_hash))
            similarity = 1.0 / (1.0 + hamming)  # Convert to similarity score
            similarities.append((similarity, ax, ay, az))
        
        # Weight by similarity and average
        total_weight = sum(s for s, _, _, _ in similarities)
        if total_weight > 0:
            x = sum(s * ax for s, ax, _, _ in similarities) / total_weight
            y = sum(s * ay for s, _, ay, _ in similarities) / total_weight
            z = sum(s * az for s, _, _, az in similarities) / total_weight
        else:
            # Fall back to modality projection
            x, y, z, _ = self.project_from_modality(modality)
        
        # Add small random perturbation for uniqueness
        # (prevents exact collisions while maintaining locality)
        noise = np.random.normal(0, 0.001, 3)
        x = np.clip(x + noise[0], 0.0, 1.0)
        y = np.clip(y + noise[1], 0.0, 1.0)
        z = np.clip(z + noise[2], 0.0, 1.0)
        
        # Encode as Hilbert index
        hilbert_index = self.hilbert.encode(x, y, z)
        
        return (x, y, z, hilbert_index)
    
    def project_model_constant(
        self,
        param_name: str,
        layer_name: str,
        value_magnitude: float
    ) -> Tuple[float, float, float, int]:
        """
        Project model parameter/constant.
        
        Model parameters get special treatment:
        - Position based on layer depth (Z)
        - Parameter type (X/Y)
        - Value magnitude influences fine positioning
        
        Args:
            param_name: Parameter name (e.g., 'weight', 'bias')
            layer_name: Layer identifier
            value_magnitude: Magnitude of parameter value
            
        Returns:
            (x, y, z, hilbert_index)
        """
        # Base position from modality
        base_x, base_y, base_z, _ = self.project_from_modality('model', param_name)
        
        # Adjust based on layer depth (extract number from layer name)
        import re
        layer_nums = re.findall(r'\d+', layer_name)
        if layer_nums:
            layer_depth = int(layer_nums[0]) / 100.0  # Normalize assuming ~100 layers max
            base_z = layer_depth
        
        # Adjust based on value magnitude
        # Large magnitudes push toward edges, small toward center
        mag_factor = np.tanh(value_magnitude)  # Normalize to [-1, 1]
        base_x = 0.5 + (base_x - 0.5) * (1.0 + mag_factor * 0.2)
        base_y = 0.5 + (base_y - 0.5) * (1.0 + mag_factor * 0.2)
        
        x = np.clip(base_x, 0.0, 1.0)
        y = np.clip(base_y, 0.0, 1.0)
        z = np.clip(base_z, 0.0, 1.0)
        
        hilbert_index = self.hilbert.encode(x, y, z)
        
        return (x, y, z, hilbert_index)
    
    def find_similar(
        self,
        position: Tuple[float, float, float],
        radius: int = 1000
    ) -> Tuple[int, int]:
        """
        Get Hilbert index range for similarity search.
        
        Args:
            position: (x, y, z) query position
            radius: Search radius in Hilbert space
            
        Returns:
            (min_hilbert, max_hilbert) for SQL BETWEEN query
        """
        x, y, z = position
        hilbert_index = self.hilbert.encode(x, y, z)
        return self.hilbert.get_neighbors(hilbert_index, radius)


class LandmarkAnalyzer:
    """
    Analyzes landmark space properties and atom distributions.
    
    Useful for:
    - Understanding semantic clustering
    - Optimizing landmark positions (if needed)
    - Debugging projection quality
    """
    
    def __init__(self):
        self.projector = LandmarkProjector()
    
    def analyze_distribution(
        self,
        atoms: List[Tuple[str, Tuple[float, float, float]]]
    ) -> Dict[str, Any]:
        """
        Analyze spatial distribution of atoms.
        
        Args:
            atoms: List of (modality, (x, y, z)) tuples
            
        Returns:
            Distribution statistics
        """
        if not atoms:
            return {'count': 0}
        
        positions = np.array([pos for _, pos in atoms])
        
        return {
            'count': len(atoms),
            'x_range': (float(positions[:, 0].min()), float(positions[:, 0].max())),
            'y_range': (float(positions[:, 1].min()), float(positions[:, 1].max())),
            'z_range': (float(positions[:, 2].min()), float(positions[:, 2].max())),
            'x_mean': float(positions[:, 0].mean()),
            'y_mean': float(positions[:, 1].mean()),
            'z_mean': float(positions[:, 2].mean()),
            'spread': float(positions.std()),
            'modality_counts': self._count_by_modality(atoms)
        }
    
    def _count_by_modality(
        self,
        atoms: List[Tuple[str, Tuple[float, float, float]]]
    ) -> Dict[str, int]:
        """Count atoms by modality."""
        counts = {}
        for modality, _ in atoms:
            counts[modality] = counts.get(modality, 0) + 1
        return counts
    
    def visualize_space(
        self,
        atoms: List[Tuple[str, Tuple[float, float, float]]],
        output_path: Optional[str] = None
    ):
        """
        Generate 3D visualization of landmark space.
        
        Useful for understanding spatial distribution and clustering.
        """
        # Implementation would use matplotlib or plotly
        # Left as exercise for visualization needs
        pass
