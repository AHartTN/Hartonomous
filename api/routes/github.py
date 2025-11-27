"""
GitHub repository ingestion - atomize entire repos into cognitive substrate.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
import os
import tempfile
from typing import Any, Dict, Optional
from urllib.parse import urlparse

import httpx
from fastapi import APIRouter, Depends, HTTPException
from psycopg import AsyncConnection
from pydantic import BaseModel, Field

from api.dependencies import get_db_connection
from api.services.code_atomization import CodeAtomizationService

logger = logging.getLogger(__name__)
router = APIRouter()


class GitHubIngestRequest(BaseModel):
    """Request model for GitHub repository ingestion."""

    repo_url: str = Field(..., description="GitHub repository URL")
    branch: str = Field(default="main", description="Branch to clone")
    max_files: int = Field(default=1000, description="Maximum files to process")
    metadata: Optional[dict] = Field(
        default=None, description="Optional metadata (JSON)"
    )


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


@router.post("/github", response_model=GitHubIngestResponse)
async def ingest_github_repo(
    request: GitHubIngestRequest, conn: AsyncConnection = Depends(get_db_connection)
):
    """
    Ingest entire GitHub repository into cognitive substrate.

    Process:
    1. Shallow clone repository
    2. Detect file types
    3. Route to appropriate atomizers:
       - .cs, .py, .js, etc. ? Code atomizer (Roslyn/Tree-sitter)
       - .md, .txt, .json ? Text atomizer
       - .png, .jpg ? Image atomizer
       - etc.
    4. All atoms feed same substrate (no differentiation)
    5. Truth convergence finds patterns
    6. Hebbian learning strengthens connections

    **The substrate doesn't care about file type.**
    **Everything becomes atoms with spatial positions.**
    """
    try:
        logger.info(f"Ingesting GitHub repo: {request.repo_url}")

        # Parse GitHub URL
        parsed = urlparse(request.repo_url)
        if "github.com" not in parsed.netloc:
            raise ValueError("Only GitHub URLs supported")

        # Extract owner/repo
        path_parts = parsed.path.strip("/").split("/")
        if len(path_parts) < 2:
            raise ValueError("Invalid GitHub URL format")

        owner, repo = path_parts[0], path_parts[1]

        # Clone repository (shallow)
        with tempfile.TemporaryDirectory() as tmpdir:
            clone_dir = os.path.join(tmpdir, repo)

            # Use GitHub CLI or git command
            import subprocess

            result = subprocess.run(
                [
                    "git",
                    "clone",
                    "--depth=1",
                    "--single-branch",
                    f"--branch={request.branch}",
                    request.repo_url,
                    clone_dir,
                ],
                capture_output=True,
                text=True,
            )

            if result.returncode != 0:
                raise RuntimeError(f"Git clone failed: {result.stderr}")

            # Walk directory tree
            total_files = 0
            code_files = 0
            text_files = 0
            image_files = 0
            total_atoms = 0
            unique_atoms = 0

            code_service = CodeAtomizationService()

            for root, dirs, files in os.walk(clone_dir):
                # Skip .git directory
                if ".git" in root:
                    continue

                for filename in files:
                    if total_files >= request.max_files:
                        break

                    filepath = os.path.join(root, filename)
                    total_files += 1

                    try:
                        # Detect file type
                        ext = os.path.splitext(filename)[1].lower()

                        # Route to appropriate atomizer
                        if ext in [
                            ".cs",
                            ".py",
                            ".js",
                            ".ts",
                            ".go",
                            ".rs",
                            ".java",
                            ".cpp",
                            ".c",
                            ".rb",
                            ".php",
                        ]:
                            # Code atomization
                            with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
                                code = f.read()

                            language = detect_language(ext)
                            result = await code_service.atomize_and_insert(
                                conn=conn,
                                code=code,
                                filename=filename,
                                language=language,
                                metadata={"repo": request.repo_url, "branch": request.branch},
                            )

                            code_files += 1
                            total_atoms += result["total_atoms"]
                            unique_atoms += result["unique_atoms"]

                        elif ext in [".md", ".txt", ".json", ".yaml", ".yml", ".toml"]:
                            # Text atomization (TODO: implement text atomizer)
                            text_files += 1

                        elif ext in [".png", ".jpg", ".jpeg", ".gif", ".bmp"]:
                            # Image atomization (TODO: implement image atomizer)
                            image_files += 1

                    except Exception as e:
                        logger.warning(f"Failed to process {filename}: {e}")
                        continue

            return GitHubIngestResponse(
                success=True,
                repo_url=request.repo_url,
                branch=request.branch,
                total_files=total_files,
                code_files=code_files,
                text_files=text_files,
                image_files=image_files,
                total_atoms=total_atoms,
                unique_atoms=unique_atoms,
                message=f"Successfully atomized {total_files} files from {owner}/{repo}",
            )

    except Exception as e:
        logger.error(f"GitHub ingestion failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


def detect_language(ext: str) -> str:
    """Detect language from file extension."""
    mapping = {
        ".cs": "csharp",
        ".py": "python",
        ".js": "javascript",
        ".ts": "typescript",
        ".go": "go",
        ".rs": "rust",
        ".java": "java",
        ".cpp": "cpp",
        ".c": "c",
        ".rb": "ruby",
        ".php": "php",
    }
    return mapping.get(ext, "unknown")
