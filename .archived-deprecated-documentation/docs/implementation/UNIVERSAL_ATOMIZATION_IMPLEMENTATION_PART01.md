# Universal Atomization Implementation Plan - Part 1: Foundation & Entity Extraction

**Version:** 1.0.0  
**Date:** December 1, 2025  
**Status:** Implementation Planning

---

## Executive Summary

This document provides granular implementation details for the **Universal Geometric Atomization** system. We've solved the black box problem by representing ALL digital content (text, images, audio, video) as atoms in geometric space, enabling:

- **Content-Addressable Storage (CAS)**: Automatic deduplication
- **Semantic BPE**: Cross-modal pattern learning
- **Concept Spaces**: Linking content across modalities
- **Neo4j Graph**: Semantic relationship traversal
- **Pure SQL/PostGIS**: No external ML dependencies

---

## Implementation Task Overview (15 Tasks Total)

### ✅ Completed (Tasks 1-6): Foundation
1. Schema audit - discovered 50+ SQL functions
2. Video atomizer simplified (330→170 lines)
3. Concept infrastructure created (ConceptAtomizer class, 410 lines)
4. RGBA optimization applied (int32 packing)
5. External ML removed (deleted semantic_extraction.py)
6. Code quality pass (black/isort formatting)

### 🔨 This Document (Tasks 7-9): Entity & Concept Extraction
7. **Text Entity Extraction** - Regex-based NER
8. **Image Color Concepts** - Dominant color detection
9. **Integration** - Link atomizers to ConceptAtomizer

### 📋 Part 2 (Tasks 10-12): Semantic BPE
10. Semantic BPE design specification
11. BPECrystallizer enhancement implementation
12. End-to-end semantic pipeline testing

### 📋 Part 3 (Tasks 13-15): Documentation
13. Architecture document (universal geometric atomization)
14. Semantic BPE deep dive
15. Cross-modal example walkthrough

---

## Part 1 Scope: Tasks 7-9

This part covers entity extraction and concept linking, establishing the foundation for semantic BPE.

---

## Task 7: Text Entity Extraction

**Goal**: Extract entities from text using simple regex patterns (no external NLP libraries)

**Estimated Lines**: ~150 lines  
**Estimated Time**: 45 minutes  
**Files Created**: 1  
**Files Modified**: 1

### 7.1 Design Decisions

**Why Regex Instead of spaCy/NLTK?**
- Aligns with "use existing infrastructure" principle
- No external ML dependencies
- Deterministic and debuggable
- Fast execution
- Easy to extend with domain-specific patterns

**Entity Types to Extract**:
1. **PERSON**: Capitalized names (John Smith, Mary Johnson)
2. **ORGANIZATION**: Companies with Inc/Corp/LLC/Ltd suffixes
3. **LOCATION**: Known city/country names
4. **DATE**: Various date formats (YYYY-MM-DD, MM/DD/YYYY, Month DD, YYYY)
5. **TIME**: Time expressions (HH:MM AM/PM)
6. **MONEY**: Currency amounts ($19.99, €50, £100.50)
7. **EMAIL**: Email addresses
8. **URL**: Web URLs (http/https)
9. **PHONE**: Phone numbers (various formats)
10. **COMMON_ENTITIES**: Domain concepts (CAT, DOG, SKY, GRASS, etc.)

### 7.2 File Creation: entity_extractor.py

**File**: `api/services/text_atomization/entity_extractor.py`

