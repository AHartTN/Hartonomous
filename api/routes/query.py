"""
Query routes for retrieving atoms and provenance.

GET /v1/query/atoms/{atom_id}           - Get atom by ID
GET /v1/query/atoms/{atom_id}/lineage   - Get provenance lineage
POST /v1/query/search                   - Spatial search

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
import time

from fastapi import APIRouter, Depends, HTTPException, Path, Query, status
from psycopg import AsyncConnection

from api.dependencies import get_db_connection
from api.models.ingest import ErrorResponse
from api.models.query import (AtomResponse, LineageNode, LineageResponse,
                              SearchRequest, SearchResponse, SearchResult)
from api.services.query import QueryService

router = APIRouter()
logger = logging.getLogger(__name__)


@router.get(
    "/atoms/{atom_id}",
    response_model=AtomResponse,
    responses={
        404: {"model": ErrorResponse, "description": "Atom not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Get atom by ID",
    description=(
        "Retrieve a single atom by its unique ID.\n\n"
        "Returns:\n"
        "- Content hash (SHA-256)\n"
        "- Canonical text (if applicable)\n"
        "- Spatial position (3D coordinates)\n"
        "- Metadata (JSON)\n"
        "- Creation timestamp"
    ),
)
async def get_atom(
    atom_id: int = Path(..., description="Unique atom ID", ge=1),
    conn: AsyncConnection = Depends(get_db_connection),
) -> AtomResponse:
    """
    Get atom by ID.

    Example:
        GET /v1/query/atoms/12345
    """
    try:
        atom = await QueryService.get_atom_by_id(conn, atom_id)

        if not atom:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Atom {atom_id} not found",
            )

        return AtomResponse(**atom)

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to retrieve atom {atom_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to retrieve atom: {str(e)}",
        )


@router.get(
    "/atoms/{atom_id}/lineage",
    response_model=LineageResponse,
    responses={
        404: {"model": ErrorResponse, "description": "Atom not found"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Get atom provenance lineage",
    description=(
        "Retrieve complete provenance lineage for an atom.\n\n"
        "Traces back through composition hierarchy to find all ancestors.\n"
        "Uses Apache AGE for graph queries (<10ms for 50-hop lineage).\n\n"
        "Example use cases:\n"
        "- 'Where did this atom come from?'\n"
        "- 'What was the source document?'\n"
        "- 'How was this generated?'\n\n"
        "Performance: <10ms for 50 hops"
    ),
)
async def get_atom_lineage(
    atom_id: int = Path(..., description="Unique atom ID", ge=1),
    max_depth: int = Query(
        default=50, ge=1, le=100, description="Maximum depth to traverse"
    ),
    conn: AsyncConnection = Depends(get_db_connection),
) -> LineageResponse:
    """
    Get atom provenance lineage.

    Example:
        GET /v1/query/atoms/12345/lineage?max_depth=50
    """
    try:
        # First check if atom exists
        atom = await QueryService.get_atom_by_id(conn, atom_id)
        if not atom:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Atom {atom_id} not found",
            )

        # Get lineage
        lineage_data = await QueryService.get_atom_lineage(conn, atom_id, max_depth)

        # Convert to response model
        nodes = [LineageNode(**node) for node in lineage_data["nodes"]]

        return LineageResponse(
            root_atom_id=atom_id,
            max_depth=max_depth,
            nodes=nodes,
            total_ancestors=lineage_data["total_ancestors"],
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Failed to retrieve lineage for atom {atom_id}: {e}", exc_info=True
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to retrieve lineage: {str(e)}",
        )


@router.post(
    "/search",
    response_model=SearchResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid query"},
        500: {"model": ErrorResponse, "description": "Server error"},
    },
    summary="Spatial search for similar atoms",
    description=(
        "Search for atoms spatially near a query.\n\n"
        "Query can be:\n"
        "- Text string (finds similar semantic concepts)\n"
        "- Atom ID (finds spatial neighbors)\n\n"
        "Uses R-tree spatial index for O(log N) KNN queries.\n\n"
        "Example queries:\n"
        "- 'cat' ? finds 'dog', 'feline', 'whiskers', etc.\n"
        "- '12345' ? finds atoms near atom 12345\n\n"
        "Performance: <10ms for 1M atoms"
    ),
)
async def search_atoms(
    request: SearchRequest, conn: AsyncConnection = Depends(get_db_connection)
) -> SearchResponse:
    """
    Spatial search for similar atoms.

    Example:
        ```json
        {
            "query": "cat",
            "limit": 10,
            "radius": 0.5
        }
        ```
    """
    start_time = time.time()

    try:
        # Perform search
        results_data = await QueryService.spatial_search(
            conn=conn, query=request.query, limit=request.limit, radius=request.radius
        )

        processing_time = (time.time() - start_time) * 1000

        # Convert to response model
        results = [SearchResult(**r) for r in results_data]

        logger.info(
            f"Search for '{request.query}': {len(results)} results "
            f"in {processing_time:.2f}ms"
        )

        return SearchResponse(
            query=request.query,
            results=results,
            total_count=len(results),
            processing_time_ms=processing_time,
        )

    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    except Exception as e:
        logger.error(f"Search failed for '{request.query}': {e}", exc_info=True)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Search failed: {str(e)}",
        )


__all__ = ["router"]
