"""
Code parser - handles code with AST-level atomization."""

import hashlib
from pathlib import Path
from typing import Dict, Any
import httpx

from ...core.atomization import BaseAtomizer


class CodeParser(BaseAtomizer):
    """Parse and atomize source code via C# atomizer service."""
    
    def __init__(self, atomizer_service_url: str = "http://localhost:5000"):
        super().__init__()
        self.service_url = atomizer_service_url
    
    async def parse(self, code_path: Path, conn) -> int:
        """
        Parse code file into atoms via C# CodeAtomizer service.
        
        Process:
        1. Read code file
        2. Call C# service for AST decomposition
        3. Create parent atom for file
        4. Atomize each AST node via SQL
        5. Build composition from AST structure
        
        Returns parent atom_id.
        """
        with open(code_path, 'r', encoding='utf-8') as f:
            code = f.read()
        
        language = self._detect_language(code_path.suffix)
        
        # Create parent atom for code file
        code_hash = hashlib.sha256(code.encode('utf-8')).digest()
        parent_atom_id = await self.create_atom(
            conn,
            code_hash,
            str(code_path.name),
            {
                'modality': 'code',
                'language': language,
                'file_path': str(code_path),
                'line_count': len(code.split('\n'))
            }
        )
        
        # Call C# atomizer service
        try:
            async with httpx.AsyncClient(timeout=30.0) as client:
                response = await client.post(
                    f"{self.service_url}/api/v1/atomize/{language}",
                    json={"code": code, "fileName": str(code_path.name)}
                )
                response.raise_for_status()
                result = response.json()
        except Exception as e:
            # Fallback: simple character-level atomization
            for idx, char in enumerate(code):
                char_bytes = char.encode('utf-8')
                char_atom_id = await self.create_atom(conn, char_bytes, char, {'modality': 'character'})
                await self.create_composition(conn, parent_atom_id, char_atom_id, idx)
            
            self.stats["total_processed"] = len(code)
            return parent_atom_id
        
        # Process AST atoms from service
        for atom in result.get("atoms", []):
            atom_bytes = bytes.fromhex(atom["contentHash"])
            atom_id = await self.create_atom(
                conn,
                atom_bytes,
                atom.get("canonicalText"),
                atom.get("metadata", {})
            )
            
            self.stats["atoms_created"] += 1
        
        # Process compositions
        for comp in result.get("compositions", []):
            parent_hash = bytes.fromhex(comp["parentHash"])
            component_hash = bytes.fromhex(comp["componentHash"])
            
            # TODO: Map hashes to atom_ids and create compositions
            
            self.stats["total_processed"] += 1
        
        return parent_atom_id
    
    def _detect_language(self, extension: str) -> str:
        """Detect programming language from file extension."""
        ext_map = {
            '.py': 'python',
            '.cs': 'csharp',
            '.js': 'javascript',
            '.ts': 'typescript',
            '.java': 'java',
            '.cpp': 'cpp',
            '.c': 'c',
            '.go': 'go',
            '.rs': 'rust',
            '.rb': 'ruby',
            '.php': 'php',
        }
        return ext_map.get(extension.lower(), 'text')
