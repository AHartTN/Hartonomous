"""FastAPI endpoints for ingestion pipeline."""

from fastapi import APIRouter, UploadFile, File, HTTPException
from pathlib import Path
import tempfile

from ..ingestion.parsers import ModelParser, ImageParser
from ..db.ingestion_writer import IngestionWriter

router = APIRouter(prefix="/api/v1/ingest", tags=["ingestion"])


@router.post("/model")
async def ingest_model(file: UploadFile = File(...)):
    with tempfile.NamedTemporaryFile(delete=False, suffix=Path(file.filename).suffix) as tmp:
        content = await file.read()
        tmp.write(content)
        tmp_path = Path(tmp.name)
    
    try:
        parser = ModelParser()
        records = list(parser.parse(tmp_path))
        return {"status": "success", "records_ingested": len(records), "filename": file.filename}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        tmp_path.unlink()


@router.get("/status")
async def ingestion_status():
    return {
        "status": "operational",
        "supported_formats": {
            "models": [".pt", ".pth"],
            "images": [".jpg", ".png"]
        }
    }