```python
"""
Entity extraction using regex patterns.

NO EXTERNAL NLP LIBRARIES - Pure Python + regex.
Deterministic, fast, debuggable.
"""

import re
from typing import List, Tuple, Dict
from dataclasses import dataclass


@dataclass
class Entity:
    """Extracted entity with metadata."""
    text: str              # "John Smith"
    entity_type: str       # "PERSON"
    start_pos: int         # Character position in text
    end_pos: int           # End position
    confidence: float      # 0.0-1.0 confidence score


class EntityExtractor:
    """
    Regex-based entity extractor.
    
    Design Philosophy:
    - Simple patterns that work 80% of the time
    - Easy to debug and extend
    - No ML model dependencies
    - Fast execution
    """
    
    def __init__(self):
        """Initialize regex patterns."""
        self.patterns = self._compile_patterns()
        self.common_entities = self._load_common_entities()
    
    def _compile_patterns(self) -> Dict[str, re.Pattern]:
        """
        Compile all regex patterns.
        
        Returns:
            Dictionary mapping entity_type -> compiled regex
        """
        return {
            'EMAIL': re.compile(
                r'\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b'
            ),
            
            'URL': re.compile(
                r'https?://[^\s<>"{}|\\^`\[\]]+',
                re.IGNORECASE
            ),
            
            'PHONE': re.compile(
                r'\b(?:\+?1[-.]?)?\(?([0-9]{3})\)?[-.]?([0-9]{3})[-.]?([0-9]{4})\b'
            ),
            
            'MONEY': re.compile(
                r'[$€£¥]\s?\d+(?:,\d{3})*(?:\.\d{2})?'
            ),
            
            'DATE_ISO': re.compile(
                r'\b\d{4}-\d{2}-\d{2}\b'
            ),
            
            'DATE_US': re.compile(
                r'\b\d{1,2}/\d{1,2}/\d{2,4}\b'
            ),
            
            'DATE_WRITTEN': re.compile(
                r'\b(?:January|February|March|April|May|June|July|August|'
                r'September|October|November|December)\s+\d{1,2},?\s+\d{4}\b',
                re.IGNORECASE
            ),
            
            'TIME': re.compile(
                r'\b\d{1,2}:\d{2}(?::\d{2})?\s?(?:AM|PM|am|pm)?\b'
            ),
            
            'PERSON': re.compile(
                # Capitalized First Last (minimum 2 words)
                r'\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b'
            ),
            
            'ORGANIZATION': re.compile(
                r'\b[A-Z][A-Za-z\s&]+(?:Inc|Corp|Corporation|LLC|Ltd|Limited|Company|Co)\b',
                re.IGNORECASE
            ),
            
            # Add more patterns as needed
        }
    
    def _load_common_entities(self) -> Dict[str, str]:
        """
        Load common entity keywords.
        
        These are domain-specific concepts we want to extract.
        Maps: word -> concept_name
        
        Returns:
            Dictionary of common entities
        """
        return {
            # Animals
            'cat': 'CAT',
            'cats': 'CAT',
            'dog': 'DOG',
            'dogs': 'DOG',
            'bird': 'BIRD',
            'birds': 'BIRD',
            'fish': 'FISH',
            
            # Nature
            'sky': 'SKY',
            'cloud': 'CLOUD',
            'clouds': 'CLOUD',
            'tree': 'TREE',
            'trees': 'TREE',
            'grass': 'GRASS',
            'water': 'WATER',
            'ocean': 'OCEAN',
            'mountain': 'MOUNTAIN',
            'mountains': 'MOUNTAIN',
            'sun': 'SUN',
            'moon': 'MOON',
            'star': 'STAR',
            'stars': 'STAR',
            
            # Colors (when used as nouns)
            'red': 'RED',
            'blue': 'BLUE',
            'green': 'GREEN',
            'yellow': 'YELLOW',
            'black': 'BLACK',
            'white': 'WHITE',
            
            # Common objects
            'car': 'CAR',
            'house': 'HOUSE',
            'building': 'BUILDING',
            'road': 'ROAD',
            'street': 'STREET',
            
            # Add more as needed
        }
    
    def extract_entities(self, text: str) -> List[Entity]:
        """
        Extract all entities from text.
        
        Args:
            text: Input text to analyze
            
        Returns:
            List of Entity objects, sorted by position
        """
        entities = []
        
        # Extract using regex patterns
        for entity_type, pattern in self.patterns.items():
            for match in pattern.finditer(text):
                entities.append(Entity(
                    text=match.group(),
                    entity_type=entity_type,
                    start_pos=match.start(),
                    end_pos=match.end(),
                    confidence=0.9  # High confidence for regex matches
                ))
        
        # Extract common entity keywords
        for word, concept in self.common_entities.items():
            # Word boundary regex
            word_pattern = re.compile(r'\b' + re.escape(word) + r'\b', re.IGNORECASE)
            for match in word_pattern.finditer(text):
                entities.append(Entity(
                    text=match.group(),
                    entity_type=concept,
                    start_pos=match.start(),
                    end_pos=match.end(),
                    confidence=0.8  # Slightly lower for keyword matches
                ))
        
        # Remove duplicates (prefer higher confidence)
        entities = self._deduplicate_entities(entities)
        
        # Sort by position
        entities.sort(key=lambda e: e.start_pos)
        
        return entities
    
    def _deduplicate_entities(self, entities: List[Entity]) -> List[Entity]:
        """
        Remove overlapping entities, keeping higher confidence ones.
        
        Args:
            entities: List of potentially overlapping entities
            
        Returns:
            Deduplicated list
        """
        if not entities:
            return []
        
        # Sort by confidence (descending)
        entities.sort(key=lambda e: e.confidence, reverse=True)
        
        kept = []
        for entity in entities:
            # Check if this entity overlaps with any kept entity
            overlaps = False
            for kept_entity in kept:
                if self._entities_overlap(entity, kept_entity):
                    overlaps = True
                    break
            
            if not overlaps:
                kept.append(entity)
        
        return kept
    
    def _entities_overlap(self, e1: Entity, e2: Entity) -> bool:
        """Check if two entities overlap in position."""
        return not (e1.end_pos <= e2.start_pos or e2.end_pos <= e1.start_pos)
    
    def extract_and_group(self, text: str) -> Dict[str, List[Entity]]:
        """
        Extract entities and group by type.
        
        Args:
            text: Input text
            
        Returns:
            Dictionary mapping entity_type -> List[Entity]
        """
        entities = self.extract_entities(text)
        
        grouped = {}
        for entity in entities:
            if entity.entity_type not in grouped:
                grouped[entity.entity_type] = []
            grouped[entity.entity_type].append(entity)
        
        return grouped


