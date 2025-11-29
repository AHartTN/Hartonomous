"""
Test script for GGUF model atomization.

Run this to test atomizing your Ollama models in D:/Models
"""

import asyncio
import sys
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from api.services.model_atomization import GGUFAtomizer
from psycopg import AsyncConnection
from api.config import settings


async def test_gguf_atomization():
    """Test atomizing a GGUF model."""

    # Connect to database
    conn = await AsyncConnection.connect(settings.get_connection_string())

    try:
        # Find GGUF models in Ollama directory
        models_dir = Path("D:/Models/blobs")

        # List available models, sorted by size (smallest first)
        print("Available models in D:/Models/blobs:")
        gguf_files = []
        
        for model_file in models_dir.glob("sha256-*"):
            # Filter out incomplete downloads (files with only partial hashes or 0 bytes)
            if model_file.stat().st_size > 1_000_000:  # At least 1MB
                gguf_files.append(model_file)
        
        # Sort by size (smallest to largest)
        gguf_files.sort(key=lambda f: f.stat().st_size)
        
        if not gguf_files:
            print("No valid GGUF models found in D:/Models/blobs")
            print("Please ensure you have Ollama models downloaded.")
            return

        for i, model_file in enumerate(gguf_files[:10], 1):
            size_gb = model_file.stat().st_size / 1e9
            print(f"{i}. {model_file.name[:20]}... ({size_gb:.2f} GB)")

        # Create atomizer (with higher threshold for testing)
        atomizer = GGUFAtomizer(threshold=0.1)

        # Atomize (limit to 5 tensors for testing)
        result = await atomizer.atomize_model(
            file_path=test_file,
            model_name=f"test-model-{test_file.name[:16]}",
            conn=conn,
            max_tensors=5,  # Test with just 5 tensors
        ) Atomize (limit to 5 tensors for testing)
        result = await atomizer.atomize_model(
            file_path=qwen_file,
            model_name="Qwen3-Coder-30B",
            conn=conn,
            max_tensors=5,  # Test with just 5 tensors
        )

        print("\n? Atomization complete!")
        print(f"   Model: {result['model_name']}")
        print(f"   Tensors processed: {result['tensors_processed']}")
        print(f"   Total weights: {result['total_processed']:,}")
        print(f"   Total atoms: {result['atoms_created']:,}")
        print(f"   Sparse skipped: {result['sparse_skipped']:,}")
        print(f"   Deduplication: {result['deduplication_ratio']:.1f}x")
        print(f"   Sparse: {result['sparse_percentage']:.1f}%")

        # Query some atoms
        print("\n?? Sample atoms:")
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT canonical_text, 
                       metadata->>'modality' as modality,
                       metadata->>'subtype' as subtype,
                       metadata->>'value' as weight_value
                FROM atom
                WHERE metadata->>'modality' = 'weight'
                LIMIT 5
            """
            )

            async for row in cur:
                print(f"   - {row[0][:50] if row[0] else 'N/A'} (modality={row[1]}, value={row[3]})")

    finally:
        await conn.close()


if __name__ == "__main__":
    print("=" * 60)
    print("GGUF Model Atomization Test")
    print("=" * 60)

    # Fix for Windows ProactorEventLoop issue with psycopg
    import sys
    if sys.platform == 'win32':
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    asyncio.run(test_gguf_atomization())
