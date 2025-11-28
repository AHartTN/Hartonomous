"""
Text parser - handles text with character/word-level atomization.
"""

from pathlib import Path
from typing import Dict, Any

from ...core.atomization import BaseAtomizer, ModalityType


class TextParser(BaseAtomizer):
    """Parse and atomize text."""
    
    async def parse(self, text_path: Path, conn) -> int:
        """Parse text file into atoms. Returns parent atom_id."""
        import hashlib
        
        with open(text_path, 'r', encoding='utf-8') as f:
            text = f.read()
        
        text_hash = hashlib.sha256(text.encode()).digest()
        
        parent_atom_id = await self.create_atom(
            conn,
            text_hash,
            str(text_path),
            {
                'modality': 'text',
                'file_path': str(text_path)
            }
        )
        
        # TODO: Atomize characters/words via SQL atomize_text()
        
        return parent_atom_id