# Convenience functions
def extract_entities(text: str) -> List[Entity]:
    """Extract entities from text (convenience function)."""
    extractor = EntityExtractor()
    return extractor.extract_entities(text)


def extract_and_group(text: str) -> Dict[str, List[Entity]]:
    """Extract and group entities by type (convenience function)."""
    extractor = EntityExtractor()
    return extractor.extract_and_group(text)
```

### 7.3 Integration with Text Atomizer

**File Modified**: `api/services/text_atomization/text_atomizer.py`

**Changes Required**:
1. Import EntityExtractor
2. Extract entities during atomization
3. Create concept atoms for each entity type
4. Link text atoms to concept atoms via atom_relation

**Implementation Details**:

```python
# Add to imports
from .entity_extractor import EntityExtractor, Entity

class TextAtomizer:
    def __init__(self):
        # ... existing init ...
        self.entity_extractor = EntityExtractor()
    
    async def atomize_text(
        self,
        conn,
        text: str,
        metadata: dict = None,
        learn_patterns: bool = True,
        extract_entities: bool = True  # NEW PARAMETER
    ) -> int:
        """
        Atomize text with optional entity extraction.
        
        Args:
            conn: Database connection
            text: Text to atomize
            metadata: Optional metadata
            learn_patterns: Whether to apply BPE learning
            extract_entities: Whether to extract and link entities
            
        Returns:
            trajectory_atom_id: ID of text trajectory atom
        """
        # ... existing atomization logic ...
        # (chunks, atoms, trajectory creation)
        
        trajectory_atom_id = # ... result from existing code ...
        
        # NEW: Entity extraction and concept linking
        if extract_entities:
            await self._extract_and_link_entities(
                conn,
                text,
                trajectory_atom_id,
                metadata
            )
        
        return trajectory_atom_id
    
    async def _extract_and_link_entities(
        self,
        conn,
        text: str,
        trajectory_atom_id: int,
        metadata: dict = None
    ):
        """
        Extract entities and link to concept atoms.
        
        Args:
            conn: Database connection
            text: Original text
            trajectory_atom_id: ID of text trajectory
            metadata: Optional metadata
        """
        from api.services.concept_atomization import ConceptAtomizer
        
        # Extract entities
        entities = self.entity_extractor.extract_entities(text)
        
        if not entities:
            return
        
        # Initialize concept atomizer
        concept_atomizer = ConceptAtomizer()
        
        # Process each entity
        for entity in entities:
            # Get or create concept atom for this entity type
            concept_atom_id = await concept_atomizer.get_or_create_concept_atom(
                conn,
                concept_name=entity.entity_type,
                concept_type='entity',
                example_atom_ids=[trajectory_atom_id]
            )
            
            # Link trajectory to concept
            await concept_atomizer.link_to_concept(
                conn,
                source_atom_id=trajectory_atom_id,
                concept_atom_id=concept_atom_id,
                relation_type='mentions',
                strength=entity.confidence,
                metadata={
                    'entity_text': entity.text,
                    'start_pos': entity.start_pos,
                    'end_pos': entity.end_pos,
                    'extracted_at': metadata.get('created_at') if metadata else None
                }
            )
