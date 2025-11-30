"""
Landmark projector - projects atoms into 3D semantic space.

Core algorithm for positioning atoms based on landmark distances.
"""

import hashlib
import re
from typing import Any, Dict, List, Optional, Tuple

import numpy as np

from .hilbert_encoder import HilbertEncoder
from .landmark_position import LandmarkPosition
from .landmark_registry import LandmarkRegistry
from .landmark_type import LandmarkType


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
        self, modality: str, subtype: Optional[str] = None, specificity: float = 0.5
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
        modality_map = {
            "text": LandmarkType.MODALITY_TEXT,
            "character": LandmarkType.MODALITY_TEXT,
            "word": LandmarkType.MODALITY_TEXT,
            "image": LandmarkType.MODALITY_IMAGE,
            "image_patch": LandmarkType.MODALITY_IMAGE,
            "audio": LandmarkType.MODALITY_AUDIO,
            "phoneme": LandmarkType.MODALITY_AUDIO,
            "video": LandmarkType.MODALITY_VIDEO,
            "code": LandmarkType.MODALITY_CODE,
            "model": LandmarkType.MODALITY_MODEL,
            "structured": LandmarkType.MODALITY_STRUCTURED,
        }

        modality_landmark = self.registry.get_landmark(
            modality_map.get(modality.lower(), LandmarkType.MODALITY_TEXT)
        )

        if subtype:
            if "literal" in subtype.lower():
                category = LandmarkType.CATEGORY_LITERAL
            elif "abstract" in subtype.lower():
                category = LandmarkType.CATEGORY_ABSTRACT
            elif "relation" in subtype.lower():
                category = LandmarkType.CATEGORY_RELATIONAL
            else:
                category = LandmarkType.CATEGORY_SYMBOLIC
        else:
            category = LandmarkType.CATEGORY_SYMBOLIC

        category_landmark = self.registry.get_landmark(category)

        if specificity < 0.25:
            spec_landmark = self.registry.get_landmark(
                LandmarkType.SPECIFICITY_UNIVERSAL
            )
        elif specificity < 0.50:
            spec_landmark = self.registry.get_landmark(
                LandmarkType.SPECIFICITY_AGGREGATE
            )
        elif specificity < 0.75:
            spec_landmark = self.registry.get_landmark(
                LandmarkType.SPECIFICITY_COMPOUND
            )
        else:
            spec_landmark = self.registry.get_landmark(LandmarkType.SPECIFICITY_ATOMIC)

        x = (
            modality_landmark.x * 0.6
            + category_landmark.x * 0.3
            + spec_landmark.x * 0.1
        )
        y = (
            modality_landmark.y * 0.2
            + category_landmark.y * 0.6
            + spec_landmark.y * 0.2
        )
        z = (
            modality_landmark.z * 0.1
            + category_landmark.z * 0.2
            + spec_landmark.z * 0.7
        )

        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)

        hilbert_index = self.hilbert.encode(x, y, z)

        return (x, y, z, hilbert_index)

    def project_from_content(
        self,
        content: bytes,
        modality: str,
        existing_atoms: Optional[List[Tuple[bytes, Tuple[float, float, float]]]] = None,
    ) -> Tuple[float, float, float, int]:
        """
        Project atom based on content similarity to existing atoms.

        Args:
            content: Raw atom content
            modality: Data modality
            existing_atoms: List of (content_hash, (x, y, z)) for similar atoms

        Returns:
            (x, y, z, hilbert_index)
        """
        if not existing_atoms or len(existing_atoms) == 0:
            return self.project_from_modality(modality)

        content_hash = hashlib.sha256(content).digest()

        similarities = []
        for atom_hash, (ax, ay, az) in existing_atoms:
            hamming = sum(a != b for a, b in zip(content_hash, atom_hash))
            similarity = 1.0 / (1.0 + hamming)
            similarities.append((similarity, ax, ay, az))

        total_weight = sum(s for s, _, _, _ in similarities)
        if total_weight > 0:
            x = sum(s * ax for s, ax, _, _ in similarities) / total_weight
            y = sum(s * ay for s, _, ay, _ in similarities) / total_weight
            z = sum(s * az for s, _, _, az in similarities) / total_weight
        else:
            x, y, z, _ = self.project_from_modality(modality)

        # Ensure coordinates stay within valid range
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)

        hilbert_index = self.hilbert.encode(x, y, z)

        return (x, y, z, hilbert_index)

    def project_model_constant(
        self, param_name: str, layer_name: str, value_magnitude: float
    ) -> Tuple[float, float, float, int]:
        """
        Project model parameter/constant.

        Args:
            param_name: Parameter name (e.g., 'weight', 'bias')
            layer_name: Layer identifier
            value_magnitude: Magnitude of parameter value

        Returns:
            (x, y, z, hilbert_index)
        """
        base_x, base_y, base_z, _ = self.project_from_modality("model", param_name)

        layer_nums = re.findall(r"\d+", layer_name)
        if layer_nums:
            layer_depth = int(layer_nums[0]) / 100.0
            base_z = layer_depth

        mag_factor = np.tanh(value_magnitude)
        base_x = 0.5 + (base_x - 0.5) * (1.0 + mag_factor * 0.2)
        base_y = 0.5 + (base_y - 0.5) * (1.0 + mag_factor * 0.2)

        x = np.clip(base_x, 0.0, 1.0)
        y = np.clip(base_y, 0.0, 1.0)
        z = np.clip(base_z, 0.0, 1.0)

        hilbert_index = self.hilbert.encode(x, y, z)

        return (x, y, z, hilbert_index)

    def find_similar(
        self, position: Tuple[float, float, float], radius: int = 1000
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

    def extract_code_landmarks(
        self,
        code_vector: np.ndarray,
        language: str,
        ast_data: Optional[Dict[str, Any]] = None,
    ) -> List[LandmarkPosition]:
        """
        Extract landmarks for code based on code vector and optional AST.

        Args:
            code_vector: Numeric representation of code chunk
            language: Programming language
            ast_data: Optional AST structure from tree-sitter

        Returns:
            List of landmark positions for this code
        """
        x, y, z, hilbert_idx = self.project_from_modality("code", subtype=language)

        main_landmark = LandmarkPosition(
            x=x, y=y, z=z, landmark_type=LandmarkType.MODALITY_CODE
        )

        landmarks = [main_landmark]

        if ast_data:
            ast_type = ast_data.get("type", "unknown")
            depth = ast_data.get("depth", 0)

            ast_hash = hashlib.sha256(ast_type.encode()).digest()
            type_offset_x = (ast_hash[0] % 100) / 1000.0
            type_offset_y = (ast_hash[1] % 100) / 1000.0
            depth_factor = min(depth / 20.0, 0.5)

            ast_landmark = LandmarkPosition(
                x=np.clip(x + type_offset_x, 0.0, 1.0),
                y=np.clip(y + type_offset_y, 0.0, 1.0),
                z=np.clip(z + depth_factor, 0.0, 1.0),
                landmark_type=LandmarkType.SPECIFICITY_COMPOUND,
            )
            landmarks.append(ast_landmark)

        return landmarks
