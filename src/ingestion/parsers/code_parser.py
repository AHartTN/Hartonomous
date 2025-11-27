"""
Code parser - handles source code via TreeSitter AST parsing.
Integrates with C# CodeAtomizer API for advanced analysis.
"""

import numpy as np
from typing import Dict, Any, Iterator, List, Optional
from pathlib import Path
import json

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark_projection import LandmarkProjector


class CodeParser:
    """Parse and atomize source code."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_languages = [
            'python', 'javascript', 'typescript', 'java', 'cpp', 'c', 'csharp',
            'go', 'rust', 'ruby', 'php', 'swift', 'kotlin', 'scala'
        ]
    
    def _detect_language(self, file_path: Path) -> str:
        """Detect programming language from file extension."""
        ext_map = {
            '.py': 'python',
            '.js': 'javascript',
            '.ts': 'typescript',
            '.java': 'java',
            '.cpp': 'cpp',
            '.cc': 'cpp',
            '.c': 'c',
            '.h': 'c',
            '.hpp': 'cpp',
            '.cs': 'csharp',
            '.go': 'go',
            '.rs': 'rust',
            '.rb': 'ruby',
            '.php': 'php',
            '.swift': 'swift',
            '.kt': 'kotlin',
            '.scala': 'scala'
        }
        return ext_map.get(file_path.suffix.lower(), 'unknown')
    
    def _parse_with_treesitter(self, code: str, language: str) -> Optional[Dict[str, Any]]:
        """Parse code using tree-sitter."""
        try:
            from tree_sitter import Language, Parser
            import tree_sitter_python
            import tree_sitter_javascript
            
            # Language mapping
            lang_parsers = {
                'python': tree_sitter_python,
                'javascript': tree_sitter_javascript,
                'typescript': tree_sitter_javascript,
            }
            
            if language not in lang_parsers:
                return None
            
            parser = Parser()
            parser.set_language(Language(lang_parsers[language].language()))
            
            tree = parser.parse(bytes(code, 'utf8'))
            
            # Extract AST structure
            def traverse(node, depth=0):
                node_info = {
                    'type': node.type,
                    'start': node.start_point,
                    'end': node.end_point,
                    'text': code[node.start_byte:node.end_byte][:100],  # Preview
                    'depth': depth,
                    'children': []
                }
                
                for child in node.children:
                    node_info['children'].append(traverse(child, depth + 1))
                
                return node_info
            
            return traverse(tree.root_node)
        
        except ImportError:
            return None
    
    def _call_csharp_atomizer(self, code: str, file_path: Path, language: str) -> Optional[List[Dict]]:
        """
        Call C# CodeAtomizer API for advanced analysis.
        This is optional/future integration with the C# AST service.
        """
        # TODO: Implement HTTP call to C# API
        # This would be used for enterprise features
        return None
    
    def parse(self, file_path: Path, use_csharp_api: bool = False) -> Iterator[Dict[str, Any]]:
        """Parse source code file into atoms."""
        # Read code
        with open(file_path, 'r', encoding='utf-8') as f:
            code = f.read()
        
        language = self._detect_language(file_path)
        
        # Parse with tree-sitter
        ast_data = self._parse_with_treesitter(code, language)
        
        # Optionally use C# API for deeper analysis
        if use_csharp_api:
            csharp_data = self._call_csharp_atomizer(code, file_path, language)
        else:
            csharp_data = None
        
        # Tokenize code into chunks
        lines = code.split('\n')
        chunk_size = 50  # lines per chunk
        
        for chunk_idx in range(0, len(lines), chunk_size):
            chunk_lines = lines[chunk_idx:chunk_idx + chunk_size]
            chunk_text = '\n'.join(chunk_lines)
            
            # Create embedding from code chunk
            # For now, use simple byte encoding; later integrate with code embedding models
            code_bytes = chunk_text.encode('utf-8')
            
            # Create a simple numeric representation
            # In production, use CodeBERT or similar
            code_vector = np.frombuffer(code_bytes[:768], dtype=np.uint8).astype(np.float64) / 255.0
            if len(code_vector) < 768:
                code_vector = np.pad(code_vector, (0, 768 - len(code_vector)))
            
            # Atomize
            code_atoms = self.atomizer.atomize_array(code_vector, ModalityType.CODE_TOKEN)
            landmarks = self.landmark_projector.extract_code_landmarks(
                code_vector,
                language,
                ast_data
            )
            
            for atom in code_atoms:
                for landmark in landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'file_path': str(file_path),
                        'language': language,
                        'chunk_index': chunk_idx // chunk_size,
                        'line_range': (chunk_idx, min(chunk_idx + chunk_size, len(lines))),
                        'ast_node': ast_data.get('type') if ast_data else None
                    }
    
    def parse_raw_code(self, code: str, language: str, source_id: str) -> Iterator[Dict[str, Any]]:
        """Parse raw code string directly."""
        lines = code.split('\n')
        chunk_size = 50
        
        for chunk_idx in range(0, len(lines), chunk_size):
            chunk_lines = lines[chunk_idx:chunk_idx + chunk_size]
            chunk_text = '\n'.join(chunk_lines)
            
            code_bytes = chunk_text.encode('utf-8')
            code_vector = np.frombuffer(code_bytes[:768], dtype=np.uint8).astype(np.float64) / 255.0
            if len(code_vector) < 768:
                code_vector = np.pad(code_vector, (0, 768 - len(code_vector)))
            
            code_atoms = self.atomizer.atomize_array(code_vector, ModalityType.CODE_TOKEN)
            landmarks = self.landmark_projector.extract_code_landmarks(code_vector, language, None)
            
            for atom in code_atoms:
                for landmark in landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'source_id': source_id,
                        'language': language,
                        'chunk_index': chunk_idx // chunk_size
                    }
