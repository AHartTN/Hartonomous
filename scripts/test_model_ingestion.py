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
        # Find Qwen3-Coder model
        models_dir = Path("D:/Models/blobs")

        # List available models
        print("Available models in D:/Models/blobs:")
        gguf_files = list(models_dir.glob("sha256-*"))

        for i, model_file in enumerate(gguf_files[:5], 1):
            size_gb = model_file.stat().st_size / 1e9
            print(f"{i}. {model_file.name[:20]}... ({size_gb:.2f} GB)")

        # Use Qwen3-Coder (18GB file)
        qwen_file = Path(
            "D:/Models/blobs/sha256-1194192cf2a187eb02722edcc3f77b11d21f537048ce04b67ccf8ba78863006a"
        )

        if not qwen_file.exists():
            print(f"\nModel file not found: {qwen_file}")
            print("Please update the path to point to a valid GGUF model.")
            return

        print(f"\n?? Atomizing: {qwen_file.name}")
        print(f"   Size: {qwen_file.stat().st_size / 1e9:.2f} GB")

        # Create atomizer (with higher threshold for testing)
        atomizer = GGUFAtomizer(threshold=0.1)

        # Atomize (limit to 5 tensors for testing)
        result = await atomizer.atomize_model(
            file_path=qwen_file,
            model_name="Qwen3-Coder-30B",
            conn=conn,
            max_tensors=5,  # Test with just 5 tensors
        )

        print("\n? Atomization complete!")
        print(f"   Model: {result['model_name']}")
        print(f"   Tensors processed: {result['tensors_processed']}")
        print(f"   Total weights: {result['total_weights']:,}")
        print(f"   Total atoms: {result['total_atoms']:,}")
        print(f"   Unique atoms: {result['unique_atoms']:,}")
        print(f"   Deduplication: {result['deduplication_ratio']:.1f}x")

        # Query some atoms
        print("\n?? Sample atoms:")
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT canonical_text, modality, subtype, 
                       metadata->>'value' as weight_value
                FROM atom
                WHERE modality = 'ml-model'
                  AND subtype = 'weight'
                LIMIT 5
            """
            )

            async for row in cur:
                print(f"   - {row[0]} (subtype={row[2]}, value={row[3]})")

    finally:
        await conn.close()


if __name__ == "__main__":
    print("=" * 60)
    print("GGUF Model Atomization Test")
    print("=" * 60)

    asyncio.run(test_gguf_atomization())