```

### 7.4 Testing Strategy

**Unit Tests**: `tests/services/text_atomization/test_entity_extractor.py`

```python
import pytest
from api.services.text_atomization.entity_extractor import EntityExtractor

def test_extract_email():
    extractor = EntityExtractor()
    text = "Contact me at john.doe@example.com for details"
    entities = extractor.extract_entities(text)
    
    email_entities = [e for e in entities if e.entity_type == 'EMAIL']
    assert len(email_entities) == 1
    assert email_entities[0].text == "john.doe@example.com"

def test_extract_person():
    extractor = EntityExtractor()
    text = "John Smith met Mary Johnson yesterday"
    entities = extractor.extract_entities(text)
    
    person_entities = [e for e in entities if e.entity_type == 'PERSON']
    assert len(person_entities) == 2
    assert person_entities[0].text == "John Smith"
    assert person_entities[1].text == "Mary Johnson"

def test_extract_common_entities():
    extractor = EntityExtractor()
    text = "The cat sat on the mat under the blue sky"
    entities = extractor.extract_entities(text)
    
    cat_entities = [e for e in entities if e.entity_type == 'CAT']
    sky_entities = [e for e in entities if e.entity_type == 'SKY']
    
    assert len(cat_entities) == 1
    assert len(sky_entities) == 1

def test_deduplicate_overlapping():
    extractor = EntityExtractor()
    # This might match both PERSON and ORGANIZATION patterns
    text = "John Smith Inc is a company"
    entities = extractor.extract_entities(text)
    
    # Should not have overlapping entities
    for i, e1 in enumerate(entities):
        for e2 in entities[i+1:]:
            assert not extractor._entities_overlap(e1, e2)
```

### 7.5 Performance Considerations

**Expected Performance**:
- Text length: 1000 words
- Extraction time: <50ms
- Entities found: 10-50 typical

**Optimization Strategies**:
1. Compile regex patterns once (done in __init__)
2. Batch entity linking (multiple entities per DB call)
3. Cache common entity lookups
4. Lazy loading of entity dictionaries

### 7.6 Extension Points

**Easy to Add**:
- More entity types (PRODUCT, EVENT, QUANTITY, etc.)
- Domain-specific entities (medical terms, legal entities, etc.)
- Multi-language patterns
- Custom regex patterns via configuration

**Example Extension**:
```python
# Add to _compile_patterns()
'MEDICAL_TERM': re.compile(
    r'\b(?:aspirin|ibuprofen|acetaminophen|...)\b',
    re.IGNORECASE
),

