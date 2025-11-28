"""GitHub ingest response model."""

from pydantic import BaseModel


class GitHubIngestResponse(BaseModel):
    """Response model for GitHub ingestion."""

    success: bool
    repo_url: str
    branch: str
    total_files: int
    code_files: int
    text_files: int
    image_files: int
    total_atoms: int
    unique_atoms: int
    message: str
