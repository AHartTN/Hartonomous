"""
Ingest routes for atomizing content.

POST /v1/ingest/text   - Atomize text
POST /v1/ingest/image  - Atomize image
POST /v1/ingest/audio  - Atomize audio

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import base64
import logging
import time

from fastapi import APIRouter, Depends, HTTPException, status
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.models.ingest import (
    AudioIngestRequest,
    ErrorResponse,
    ImageIngestRequest,
    IngestResponse,
    TextIngestRequest,
)
from api.services.general_atomization import AtomizationService

router = APIRouter()
logger = logging.getLogger(__name__)


@router.post(
    "/text",
    response_model=IngestResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid input"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Atomize text content",
    description=(
        "Atomize text into character-level atoms.\n\n"
        "Each character becomes a content-addressable atom with:\n"
        "- SHA-256 content hash\n"
        "- Spatial position in semantic space\n"
        "- Hierarchical composition (characters ? words ? sentences)\n\n"
        "Performance: ~1000 characters/second"
    ),
)
async def ingest_text(
    request: TextIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
) -> IngestResponse:
    """
    Atomize text content.

    Example:
        ```json
        {
            "text": "Hello, World!",
            "metadata": {
                "source": "api",
                "author": "user123"
            }
        }
        ```

    Returns:
        IngestResponse with atom_count and root_atom_id
    """
    start_time = time.time()

    try:
        # Atomize text
        result = await AtomizationService.atomize_text(
            conn=conn, text=request.text, metadata=request.metadata
        )

        processing_time = (time.time() - start_time) * 1000

        logger.info(
            f"Text ingestion complete: {result['atom_count']} atoms "
            f"in {processing_time:.2f}ms"
        )

        return IngestResponse(
            atom_count=result["atom_count"],
            root_atom_id=result["root_atom_id"],
            processing_time_ms=processing_time,
            message=f"Successfully atomized {result['atom_count']} characters",
        )

    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    except Exception as e:
        logger.error(f"Text ingestion failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Text ingestion failed: {str(e)}",
        )


@router.post(
    "/image",
    response_model=IngestResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid input"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Atomize image content",
    description=(
        "Atomize image into pixel-level atoms.\n\n"
        "Each pixel becomes a content-addressable atom with:\n"
        "- RGB color values\n"
        "- Spatial position (x, y)\n"
        "- Hilbert curve indexing for compression\n\n"
        "Supported formats: JPEG, PNG, GIF, BMP\n"
        "Max size: 10000x10000 pixels\n\n"
        "Performance: ~50ms for 1M pixels (vectorized)"
    ),
)
async def ingest_image(
    request: ImageIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
) -> IngestResponse:
    """
    Atomize image content.

    Example:
        ```json
        {
            "image_data": "iVBORw0KGgoAAAANS...",
            "width": 100,
            "height": 100,
            "metadata": {
                "filename": "test.png",
                "format": "PNG"
            }
        }
        ```

    Returns:
        IngestResponse with atom_count and root_atom_id
    """
    start_time = time.time()

    try:
        # Decode base64
        image_bytes = base64.b64decode(request.image_data)

        # Atomize image
        result = await AtomizationService.atomize_image(
            conn=conn,
            image_data=image_bytes,
            width=request.width,
            height=request.height,
            metadata=request.metadata,
        )

        processing_time = (time.time() - start_time) * 1000

        logger.info(
            f"Image ingestion complete: {request.width}x{request.height} = "
            f"{result['atom_count']} pixels in {processing_time:.2f}ms"
        )

        return IngestResponse(
            atom_count=result["atom_count"],
            root_atom_id=result["root_atom_id"],
            processing_time_ms=processing_time,
            message=(
                f"Successfully atomized {request.width}x{request.height} image "
                f"({result['atom_count']} pixels)"
            ),
        )

    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    except Exception as e:
        logger.error(f"Image ingestion failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Image ingestion failed: {str(e)}",
        )


@router.post(
    "/audio",
    response_model=IngestResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid input"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Atomize audio content",
    description=(
        "Atomize audio into sample-level atoms.\n\n"
        "Each sample becomes a content-addressable atom with:\n"
        "- Time offset (ms)\n"
        "- Amplitude value\n"
        "- Sparse encoding (only significant samples)\n\n"
        "Supported formats: WAV (16-bit)\n"
        "Max sample rate: 192kHz\n"
        "Max channels: 8\n\n"
        "Performance: Real-time or better"
    ),
)
async def ingest_audio(
    request: AudioIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
) -> IngestResponse:
    """
    Atomize audio content.

    Example:
        ```json
        {
            "audio_data": "UklGRiQAAABXQVZFZm10...",
            "sample_rate": 44100,
            "channels": 2,
            "metadata": {
                "filename": "test.wav",
                "duration_ms": 5000
            }
        }
        ```

    Returns:
        IngestResponse with atom_count and root_atom_id
    """
    start_time = time.time()

    try:
        # Decode base64
        audio_bytes = base64.b64decode(request.audio_data)

        # Atomize audio
        result = await AtomizationService.atomize_audio(
            conn=conn,
            audio_data=audio_bytes,
            sample_rate=request.sample_rate,
            channels=request.channels,
            metadata=request.metadata,
        )

        processing_time = (time.time() - start_time) * 1000

        logger.info(
            f"Audio ingestion complete: {request.sample_rate}Hz x "
            f"{request.channels}ch = {result['atom_count']} samples "
            f"in {processing_time:.2f}ms"
        )

        return IngestResponse(
            atom_count=result["atom_count"],
            root_atom_id=result["root_atom_id"],
            processing_time_ms=processing_time,
            message=(
                f"Successfully atomized audio " f"({result['atom_count']} samples)"
            ),
        )

    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    except Exception as e:
        logger.error(f"Audio ingestion failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Audio ingestion failed: {str(e)}",
        )


__all__ = ["router"]