'LEGAL_ENTITY': re.compile(
    r'\b[A-Z][A-Za-z\s]+(?:v\.|vs\.)\s+[A-Z][A-Za-z\s]+\b'
),
```

---

## Task 8: Image Color Concept Linking

**Goal**: Detect dominant colors in images and link to color concept atoms

**Estimated Lines**: ~120 lines  
**Estimated Time**: 40 minutes  
**Files Created**: 1  
**Files Modified**: 1

### 8.1 Design Decisions

**Why Color Concepts?**
- Colors have semantic meaning (blue = SKY, green = GRASS)
- Simple to detect (no ML required)
- Fast computation (histogram analysis)
- Universal across images
- Foundation for more complex vision concepts

**Detection Strategy**:
1. Analyze pixel color distribution (histogram)
2. Cluster into major color categories
3. Map color ranges to semantic concepts
4. Link image trajectory to concept atoms

**Color Categories** (predefined ranges):
- **SKY**: Blue tones (high B, mid-high G, low-mid R)
- **GRASS**: Green tones (high G, low-mid R/B)
- **FIRE**: Red-orange-yellow tones (high R, mid-low G, low B)
- **WATER**: Blue-cyan tones (high B, high G, low R)
- **SKIN**: Beige/brown tones (high R, mid-high G, mid B)
- **WHITE**: All channels high
- **BLACK**: All channels low
- **GRAY**: All channels mid, approximately equal

### 8.2 File Creation: color_concepts.py

**File**: `api/services/image_atomization/color_concepts.py`

```python
"""
Color concept detection for images.

Maps pixel colors to semantic concepts (SKY, GRASS, etc.)
NO EXTERNAL ML - Pure algorithmic detection.
"""

import numpy as np
from typing import List, Tuple, Dict
from dataclasses import dataclass
from collections import Counter


@dataclass
class ColorConcept:
    """Detected color concept with metadata."""
    concept_name: str      # "SKY"
    percentage: float      # 0.0-1.0 (fraction of image)
    pixel_count: int       # Number of pixels matching
    average_rgb: Tuple[int, int, int]  # Average color of matching pixels
    confidence: float      # 0.0-1.0 confidence


