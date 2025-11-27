"""
Code ingestion routes - delegates to C# microservice for AST atomization.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Optional

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile
from psycopg import AsyncConnection
from pydantic import BaseModel, Field

from api.dependencies import get_db_connection
from api.services.code_atomization import CodeAtomizationService

logger = logging.getLogger(__name__)
router = APIRouter()


class CodeIngestRequest(BaseModel):
    """Request model for code ingestion."""

    code: str = Field(..., description="Source code to atomize")
    filename: str = Field(
        default="code.txt", description="Filename (determines language)"
    )
    language: str = Field(default="csharp", description="Programming language")
    metadata: Optional[dict] = Field(
        default=None, description="Optional metadata (JSON)"
    )


class CodeIngestResponse(BaseModel):
    """Response model for code ingestion."""

    success: bool
    total_atoms: int
    unique_atoms: int
    compositions: int
    relations: int
    message: str


@router.post("/code", response_model=CodeIngestResponse)
async def ingest_code(
    request: CodeIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
):
    """
    Ingest source code via Roslyn/Tree-sitter microservice.

    Delegates AST parsing to C# microservice, then bulk inserts atoms/compositions/relations.

    **Supported Languages:**
    - C# (Roslyn semantic analysis)
    - Python (Tree-sitter) [Coming soon]
    - JavaScript/TypeScript (Tree-sitter) [Coming soon]
    """
    try:
        service = CodeAtomizationService()

        result = await service.atomize_and_insert(
            conn=conn,
            code=request.code,
            filename=request.filename,
            language=request.language,
            metadata=request.metadata,
        )

        return CodeIngestResponse(
            success=True,
            total_atoms=result["total_atoms"],
            unique_atoms=result["unique_atoms"],
            compositions=result["compositions"],
            relations=result["relations"],
            message=f"Successfully atomized {request.filename} ({len(request.code)} bytes)",
        )

    except Exception as e:
        logger.error(f"Code ingestion failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/code/file", response_model=CodeIngestResponse)
async def ingest_code_file(
    file: UploadFile = File(..., description="Code file to atomize"),
    language: str = Form(default="csharp", description="Programming language"),
    conn: AsyncConnection = Depends(get_db_connection),
):
    """
    Ingest code file via Roslyn/Tree-sitter microservice.

    Upload a code file (.cs, .py, .js, etc.) for deep AST atomization.
    """
    try:
        # Read file content
        content = await file.read()
        code = content.decode("utf-8")

        service = CodeAtomizationService()

        result = await service.atomize_and_insert(
            conn=conn,
            code=code,
            filename=file.filename or "code.txt",
            language=language,
            metadata=None,
        )

        return CodeIngestResponse(
            success=True,
            total_atoms=result["total_atoms"],
            unique_atoms=result["unique_atoms"],
            compositions=result["compositions"],
            relations=result["relations"],
            message=f"Successfully atomized {file.filename} ({len(code)} bytes)",
        )

    except UnicodeDecodeError:
        raise HTTPException(status_code=400, detail="File must be UTF-8 encoded text")
    except Exception as e:
        logger.error(f"Code file ingestion failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/code/health")
async def check_code_atomizer_health():
    """
    Check if code atomizer microservice is healthy.
    """
    from api.services.code_atomization import CodeAtomizerClient

    client = CodeAtomizerClient()
    try:
        is_healthy = await client.health_check()
        return {
            "service": "code-atomizer",
            "status": "healthy" if is_healthy else "unhealthy",
        }
    finally:
        await client.close()
