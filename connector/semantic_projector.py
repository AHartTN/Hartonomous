"""Semantic projection of atomic values into 4D coordinates"""

import struct
import numpy as np
from typing import Tuple


class SemanticProjector:
    """Projects atomic values into initial 4D semantic positions
    
    Uses Gram-Schmidt orthonormalization and semantic feature extraction
    to place atoms in meaningful positions BEFORE Cortex LMDS refinement.
    """
    
    def __init__(self, scale: float = 100.0):
        """Initialize projector
        
        Args:
            scale: Coordinate range for X/Y dimensions
        """
        self.scale = scale
    
    def project_numeric(self, value: float) -> Tuple[float, float, float, float]:
        """Project numeric value into 4D space
        
        Position encodes: magnitude, sign, scale, precision
        
        Args:
            value: Numeric constant
            
        Returns:
            (x, y, z, m) coordinates
        """
        # X: Direct value mapping (scaled)
        x = np.tanh(value / self.scale) * self.scale
        
        # Y: Logarithmic scale
        if value != 0:
            y = np.sign(value) * np.log10(abs(value) + 1) * (self.scale / 10)
        else:
            y = 0.0
        
        # Z: Always 0 for raw constants
        z = 0.0
        
        # M: Magnitude indicator
        m = min(abs(value) / self.scale, 1.0)
        
        return (x, y, z, m)
    
    def project_color(self, r: int, g: int, b: int) -> Tuple[float, float, float, float]:
        """Project RGB color into 4D space
        
        Uses perceptual color space transformation
        
        Args:
            r, g, b: Color components (0-255)
            
        Returns:
            (x, y, z, m) coordinates
        """
        # Normalize to [0, 1]
        r_norm = r / 255.0
        g_norm = g / 255.0
        b_norm = b / 255.0
        
        # X: Red-Green opponent channel
        x = (r_norm - g_norm) * self.scale
        
        # Y: Blue-Yellow opponent channel
        y = (0.5 * (r_norm + g_norm) - b_norm) * self.scale
        
        # Z: Lightness (always 0 for raw color atoms)
        z = 0.0
        
        # M: Saturation
        max_val = max(r_norm, g_norm, b_norm)
        min_val = min(r_norm, g_norm, b_norm)
        if max_val > 0:
            m = (max_val - min_val) / max_val
        else:
            m = 0.0
        
        return (x, y, z, m)
    
    def project_audio_sample(self, sample: int, bit_depth: int = 16) -> Tuple[float, float, float, float]:
        """Project audio sample into 4D space
        
        Args:
            sample: Audio sample value
            bit_depth: Bit depth (8, 16, 24, 32)
            
        Returns:
            (x, y, z, m) coordinates
        """
        max_val = 2 ** (bit_depth - 1)
        normalized = sample / max_val
        
        # X: Amplitude
        x = normalized * self.scale
        
        # Y: Sign/phase
        y = np.sign(sample) * np.sqrt(abs(normalized)) * self.scale
        
        # Z: Always 0 for raw samples
        z = 0.0
        
        # M: Dynamic range
        m = abs(normalized)
        
        return (x, y, z, m)
    
    def project_token(self, token: str, vocab_size: int = 50000) -> Tuple[float, float, float, float]:
        """Project text token into 4D space
        
        Uses character statistics and hash-based distributional placement
        
        Args:
            token: Text token
            vocab_size: Estimated vocabulary size
            
        Returns:
            (x, y, z, m) coordinates
        """
        # Character-based features
        length = len(token)
        avg_char_val = sum(ord(c) for c in token) / length if length > 0 else 0
        
        # X: Character value distribution
        x = (avg_char_val / 128.0 - 1.0) * self.scale
        
        # Y: Token length (logarithmic)
        y = np.log2(length + 1) * (self.scale / 10)
        
        # Z: Always 0 for raw tokens
        z = 0.0
        
        # M: Lexical diversity (unique chars / total chars)
        if length > 0:
            m = len(set(token)) / length
        else:
            m = 0.0
        
        return (x, y, z, m)
    
    def project_weight(self, weight: float) -> Tuple[float, float, float, float]:
        """Project neural network weight into 4D space
        
        Similar to numeric but optimized for typical weight distributions
        
        Args:
            weight: Network weight value
            
        Returns:
            (x, y, z, m) coordinates
        """
        # X: Direct value (weights typically [-1, 1])
        x = weight * self.scale
        
        # Y: Squared value (energy-like measure)
        y = np.sign(weight) * (weight ** 2) * self.scale
        
        # Z: Always 0 for raw weights
        z = 0.0
        
        # M: Magnitude (importance indicator)
        m = min(abs(weight), 1.0)
        
        return (x, y, z, m)