class ColorConceptDetector:
    """
    Detect semantic color concepts in images.
    
    Design Philosophy:
    - Predefined color ranges (deterministic)
    - Fast histogram-based analysis
    - Semantic meaning over precise color matching
    """
    
    def __init__(self, min_percentage: float = 0.05):
        """
        Initialize detector.
        
        Args:
            min_percentage: Minimum % of image to consider a concept present
        """
        self.min_percentage = min_percentage
        self.color_ranges = self._define_color_ranges()
    
    def _define_color_ranges(self) -> Dict[str, Dict[str, Tuple[int, int]]]:
        """
        Define RGB ranges for each color concept.
        
        Returns:
            Dictionary mapping concept_name -> {'r': (min, max), 'g': ..., 'b': ...}
        """
        return {
            'SKY': {
                'r': (80, 200),
                'g': (120, 220),
                'b': (180, 255),
            },
            
            'GRASS': {
                'r': (0, 120),
                'g': (80, 200),
                'b': (0, 120),
            },
            
            'FIRE': {
                'r': (180, 255),
                'g': (50, 180),
                'b': (0, 80),
            },
            
            'WATER': {
                'r': (0, 120),
                'g': (100, 200),
                'b': (150, 255),
            },
            
            'SKIN': {
                'r': (180, 255),
                'g': (120, 220),
                'b': (80, 180),
            },
            
            'WHITE': {
                'r': (200, 255),
                'g': (200, 255),
                'b': (200, 255),
            },
            
            'BLACK': {
                'r': (0, 50),
                'g': (0, 50),
                'b': (0, 50),
            },
            
            'GRAY': {
                # All channels similar and mid-range
                'r': (50, 200),
                'g': (50, 200),
                'b': (50, 200),
                'special': 'gray_check'  # Additional validation needed
            },
        }
    
    def detect_concepts(
        self,
        pixels: np.ndarray
    ) -> List[ColorConcept]:
        """
        Detect color concepts in image pixels.
        
        Args:
            pixels: numpy array of shape (height, width, 3) or (N, 3)
                   RGB values 0-255
                   
        Returns:
            List of ColorConcept objects for detected concepts
        """
        # Flatten to (N, 3) if needed
        if len(pixels.shape) == 3:
            height, width, channels = pixels.shape
            pixels = pixels.reshape(-1, 3)
        
        total_pixels = len(pixels)
        
        concepts = []
        
        for concept_name, ranges in self.color_ranges.items():
            # Find pixels matching this concept
            matching_mask = self._pixels_match_concept(pixels, ranges)
            matching_pixels = pixels[matching_mask]
            
            if len(matching_pixels) == 0:
                continue
            
            percentage = len(matching_pixels) / total_pixels
            
            # Filter by minimum percentage
            if percentage < self.min_percentage:
                continue
            
            # Compute average color
            avg_rgb = tuple(matching_pixels.mean(axis=0).astype(int))
            
            concepts.append(ColorConcept(
                concept_name=concept_name,
                percentage=percentage,
                pixel_count=len(matching_pixels),
                average_rgb=avg_rgb,
                confidence=min(0.95, percentage * 2)  # Higher % = higher confidence
            ))
        
        # Sort by percentage (most dominant first)
        concepts.sort(key=lambda c: c.percentage, reverse=True)
        
        return concepts
    
    def _pixels_match_concept(
        self,
        pixels: np.ndarray,
        ranges: Dict[str, Tuple[int, int]]
    ) -> np.ndarray:
        """
        Check which pixels match a concept's color ranges.
        
        Args:
            pixels: (N, 3) array of RGB values
            ranges: RGB ranges for this concept
            
        Returns:
            Boolean mask of matching pixels
        """
        r, g, b = pixels[:, 0], pixels[:, 1], pixels[:, 2]
        
        # Check each channel is in range
        r_match = (r >= ranges['r'][0]) & (r <= ranges['r'][1])
        g_match = (g >= ranges['g'][0]) & (g <= ranges['g'][1])
        b_match = (b >= ranges['b'][0]) & (b <= ranges['b'][1])
        
        match = r_match & g_match & b_match
        
        # Special case: GRAY requires channels to be similar
        if ranges.get('special') == 'gray_check':
            # RGB values must be within 30 of each other
            rg_diff = np.abs(r - g) < 30
            gb_diff = np.abs(g - b) < 30
            rb_diff = np.abs(r - b) < 30
            match = match & rg_diff & gb_diff & rb_diff
        
        return match
    
    def get_dominant_concept(
        self,
        pixels: np.ndarray
    ) -> ColorConcept:
        """
        Get the single most dominant color concept.
        
        Args:
            pixels: Image pixels
            
        Returns:
            Most dominant ColorConcept, or None if none detected
        """
        concepts = self.detect_concepts(pixels)
        return concepts[0] if concepts else None
    
    def analyze_regions(
        self,
        pixels: np.ndarray,
        grid_size: Tuple[int, int] = (3, 3)
    ) -> Dict[str, List[ColorConcept]]:
        """
        Analyze image in grid regions (e.g., top-third for SKY).
        
        Args:
            pixels: Image pixels (height, width, 3)
            grid_size: (rows, cols) for region grid
            
        Returns:
            Dictionary mapping region_name -> List[ColorConcept]
        """
        height, width, _ = pixels.shape
        rows, cols = grid_size
        
        region_height = height // rows
        region_width = width // cols
        
        regions = {}
        
        for row in range(rows):
            for col in range(cols):
                # Extract region
                r_start = row * region_height
                r_end = (row + 1) * region_height if row < rows - 1 else height
                c_start = col * region_width
                c_end = (col + 1) * region_width if col < cols - 1 else width
                
                region_pixels = pixels[r_start:r_end, c_start:c_end]
                
                # Detect concepts in this region
                concepts = self.detect_concepts(region_pixels)
                
                region_name = f"region_{row}_{col}"
                regions[region_name] = concepts
        
        return regions


