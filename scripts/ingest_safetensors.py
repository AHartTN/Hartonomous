"""
Production script for ingesting SafeTensors models into Hartonomous.

Usage:
    python scripts/ingest_safetensors.py <model_path> --name MODEL_NAME [options]
    
Example:
    python scripts/ingest_safetensors.py .cache/embedding_models/all-MiniLM-L6-v2/model.safetensors \
        --name "all-MiniLM-L6-v2" \
        --config .cache/embedding_models/all-MiniLM-L6-v2/config.json \
        --tokenizer .cache/embedding_models/all-MiniLM-L6-v2/tokenizer.json
"""

import asyncio
import sys
import argparse
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from api.services.safetensors_atomization import SafeTensorsAtomizer
from psycopg import AsyncConnection
from api.config import settings


async def ingest_safetensors(
    model_path: Path,
    model_name: str,
    config_path: Optional[Path] = None,
    tokenizer_path: Optional[Path] = None,
    max_tensors: Optional[int] = None,
    threshold: float = 1e-6,
):
    """Ingest a SafeTensors model into the database."""
    
    if not model_path.exists():
        print(f"Error: Model file not found: {model_path}")
        return False
    
    size_mb = model_path.stat().st_size / 1e6
    print("=" * 70)
    print(f"INGESTING SAFETENSORS MODEL: {model_name}")
    print(f"File: {model_path}")
    print(f"Size: {size_mb:.2f} MB")
    if config_path:
        print(f"Config: {config_path}")
    if tokenizer_path:
        print(f"Tokenizer: {tokenizer_path}")
    if max_tensors:
        print(f"Limit: {max_tensors} tensors (partial ingestion)")
    print("=" * 70)
    
    # Connect to database
    print("\n[1/4] Connecting to database...")
    conn = await AsyncConnection.connect(settings.get_connection_string())
    
    try:
        print("[2/4] Creating atomizer...")
        atomizer = SafeTensorsAtomizer(threshold=threshold)
        
        print(f"[3/4] Atomizing model (threshold={threshold})...")
        result = await atomizer.atomize_model(
            file_path=model_path,
            model_name=model_name,
            conn=conn,
            config_path=config_path,
            tokenizer_path=tokenizer_path,
            max_tensors=max_tensors,
        )
        
        print("\n[4/4] Ingestion complete!")
        print("\n" + "=" * 70)
        print("INGESTION SUMMARY")
        print("=" * 70)
        print(f"Model: {result['model_name']}")
        print(f"Model Atom ID: {result['model_atom_id']}")
        print(f"File Size: {result['file_size_gb']:.2f} GB")
        print(f"Tensors Processed: {result['tensors_processed']}")
        print(f"Total Weights: {result['total_processed']:,}")
        print(f"Atoms Created: {result['atoms_created']:,}")
        print(f"Atoms Deduplicated: {result['atoms_deduped']:,}")
        print(f"Sparse Skipped: {result['sparse_skipped']:,}")
        print(f"Deduplication Ratio: {result['deduplication_ratio']:.1f}x")
        print(f"Sparse Percentage: {result['sparse_percentage']:.1f}%")
        print("=" * 70)
        
        # Sample atoms
        print("\nSample atoms from database:")
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT 
                    canonical_text, 
                    metadata->>'modality' as modality,
                    metadata->>'token_id' as token_id,
                    metadata->>'shape' as shape
                FROM atom
                WHERE metadata->>'modality' IN ('tokenizer/vocabulary', 'tensor')
                ORDER BY atom_id DESC
                LIMIT 5
                """
            )
            
            rows = await cur.fetchall()
            for row in rows:
                text = row[0][:50] if row[0] else 'N/A'
                modality = row[1]
                if modality == 'tokenizer/vocabulary':
                    print(f"  • Token {row[2]}: '{text}'")
                elif modality == 'tensor':
                    print(f"  • Tensor: {text} {row[3]}")
        
        return True
        
    except Exception as e:
        print(f"\n❌ Error during ingestion: {e}")
        import traceback
        traceback.print_exc()
        return False
    
    finally:
        await conn.close()


def main():
    """Parse arguments and run ingestion."""
    from typing import Optional
    
    parser = argparse.ArgumentParser(
        description="Ingest SafeTensors models into Hartonomous",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Ingest embedding model with config and tokenizer
  python scripts/ingest_safetensors.py \\
      .cache/embedding_models/all-MiniLM-L6-v2/model.safetensors \\
      --name "all-MiniLM-L6-v2" \\
      --config .cache/embedding_models/all-MiniLM-L6-v2/config.json \\
      --tokenizer .cache/embedding_models/all-MiniLM-L6-v2/tokenizer.json
  
  # Ingest just the model weights (no config/tokenizer)
  python scripts/ingest_safetensors.py model.safetensors --name "MyModel"
  
  # Test with first 5 tensors only
  python scripts/ingest_safetensors.py model.safetensors --name "Test" --max-tensors 5
        """
    )
    
    parser.add_argument(
        "model_path",
        type=str,
        help="Path to SafeTensors model file"
    )
    
    parser.add_argument(
        "--name",
        type=str,
        required=True,
        help="Name for the model (e.g., 'all-MiniLM-L6-v2')"
    )
    
    parser.add_argument(
        "--config",
        type=str,
        default=None,
        help="Path to config.json file"
    )
    
    parser.add_argument(
        "--tokenizer",
        type=str,
        default=None,
        help="Path to tokenizer.json file"
    )
    
    parser.add_argument(
        "--max-tensors",
        type=int,
        default=None,
        help="Maximum number of tensors to process (for testing)"
    )
    
    parser.add_argument(
        "--threshold",
        type=float,
        default=1e-6,
        help="Sparsity threshold (default: 1e-6)"
    )
    
    args = parser.parse_args()
    
    # Convert paths
    model_path = Path(args.model_path)
    config_path = Path(args.config) if args.config else None
    tokenizer_path = Path(args.tokenizer) if args.tokenizer else None
    
    # Fix for Windows ProactorEventLoop issue with psycopg
    if sys.platform == 'win32':
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    
    # Run ingestion
    success = asyncio.run(ingest_safetensors(
        model_path=model_path,
        model_name=args.name,
        config_path=config_path,
        tokenizer_path=tokenizer_path,
        max_tensors=args.max_tensors,
        threshold=args.threshold,
    ))
    
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
