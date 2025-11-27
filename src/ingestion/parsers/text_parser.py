"""
Text parser - handles documents, plain text, PDFs, structured text.
Extracts text, embeddings, and semantic structure.
"""

import numpy as np
from typing import Dict, Any, Iterator, Optional, List
from pathlib import Path
import re

from ...core.atomization import Atomizer, ModalityType
from ...core.landmark_projection import LandmarkProjector


class TextParser:
    """Parse and atomize text documents."""
    
    def __init__(self):
        self.atomizer = Atomizer()
        self.landmark_projector = LandmarkProjector()
        self.supported_formats = ['.txt', '.md', '.pdf', '.doc', '.docx', '.html', '.json', '.xml']
    
    def _chunk_text(self, text: str, chunk_size: int = 512) -> List[str]:
        """Split text into semantic chunks."""
        # Split on paragraph boundaries first
        paragraphs = text.split('\n\n')
        
        chunks = []
        current_chunk = []
        current_len = 0
        
        for para in paragraphs:
            para_len = len(para)
            if current_len + para_len > chunk_size and current_chunk:
                chunks.append('\n\n'.join(current_chunk))
                current_chunk = [para]
                current_len = para_len
            else:
                current_chunk.append(para)
                current_len += para_len
        
        if current_chunk:
            chunks.append('\n\n'.join(current_chunk))
        
        return chunks
    
    def _extract_text_from_file(self, file_path: Path) -> str:
        """Extract text from various file formats."""
        suffix = file_path.suffix.lower()
        
        if suffix == '.txt' or suffix == '.md':
            with open(file_path, 'r', encoding='utf-8') as f:
                return f.read()
        
        elif suffix == '.pdf':
            try:
                import PyPDF2
                with open(file_path, 'rb') as f:
                    reader = PyPDF2.PdfReader(f)
                    text = []
                    for page in reader.pages:
                        text.append(page.extract_text())
                    return '\n\n'.join(text)
            except ImportError:
                raise ImportError("PyPDF2 required for PDF parsing: pip install PyPDF2")
        
        elif suffix in ['.html', '.htm']:
            try:
                from bs4 import BeautifulSoup
                with open(file_path, 'r', encoding='utf-8') as f:
                    soup = BeautifulSoup(f.read(), 'html.parser')
                    return soup.get_text()
            except ImportError:
                raise ImportError("beautifulsoup4 required for HTML parsing: pip install beautifulsoup4")
        
        elif suffix == '.json':
            import json
            with open(file_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
                return json.dumps(data, indent=2)
        
        elif suffix == '.xml':
            with open(file_path, 'r', encoding='utf-8') as f:
                return f.read()
        
        else:
            raise ValueError(f"Unsupported text format: {suffix}")
    
    def _generate_embeddings(self, text_chunks: List[str]) -> np.ndarray:
        """
        Generate embeddings for text chunks.
        Uses sentence-transformers or similar.
        """
        try:
            from sentence_transformers import SentenceTransformer
            model = SentenceTransformer('all-MiniLM-L6-v2')
            embeddings = model.encode(text_chunks, convert_to_numpy=True)
            return embeddings.astype(np.float64)
        except ImportError:
            # Fallback: simple word frequency vectors
            from sklearn.feature_extraction.text import TfidfVectorizer
            vectorizer = TfidfVectorizer(max_features=384)
            vectors = vectorizer.fit_transform(text_chunks).toarray()
            return vectors.astype(np.float64)
    
    def parse(self, file_path: Path, generate_embeddings: bool = True) -> Iterator[Dict[str, Any]]:
        """Parse text file into atoms."""
        # Extract text
        text = self._extract_text_from_file(file_path)
        
        # Chunk text
        chunks = self._chunk_text(text)
        
        # Generate embeddings if requested
        if generate_embeddings:
            embeddings = self._generate_embeddings(chunks)
        else:
            embeddings = None
        
        # Process each chunk
        for idx, chunk in enumerate(chunks):
            # Encode text as bytes
            text_bytes = chunk.encode('utf-8')
            
            # Create embedding array
            if embeddings is not None:
                embedding = embeddings[idx]
                embedding_atoms = self.atomizer.atomize_array(embedding, ModalityType.TEXT_EMBEDDING)
                landmarks = self.landmark_projector.extract_embedding_landmarks(embedding)
            else:
                embedding_atoms = []
                landmarks = []
            
            # Yield record
            for atom in embedding_atoms:
                for landmark in landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'text_chunk': chunk[:100],  # Preview
                        'chunk_index': idx,
                        'file_path': str(file_path),
                        'chunk_length': len(chunk)
                    }
    
    def parse_raw_text(self, text: str, source_id: str) -> Iterator[Dict[str, Any]]:
        """Parse raw text string directly."""
        chunks = self._chunk_text(text)
        embeddings = self._generate_embeddings(chunks)
        
        for idx, chunk in enumerate(chunks):
            embedding = embeddings[idx]
            embedding_atoms = self.atomizer.atomize_array(embedding, ModalityType.TEXT_EMBEDDING)
            landmarks = self.landmark_projector.extract_embedding_landmarks(embedding)
            
            for atom in embedding_atoms:
                for landmark in landmarks:
                    yield {
                        'atom': atom,
                        'landmark': landmark,
                        'text_chunk': chunk[:100],
                        'chunk_index': idx,
                        'source_id': source_id
                    }
