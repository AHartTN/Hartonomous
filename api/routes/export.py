"""
Export routes for model serialization.

POST /v1/export/onnx  - Export model to ONNX format

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
import time

from fastapi import APIRouter, Depends, HTTPException, status
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.models.export import OnnxExportRequest, OnnxExportResponse
from api.models.ingest import ErrorResponse
from api.services.export import ExportService

router = APIRouter()
logger = logging.getLogger(__name__)


@router.post(
    "/onnx",
    response_model=OnnxExportResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid input"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Export model to ONNX format",
    description=(
        "Export trained model (atoms + relations) to ONNX format.\n\n"
        "ONNX (Open Neural Network Exchange) is a standard format for ML models.\n"
        "Compatible with:\n"
        "- ONNX Runtime\n"
        "- TensorFlow\n"
        "- PyTorch\n"
        "- Scikit-learn\n"
        "- Edge deployment (mobile, IoT)\n\n"
        "Export includes:\n"
        "- Atom embeddings (spatial positions)\n"
        "- Relation weights (synaptic connections)\n"
        "- Model metadata\n\n"
        "Use cases:\n"
        "- Deploy to production\n"
        "- Edge inference (offline)\n"
        "- Model versioning\n"
        "- Cross-platform compatibility\n\n"
        "Performance: O(N) where N = atom count"
    ),
)
async def export_onnx(
    request: OnnxExportRequest, conn: AsyncConnection = Depends(get_db_connection)
) -> OnnxExportResponse:
    """
    Export model to ONNX format.

    Example:
        ```json
        {
            "atom_ids": [1, 2, 3, 4, 5],
            "model_name": "my_model",
            "output_path": "/models/my_model.onnx"
        }
        ```

    Returns:
        OnnxExportResponse with file path and statistics
    """
    start_time = time.time()

    try:
        # Export
        result = await ExportService.export_to_onnx(
            conn=conn,
            atom_ids=request.atom_ids,
            model_name=request.model_name,
            output_path=request.output_path,
        )

        processing_time = (time.time() - start_time) * 1000

        logger.info(
            f"ONNX export complete: '{request.model_name}' "
            f"({result['atom_count']} atoms, {result['relation_count']} relations) "
            f"in {processing_time:.2f}ms"
        )

        return OnnxExportResponse(
            model_name=request.model_name,
            output_path=result["output_path"],
            atom_count=result["atom_count"],
            relation_count=result["relation_count"],
            file_size_bytes=result["file_size_bytes"],
            processing_time_ms=processing_time,
            message=(
                f"Model '{request.model_name}' exported to {result['output_path']} "
                f"({result['atom_count']} atoms, {result['relation_count']} relations)"
            ),
        )

    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    except Exception as e:
        logger.error(f"ONNX export failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Export failed: {str(e)}",
        )


__all__ = ["router"]
