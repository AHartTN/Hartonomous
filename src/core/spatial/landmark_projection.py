"""
Landmark-based spatial positioning system.
Maps semantic concepts to 3D coordinates using fixed landmarks in PostGIS POINTZM geometry.

Architecture:
- X: Modality (type of information)
- Y: Category (semantic role)  
- Z: Specificity (abstraction level)
- M: Hilbert curve index (spatial key for efficient querying)
"""

import hashlib
import numpy as np
from typing import Dict, Tuple, Optional
from .hilbert_curve import encode_hilbert_3d

# Modality landmarks (X-axis) - fundamental types of information
MODALITY_LANDMARKS: Dict[str, float] = {
    'code': 0.1,
    'text': 0.3,
    'numeric': 0.4,
    'image': 0.5,
    'audio': 0.7,
    'video': 0.9,
    'binary': 0.95,
    'graph': 0.25,
    'document': 0.35,
    'structured': 0.45,
}

# Category landmarks (Y-axis) - semantic roles and purposes
CATEGORY_LANDMARKS: Dict[str, float] = {
    # Structural elements
    'file': 0.05,
    'namespace': 0.1,
    'module': 0.12,
    'package': 0.14,
    'class': 0.15,
    'interface': 0.18,
    'struct': 0.2,
    'enum': 0.22,
    
    # Behavioral elements  
    'method': 0.3,
    'function': 0.32,
    'property': 0.35,
    'event': 0.38,
    'callback': 0.39,
    
    # Data elements
    'field': 0.5,
    'variable': 0.52,
    'parameter': 0.55,
    'literal': 0.58,
    'constant': 0.6,
    
    # Metadata elements
    'comment': 0.7,
    'attribute': 0.72,
    'annotation': 0.75,
    'decorator': 0.77,
    
    # Compositional elements
    'statement': 0.85,
    'expression': 0.87,
    'operator': 0.9,
    'keyword': 0.95,
    
    # Data structure elements
    'array': 0.62,
    'list': 0.63,
    'dict': 0.64,
    'set': 0.65,
    'tuple': 0.66,
}

# Specificity landmarks (Z-axis) - level of abstraction
SPECIFICITY_LANDMARKS: Dict[str, float] = {
    'abstract': 0.1,      # Pure concepts, interfaces
    'generic': 0.3,       # Type parameters, templates
    'concrete': 0.5,      # Implementations, classes
    'instance': 0.7,      # Objects, variables
    'literal': 0.9,       # Constants, raw values
}


def compute_position(
    modality: str,
    category: str,
    specificity: Optional[str] = None,
    identifier: Optional[str] = None,
    hilbert_order: int = 21
) -> Tuple[float, float, float, int]:
    """
    Compute 3D spatial position and Hilbert index for an atom.
    
    Args:
        modality: Type of information (code, text, image, etc.)
        category: Semantic role (class, method, field, etc.)
        specificity: Abstraction level (abstract, concrete, literal)
        identifier: Unique identifier for fine-tuning position
        hilbert_order: Hilbert curve resolution (default 21 = 2M³ resolution)
        
    Returns:
        Tuple of (x, y, z, hilbert_index)
    """
    # Base coordinates from landmarks
    x = MODALITY_LANDMARKS.get(modality.lower(), 0.5)
    y = CATEGORY_LANDMARKS.get(category.lower(), 0.5)
    z = SPECIFICITY_LANDMARKS.get(specificity.lower(), 0.5) if specificity else 0.5
    
    # Fine-tune position using identifier hash (prevents exact overlaps)
    if identifier:
        hash_bytes = hashlib.sha256(identifier.encode('utf-8')).digest()
        
        # Add small perturbation (±0.05) based on hash
        x += (hash_bytes[0] % 100 - 50) / 1000.0
        y += (hash_bytes[1] % 100 - 50) / 1000.0
        z += (hash_bytes[2] % 100 - 50) / 1000.0
        
        # Clamp to [0, 1]
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)
    
    # Compute Hilbert index for spatial key
    hilbert_index = encode_hilbert_3d(x, y, z, hilbert_order)
    
    return (x, y, z, hilbert_index)


def infer_specificity(
    node_type: str,
    is_abstract: bool = False,
    has_value: bool = False
) -> str:
    """
    Infer specificity level from node type and context.
    
    Args:
        node_type: Type of the node/atom
        is_abstract: Whether the node is abstract
        has_value: Whether the node has a concrete value
        
    Returns:
        Specificity level string
    """
    if is_abstract:
        return 'abstract'
    
    node_lower = node_type.lower()
    
    if node_lower in ('interface', 'abstract-class', 'protocol'):
        return 'abstract'
    elif node_lower in ('generic-parameter', 'type-parameter', 'template'):
        return 'generic'
    elif node_lower in ('class', 'method', 'function', 'field'):
        return 'concrete'
    elif node_lower in ('variable', 'parameter', 'instance'):
        return 'instance'
    elif node_lower in ('literal', 'constant') or has_value:
        return 'literal'
    else:
        return 'concrete'


def compute_distance(
    pos1: Tuple[float, float, float],
    pos2: Tuple[float, float, float]
) -> float:
    """
    Compute Euclidean distance between two 3D positions.
    
    Args:
        pos1: First position (x, y, z)
        pos2: Second position (x, y, z)
        
    Returns:
        Euclidean distance
    """
    return np.sqrt(sum((a - b) ** 2 for a, b in zip(pos1, pos2)))


def get_nearest_category(y: float) -> str:
    """
    Get nearest landmark category for a given Y coordinate.
    
    Args:
        y: Y coordinate
        
    Returns:
        Nearest category name
    """
    return min(
        CATEGORY_LANDMARKS.items(),
        key=lambda kv: abs(kv[1] - y)
    )[0]


def get_all_landmarks() -> Dict[str, Tuple[float, float, float]]:
    """
    Get all landmark coordinates for visualization/debugging.
    
    Returns:
        Dictionary mapping landmark keys to (x, y, z) coordinates
    """
    landmarks = {}
    
    for mod_key, mod_val in MODALITY_LANDMARKS.items():
        for cat_key, cat_val in CATEGORY_LANDMARKS.items():
            for spec_key, spec_val in SPECIFICITY_LANDMARKS.items():
                key = f"{mod_key}:{cat_key}:{spec_key}"
                landmarks[key] = (mod_val, cat_val, spec_val)
    
    return landmarks
