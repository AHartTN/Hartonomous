"""
Model ingestion routes - GGUF, SafeTensors, PyTorch, ONNX atomization.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from pathlib import Path
from typing import Optional

from fastapi import APIRouter, Depends, HTTPException, UploadFile, File
from psycopg import AsyncConnection
from pydantic import BaseModel, Field

from api.dependencies import get_db_connection
from api.services.model_atomization import GGUFAtomizer

logger = logging.getLogger(__name__)
router = APIRouter()


class ModelIngestRequest(BaseModel):
    """Request model for AI model ingestion."""

    model_path: str = Field(..., description="Path to model file on server")
    model_name: str = Field(..., description="Human-readable model name")
    model_format: str = Field(..., description="Model format: gguf, safetensors, pytorch, onnx")
    threshold: float = Field(default=0.01, description="Sparse encoding threshold")
    max_tensors: Optional[int] = Field(default=None, description="Max tensors (for testing)")


class ModelIngestResponse(BaseModel):
    """Response model for model ingestion."""

    success: bool
    model_name: str
    model_format: str
    file_size_gb: float
    tensors_processed: int
    total_weights: int
    total_atoms: int
    unique_atoms: int
    deduplication_ratio: float
    message: str


@router.post("/model", response_model=ModelIngestResponse)
async def ingest_model(
    request: ModelIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
):
    """
    Ingest AI model into cognitive substrate.
    
    Supported formats:
    - **GGUF**: Llama, Qwen, Mistral (quantized models from Ollama)
    - **SafeTensors**: Hugging Face models [Coming soon]
    - **PyTorch**: .pt, .pth files [Coming soon]
    - **ONNX**: .onnx files [Coming soon]
    
    Process:
    1. Parse model file (extract tensors/layers)
    2. Deduplicate weights via content addressing (SHA-256)
    3. Apply sparse encoding (ignore weights below threshold)
    4. Assign Hilbert indices (spatial positioning)
    5. Bulk insert atoms into PostgreSQL
    6. Create hierarchical compositions (model ? layers ? weights)
    
    Result:
    - Model knowledge becomes queryable in same space as code
    - Truth convergence detects patterns across models
    - Hebbian learning strengthens useful weight patterns
    """
    try:
        logger.info(f"Ingesting {request.model_format} model: {request.model_name}")

        # Validate file exists
        model_path = Path(request.model_path)
        if not model_path.exists():
            raise HTTPException(status_code=404, detail=f"Model file not found: {request.model_path}")

        # Route to appropriate atomizer
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
    """
    Get list of supported model formats.
    """
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