# Convenience functions
def detect_color_concepts(pixels: np.ndarray) -> List[ColorConcept]:
    """Detect color concepts (convenience function)."""
    detector = ColorConceptDetector()
    return detector.detect_concepts(pixels)


def get_dominant_color_concept(pixels: np.ndarray) -> ColorConcept:
    """Get dominant color concept (convenience function)."""
    detector = ColorConceptDetector()
    return detector.get_dominant_concept(pixels)
```

### 8.3 Integration with Image Atomizer

**File Modified**: `api/services/image_atomization.py`

**Changes Required**:
1. Import ColorConceptDetector
2. Detect color concepts after pixel atomization
3. Link image trajectory to color concept atoms
4. Store region-based concepts if applicable

**Implementation snippet** (add to `ImageAtomizer.atomize_image`):

```python
# Add to imports
from .color_concepts import ColorConceptDetector

class ImageAtomizer:
    def __init__(self):
        # ... existing init ...
        self.color_detector = ColorConceptDetector(min_percentage=0.10)
    
    async def atomize_image(
        self,
        conn,
        image_path: str,
        metadata: dict = None,
        detect_colors: bool = True  # NEW PARAMETER
    ) -> int:
        """
        Atomize image with optional color concept detection.
        
        Returns:
            trajectory_atom_id: ID of image trajectory atom
        """
        # ... existing pixel atomization logic ...
        
        trajectory_atom_id = # ... result from existing code ...
        
        # NEW: Color concept detection and linking
        if detect_colors:
            await self._detect_and_link_colors(
                conn,
                pixels,  # From existing atomization
                trajectory_atom_id,
                metadata
            )
        
        return trajectory_atom_id
    
    async def _detect_and_link_colors(
        self,
        conn,
        pixels: np.ndarray,
        trajectory_atom_id: int,
        metadata: dict = None
    ):
        """
        Detect color concepts and link to concept atoms.
        
        Args:
            conn: Database connection
            pixels: Image pixels (H, W, 3)
            trajectory_atom_id: ID of image trajectory
            metadata: Optional metadata
        """
        from api.services.concept_atomization import ConceptAtomizer
        
        # Detect color concepts
        concepts = self.color_detector.detect_concepts(pixels)
        
        if not concepts:
            return
        
        # Initialize concept atomizer
        concept_atomizer = ConceptAtomizer()
        
        # Process each color concept
        for color_concept in concepts:
            # Get or create concept atom
            concept_atom_id = await concept_atomizer.get_or_create_concept_atom(
                conn,
                concept_name=color_concept.concept_name,
                concept_type='color',
                example_atom_ids=[trajectory_atom_id]
            )
            
            # Link trajectory to concept
            await concept_atomizer.link_to_concept(
                conn,
                source_atom_id=trajectory_atom_id,
                concept_atom_id=concept_atom_id,
                relation_type='depicts',
                strength=color_concept.confidence,
                metadata={
                    'percentage': color_concept.percentage,
                    'pixel_count': color_concept.pixel_count,
                    'average_rgb': color_concept.average_rgb,
                    'detected_at': metadata.get('created_at') if metadata else None
                }
            )
```

---

**Status**: Part 1 (Tasks 7-8) documented  
**Next**: Task 9 integration, then Part 2 for tasks 10-12
