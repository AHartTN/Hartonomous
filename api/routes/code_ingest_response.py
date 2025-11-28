"""Code ingestion response model."""

from pydantic import BaseModel, Field


class CodeIngestResponse(BaseModel):
    """Response model for code ingestion."""

    success: bool = Field(..., description="Whether ingestion succeeded")
    total_atoms: int = Field(..., description="Total atoms created/referenced")
    unique_atoms: int = Field(..., description="New unique atoms created")
    compositions: int = Field(..., description="Composition relationships created")
    relations: int = Field(..., description="Semantic relations created")
    message: str = Field(..., description="Success/error message")

    class Config:
        json_schema_extra = {
            "example": {
                "success": True,
                "total_atoms": 150,
                "unique_atoms": 45,
                "compositions": 120,
                "relations": 30,
                "message": "Successfully atomized Example.cs (1234 bytes)",
            }
        }
