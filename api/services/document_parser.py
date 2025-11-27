"""
Document Parser Service
Atomizes PDF, DOCX, Markdown, HTML, TXT into extreme granular atoms.

Hierarchical Pattern:
  document → sections/pages → paragraphs → sentences → words → characters

Every level becomes an atom with metadata and hierarchical composition.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import io
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from psycopg import AsyncConnection
from psycopg.types.json import Json

logger = logging.getLogger(__name__)


class DocumentParserService:
    """Service for parsing and atomizing documents."""

    @staticmethod
    async def parse_and_atomize_pdf(
        conn: AsyncConnection,
        file_path: Optional[Path] = None,
        file_data: Optional[bytes] = None,
        metadata: Optional[Dict[str, Any]] = None,
        extract_images: bool = True,
        ocr_enabled: bool = False,
    ) -> Dict[str, Any]:
        """
        Parse and atomize PDF document.
        
        Args:
            conn: Database connection
            file_path: Path to PDF file (or file_data)
            file_data: PDF file bytes
            metadata: Additional metadata
            extract_images: Extract and atomize embedded images
            ocr_enabled: Use OCR for scanned PDFs
            
        Returns:
            dict with atom counts, root_atom_id, page_ids
        """
        try:
            import pdfplumber
            
            if metadata is None:
                metadata = {}
            
            # Open PDF
            if file_data:
                pdf_file = pdfplumber.open(io.BytesIO(file_data))
            elif file_path:
                pdf_file = pdfplumber.open(file_path)
            else:
                raise ValueError("Either file_path or file_data must be provided")
            
            with pdf_file as pdf:
                # Extract PDF metadata
                pdf_metadata = pdf.metadata or {}
                metadata.update({
                    "modality": "document",
                    "format": "pdf",
                    "total_pages": len(pdf.pages),
                    "pdf_metadata": pdf_metadata
                })
                
                # Create document root atom
                doc_title = pdf_metadata.get('Title', file_path.name if file_path else 'document')
                
                async with conn.cursor() as cur:
                    # Create document atom
                    await cur.execute(
                        """
                        SELECT atomize_value(
                            digest(%s, 'sha256'),
                            %s,
                            %s::jsonb
                        )
                        """,
                        (doc_title.encode('utf-8'), doc_title, Json(metadata))
                    )
                    doc_atom_id = (await cur.fetchone())[0]
                    
                    page_ids = []
                    total_atoms = 1  # Document atom
                    
                    # Process each page
                    for page_num, page in enumerate(pdf.pages, start=1):
                        # Extract text
                        text = page.extract_text() or ""
                        
                        # OCR if needed and enabled
                        if ocr_enabled and not text.strip():
                            text = await DocumentParserService._ocr_page(page)
                        
                        # Create page atom
                        page_metadata = {
                            "modality": "page",
                            "page_number": page_num,
                            "width": page.width,
                            "height": page.height
                        }
                        
                        await cur.execute(
                            """
                            SELECT atomize_value(
                                digest(%s, 'sha256'),
                                %s,
                                %s::jsonb
                            )
                            """,
                            (f"page_{page_num}".encode('utf-8'), 
                             f"Page {page_num}", 
                             Json(page_metadata))
                        )
                        page_atom_id = (await cur.fetchone())[0]
                        page_ids.append(page_atom_id)
                        total_atoms += 1
                        
                        # Link page to document
                        await cur.execute(
                            """
                            INSERT INTO atom_composition 
                                (parent_atom_id, component_atom_id, sequence_index)
                            VALUES (%s, %s, %s)
                            """,
                            (doc_atom_id, page_atom_id, page_num - 1)
                        )
                        
                        # Atomize page text
                        if text.strip():
                            await cur.execute(
                                "SELECT atomize_text(%s, %s::jsonb)",
                                (text, Json({"page": page_num}))
                            )
                            char_atoms = (await cur.fetchone())[0]
                            total_atoms += len(char_atoms)
                            
                            # Link text atoms to page
                            # TODO: Consider paragraph/sentence level composition
                            # For now, link all characters to page
                            for idx, char_atom_id in enumerate(char_atoms):
                                await cur.execute(
                                    """
                                    INSERT INTO atom_composition 
                                        (parent_atom_id, component_atom_id, sequence_index)
                                    VALUES (%s, %s, %s)
                                    ON CONFLICT DO NOTHING
                                    """,
                                    (page_atom_id, char_atom_id, idx)
                                )
                        
                        # Extract and atomize images
                        if extract_images and page.images:
                            for img_idx, img in enumerate(page.images):
                                # TODO: Implement image extraction and atomization
                                logger.info(f"Found image on page {page_num}: {img}")
                                # image_atom_id = await atomize_image(...)
                                # total_atoms += image_pixel_count
                    
                    logger.info(
                        f"PDF atomization complete: {len(page_ids)} pages, "
                        f"{total_atoms} total atoms"
                    )
                    
                    return {
                        "atom_count": total_atoms,
                        "root_atom_id": doc_atom_id,
                        "page_ids": page_ids,
                        "total_pages": len(pdf.pages)
                    }
        
        except Exception as e:
            logger.error(f"PDF parsing failed: {e}", exc_info=True)
            raise

    @staticmethod
    async def parse_and_atomize_docx(
        conn: AsyncConnection,
        file_path: Optional[Path] = None,
        file_data: Optional[bytes] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Parse and atomize DOCX document.
        
        Structure: document → sections → paragraphs → runs → words → characters
        """
        try:
            from docx import Document
            
            if metadata is None:
                metadata = {}
            
            # Open DOCX
            if file_data:
                doc = Document(io.BytesIO(file_data))
            elif file_path:
                doc = Document(file_path)
            else:
                raise ValueError("Either file_path or file_data must be provided")
            
            # Extract metadata
            props = doc.core_properties
            metadata.update({
                "modality": "document",
                "format": "docx",
                "title": props.title or (file_path.name if file_path else "document"),
                "author": props.author,
                "created": str(props.created) if props.created else None
            })
            
            async with conn.cursor() as cur:
                # Create document atom
                doc_title = props.title or (file_path.name if file_path else "document")
                
                await cur.execute(
                    """
                    SELECT atomize_value(
                        digest(%s, 'sha256'),
                        %s,
                        %s::jsonb
                    )
                    """,
                    (doc_title.encode('utf-8'), doc_title, Json(metadata))
                )
                doc_atom_id = (await cur.fetchone())[0]
                
                total_atoms = 1
                para_ids = []
                
                # Process paragraphs
                for para_idx, para in enumerate(doc.paragraphs):
                    if not para.text.strip():
                        continue
                    
                    # Create paragraph atom
                    para_metadata = {
                        "modality": "paragraph",
                        "style": para.style.name,
                        "alignment": str(para.alignment) if para.alignment else None
                    }
                    
                    await cur.execute(
                        """
                        SELECT atomize_value(
                            digest(%s, 'sha256'),
                            %s,
                            %s::jsonb
                        )
                        """,
                        (para.text[:64].encode('utf-8'), 
                         para.text[:100], 
                         Json(para_metadata))
                    )
                    para_atom_id = (await cur.fetchone())[0]
                    para_ids.append(para_atom_id)
                    total_atoms += 1
                    
                    # Link paragraph to document
                    await cur.execute(
                        """
                        INSERT INTO atom_composition 
                            (parent_atom_id, component_atom_id, sequence_index)
                        VALUES (%s, %s, %s)
                        """,
                        (doc_atom_id, para_atom_id, para_idx)
                    )
                    
                    # Atomize paragraph text
                    await cur.execute(
                        "SELECT atomize_text(%s, %s::jsonb)",
                        (para.text, Json({"paragraph": para_idx}))
                    )
                    char_atoms = (await cur.fetchone())[0]
                    total_atoms += len(char_atoms)
                    
                    # Link text atoms to paragraph
                    for idx, char_atom_id in enumerate(char_atoms):
                        await cur.execute(
                            """
                            INSERT INTO atom_composition 
                                (parent_atom_id, component_atom_id, sequence_index)
                            VALUES (%s, %s, %s)
                            ON CONFLICT DO NOTHING
                            """,
                            (para_atom_id, char_atom_id, idx)
                        )
                
                # Process tables
                for table_idx, table in enumerate(doc.tables):
                    # TODO: Implement table atomization
                    # Each cell becomes an atom with row/col metadata
                    logger.info(f"Found table with {len(table.rows)} rows")
                
                logger.info(
                    f"DOCX atomization complete: {len(para_ids)} paragraphs, "
                    f"{total_atoms} total atoms"
                )
                
                return {
                    "atom_count": total_atoms,
                    "root_atom_id": doc_atom_id,
                    "paragraph_ids": para_ids
                }
        
        except Exception as e:
            logger.error(f"DOCX parsing failed: {e}", exc_info=True)
            raise

    @staticmethod
    async def parse_and_atomize_markdown(
        conn: AsyncConnection,
        text: str,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Parse and atomize Markdown document.
        
        Structure: document → sections (by headers) → blocks → text
        """
        try:
            from markdown_it import MarkdownIt
            
            if metadata is None:
                metadata = {}
            
            md = MarkdownIt()
            tokens = md.parse(text)
            
            metadata.update({
                "modality": "document",
                "format": "markdown"
            })
            
            async with conn.cursor() as cur:
                # Create document atom
                title = "Markdown Document"  # TODO: Extract from first h1
                
                await cur.execute(
                    """
                    SELECT atomize_value(
                        digest(%s, 'sha256'),
                        %s,
                        %s::jsonb
                    )
                    """,
                    (title.encode('utf-8'), title, Json(metadata))
                )
                doc_atom_id = (await cur.fetchone())[0]
                
                total_atoms = 1
                
                # Process markdown tokens
                for token in tokens:
                    if token.type == 'heading_open':
                        # Create section atom for heading
                        pass
                    elif token.type == 'paragraph_open':
                        # Create paragraph atom
                        pass
                    elif token.type == 'code_block' or token.type == 'fence':
                        # Create code block atom (special handling)
                        code = token.content
                        # TODO: Optionally send to C# code atomizer
                        pass
                    elif token.type == 'inline':
                        # Atomize inline text
                        if token.content:
                            await cur.execute(
                                "SELECT atomize_text(%s, %s::jsonb)",
                                (token.content, Json(metadata))
                            )
                            char_atoms = (await cur.fetchone())[0]
                            total_atoms += len(char_atoms)
                
                return {
                    "atom_count": total_atoms,
                    "root_atom_id": doc_atom_id
                }
        
        except Exception as e:
            logger.error(f"Markdown parsing failed: {e}", exc_info=True)
            raise

    @staticmethod
    async def _ocr_page(page) -> str:
        """
        Perform OCR on PDF page using Tesseract.
        """
        try:
            import pytesseract
            from PIL import Image
            
            # Convert page to image
            img = page.to_image(resolution=300)
            pil_img = img.original
            
            # Perform OCR
            text = pytesseract.image_to_string(pil_img, lang='eng')
            
            logger.info(f"OCR extracted {len(text)} characters")
            return text
        
        except Exception as e:
            logger.error(f"OCR failed: {e}")
            return ""


__all__ = ["DocumentParserService"]
