"""GitHub ingest request model."""

from typing import Optional

from pydantic import BaseModel, Field


class GitHubIngestRequest(BaseModel):
    """Request model for GitHub repository ingestion."""

    repo_url: str = Field(..., description="GitHub repository URL")
    branch: str = Field(default="main", description="Branch to clone")
    max_files: int = Field(default=1000, description="Maximum files to process")
    metadata: Optional[dict] = Field(
        default=None, description="Optional metadata (JSON)"
    )
