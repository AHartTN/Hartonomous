"""
Document ingestion routes.
POST /v1/ingest/document - Atomize documents (PDF, DOCX, MD, HTML, TXT)
"""

import logging
from pathlib import Path
from typing import Optional

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.models.ingest import IngestResponse
from api.services.document_parser import DocumentParserService

router = APIRouter()
logger = logging.getLogger(__name__)


@router.post(
    "/document",
    response_model=IngestResponse,
    summary="Atomize document (PDF/DOCX/MD/HTML/TXT)",
)
async def ingest_document(
    file: UploadFile = File(...),
    format: Optional[str] = Form(None),
    extract_images: bool = Form(True),
    ocr_enabled: bool = Form(False),
    conn: AsyncConnection = Depends(get_db_connection),
):
    """
    Atomize document into extreme granular atoms.

    Supported formats: PDF, DOCX, MD (Markdown), HTML, TXT
    Auto-detects format from file extension if not provided.
    """
    import time

    start_time = time.time()

    try:
        # Read file
        file_data = await file.read()

        # Detect format
        if not format:
            format = Path(file.filename).suffix.lower().lstrip(".")

        format = format.lower()

        # Route to appropriate parser
        if format == "pdf":
            result = await DocumentParserService.parse_and_atomize_pdf(
                conn=conn,
                file_data=file_data,
                metadata={"filename": file.filename},
                extract_images=extract_images,
                ocr_enabled=ocr_enabled,
            )
        elif format in ["docx", "doc"]:
            result = await DocumentParserService.parse_and_atomize_docx(
                conn=conn, file_data=file_data, metadata={"filename": file.filename}
            )
        elif format in ["md", "markdown"]:
            text = file_data.decode("utf-8")
            result = await DocumentParserService.parse_and_atomize_markdown(
                conn=conn, text=text, metadata={"filename": file.filename}
            )
        else:
            raise HTTPException(
                status_code=400,
                detail=f"Unsupported format: {format}. Supported: pdf, docx, md",
            )

        processing_time = (time.time() - start_time) * 1000

        return IngestResponse(
            atom_count=result["atom_count"],
            root_atom_id=result["root_atom_id"],
            processing_time_ms=processing_time,
            message=f"Document atomized: {result['atom_count']} atoms",
        )

    except Exception as e:
        logger.error(f"Document ingestion failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


__all__ = ["router"]
