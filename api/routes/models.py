"""
Model ingestion routes - GGUF, SafeTensors, PyTorch, ONNX atomization.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from pathlib import Path

from fastapi import APIRouter, Depends, HTTPException
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.services.model_atomization import GGUFAtomizer
from .model_ingest_request import ModelIngestRequest
from .model_ingest_response import ModelIngestResponse

logger = logging.getLogger(__name__)
router = APIRouter()


@router.post("/model", response_model=ModelIngestResponse)
async def ingest_model(
    request: ModelIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
):
    """Ingest AI model into cognitive substrate."""
    try:
        logger.info(f"Ingesting {request.model_format} model: {request.model_name}")

        model_path = Path(request.model_path)
        if not model_path.exists():
            raise HTTPException(status_code=404, detail=f"Model file not found: {request.model_path}")

        if request.model_format.lower() == "gguf":
            atomizer = GGUFAtomizer(threshold=request.threshold)
            result = await atomizer.atomize_model(
                file_path=model_path,
                model_name=request.model_name,
                conn=conn,
                max_tensors=request.max_tensors,
            )

            return ModelIngestResponse(
                success=True,
                model_name=result["model_name"],
                model_format="gguf",
                file_size_gb=result["file_size_gb"],
                tensors_processed=result["tensors_processed"],
                total_weights=result["total_weights"],
                total_atoms=result["total_atoms"],
                unique_atoms=result["unique_atoms"],
                deduplication_ratio=result["deduplication_ratio"],
                message=f"Successfully atomized {request.model_name} "
                        f"({result['deduplication_ratio']:.1f}x deduplication)",
            )

        elif request.model_format.lower() == "safetensors":
            raise HTTPException(
                status_code=501,
                detail="SafeTensors atomization not yet implemented. Coming soon!"
            )

        elif request.model_format.lower() in ["pytorch", "pt", "pth"]:
            raise HTTPException(
                status_code=501,
                detail="PyTorch atomization not yet implemented. Coming soon!"
            )

        elif request.model_format.lower() == "onnx":
            raise HTTPException(
                status_code=501,
                detail="ONNX atomization not yet implemented. Coming soon!"
            )

        else:
            raise HTTPException(
                status_code=400,
                detail=f"Unsupported model format: {request.model_format}. "
                       f"Supported: gguf, safetensors, pytorch, onnx"
            )

    except Exception as e:
        logger.error(f"Model ingestion failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/model/formats")
async def get_supported_formats():
    """Get list of supported model formats."""
    return {
        "formats": {
            "gguf": {
                "status": "available",
                "description": "Quantized models from Ollama (Llama, Qwen, Mistral)",
                "extensions": [".gguf"],
            },
            "safetensors": {
                "status": "coming_soon",
                "description": "Hugging Face SafeTensors format",
                "extensions": [".safetensors"],
            },
            "pytorch": {
                "status": "coming_soon",
                "description": "PyTorch model checkpoints",
                "extensions": [".pt", ".pth"],
            },
            "onnx": {
                "status": "coming_soon",
                "description": "ONNX model format",
                "extensions": [".onnx"],
            },
        }
    }
