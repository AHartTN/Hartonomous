"""
Code parser - handles code with AST-level atomization."""

from pathlib import Path
from typing import Dict, Any

from ...core.atomization import BaseAtomizer, ModalityType


class CodeParser(BaseAtomizer):
    """Parse and atomize source code."""
    
    async def parse(self, code_path: Path, conn) -> int:
        """Parse code file into atoms via C# atomizer service. Returns parent atom_id."""
        import hashlib
        
        with open(code_path, 'r', encoding='utf-8') as f:
            code = f.read()
        
        code_hash = hashlib.sha256(code.encode()).digest()
        
        parent_atom_id = await self.create_atom(
            conn,
            code_hash,
            str(code_path),
            {
                'modality': 'code',
                'file_path': str(code_path),
                'language': code_path.suffix[1:]
            }
        )
        
        # TODO: Call C# code atomizer service for AST decomposition
        
        return parent_atom_id
