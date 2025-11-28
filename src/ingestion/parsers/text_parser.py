"""Text parser - handles text with character/word-level atomization."""

import hashlib
from pathlib import Path
from typing import Dict, Any

from ...core.atomization import BaseAtomizer


class TextParser(BaseAtomizer):
    """Parse and atomize text via SQL functions."""
    
    async def parse(self, text_path: Path, conn) -> int:
        """
        Parse text file into atoms.
        
        Process:
        1. Read text file
        2. Create parent atom for document
        3. Call SQL atomize_text() for character-level decomposition
        4. Link via composition
        
        Returns parent atom_id.
        """
        with open(text_path, 'r', encoding='utf-8') as f:
            text = f.read()
        
        # Create parent atom for document
        text_hash = hashlib.sha256(text.encode('utf-8')).digest()
        parent_atom_id = await self.create_atom(
            conn,
            text_hash,
            str(text_path.name),
            {
                'modality': 'text',
                'char_count': len(text),
                'word_count': len(text.split()),
                'file_path': str(text_path)
            }
        )
        
        # Use SQL atomize_text() function for character decomposition
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atomize_text(%s)",
                (text,)
            )
            char_atom_ids = (await cur.fetchone())[0]
        
        # Link character atoms to document
        for idx, char_atom_id in enumerate(char_atom_ids):
            await self.create_composition(conn, parent_atom_id, char_atom_id, idx)
            self.stats["atoms_created"] += 1
        
        self.stats["total_processed"] = len(text)
        
        return parent_atom_id
