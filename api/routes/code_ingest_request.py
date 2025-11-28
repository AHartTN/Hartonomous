"""Code ingestion request model."""

from typing import Any, Dict, Optional

from pydantic import BaseModel, Field


class CodeIngestRequest(BaseModel):
    """Request model for code ingestion."""

    code: str = Field(..., description="Source code to atomize")
    filename: str = Field(..., description="Filename for context")
    language: str = Field(
        default="csharp",
        description="Programming language (csharp, python, java, javascript, etc.)",
    )
    metadata: Optional[Dict[str, Any]] = Field(
        default=None, description="Additional metadata"
    )

    class Config:
        json_schema_extra = {
            "example": {
                "code": "public class Example { public int Value { get; set; } }",
                "filename": "Example.cs",
                "language": "csharp",
                "metadata": {"project": "MyProject", "namespace": "MyNamespace"},
            }
        }
